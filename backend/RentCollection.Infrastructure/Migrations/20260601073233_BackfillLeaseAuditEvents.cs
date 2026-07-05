using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <summary>
    /// One-shot backfill of <c>AuditLogs</c> for pre-existing leases — without
    /// this the new /Leases/History/{id} page shows blank for every lease
    /// created before the AuditLog table existed.
    ///
    /// Four event types are synthesised, each with <c>"synthesised": true</c>
    /// in the payload so the UI can mark them as approximate:
    ///
    ///   • <c>Created</c>      — one per lease, at <c>CreatedDate</c>
    ///   • <c>Terminated</c>   — for closed leases, at <c>TerminationDate</c>
    ///   • <c>Renewed</c>      — for predecessors (RenewedToLeaseId IS NOT NULL)
    ///   • <c>Expired</c>      — for currently-Expired leases at end-date + 1
    ///
    /// Every INSERT is gated on <c>NOT EXISTS</c> so re-running this migration
    /// (or running it on a partially-backfilled DB) won't double-insert.
    /// </summary>
    public partial class BackfillLeaseAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─── Created — every lease gets one ──────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO AuditLogs (EntityType, EntityId, Action, ByUser, At, PayloadJson)
                SELECT 'Lease', l.LeaseId, 'Created', 'system:backfill', l.CreatedDate,
                       '{""synthesised"":true,""reason"":""pre-AuditLog row""}'
                  FROM Leases l
                 WHERE NOT EXISTS (
                       SELECT 1 FROM AuditLogs a
                        WHERE a.EntityType = 'Lease'
                          AND a.EntityId   = l.LeaseId
                          AND a.Action     = 'Created'
                 );
            ");

            // ─── Terminated — closed leases with a recorded date ─────────
            // Reason text is shown to operators; escape embedded quotes
            // before splicing into the JSON payload.
            migrationBuilder.Sql(@"
                INSERT INTO AuditLogs (EntityType, EntityId, Action, ByUser, At, PayloadJson)
                SELECT 'Lease', l.LeaseId, 'Terminated',
                       COALESCE(l.TerminatedBy, 'system:backfill'),
                       l.TerminationDate,
                       CONCAT(
                           '{""synthesised"":true,""reason"":""TerminationDate stamp""',
                           CASE WHEN l.TerminationReason IS NOT NULL
                                THEN CONCAT(',""terminationReason"":""',
                                            REPLACE(l.TerminationReason, '""', '\""'),
                                            '""')
                                ELSE '' END,
                           '}')
                  FROM Leases l
                 WHERE l.Status = 'Terminated'
                   AND l.TerminationDate IS NOT NULL
                   AND NOT EXISTS (
                       SELECT 1 FROM AuditLogs a
                        WHERE a.EntityType = 'Lease'
                          AND a.EntityId   = l.LeaseId
                          AND a.Action     = 'Terminated'
                 );
            ");

            // ─── Renewed — predecessors that point to a successor ────────
            migrationBuilder.Sql(@"
                INSERT INTO AuditLogs (EntityType, EntityId, Action, ByUser, At, PayloadJson)
                SELECT 'Lease', l.LeaseId, 'Renewed',
                       COALESCE(l.RenewedBy, 'system:backfill'),
                       COALESCE(l.ModifiedDate, l.CreatedDate),
                       CONCAT('{""synthesised"":true,""newLeaseId"":', l.RenewedToLeaseId, '}')
                  FROM Leases l
                 WHERE l.RenewedToLeaseId IS NOT NULL
                   AND NOT EXISTS (
                       SELECT 1 FROM AuditLogs a
                        WHERE a.EntityType = 'Lease'
                          AND a.EntityId   = l.LeaseId
                          AND a.Action     = 'Renewed'
                 );
            ");

            // ─── Expired — synthesised at end-date + 1 day for currently
            // Expired leases. The exact moment is unknown so this is an
            // approximation; the synthesised flag tells the UI to mark it.
            migrationBuilder.Sql(@"
                INSERT INTO AuditLogs (EntityType, EntityId, Action, ByUser, At, PayloadJson)
                SELECT 'Lease', l.LeaseId, 'Expired', 'system:backfill',
                       DATEADD(day, 1, l.LeaseEndDate),
                       '{""synthesised"":true,""reason"":""past LeaseEndDate""}'
                  FROM Leases l
                 WHERE l.Status = 'Expired'
                   AND NOT EXISTS (
                       SELECT 1 FROM AuditLogs a
                        WHERE a.EntityType = 'Lease'
                          AND a.EntityId   = l.LeaseId
                          AND a.Action     = 'Expired'
                 );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove only the rows this migration created — anything written
            // by application code afterwards stays. Idempotent rollback.
            migrationBuilder.Sql(@"
                DELETE FROM AuditLogs
                 WHERE EntityType = 'Lease'
                   AND ByUser     LIKE 'system:backfill%'
                   AND ISJSON(PayloadJson) = 1
                   AND JSON_VALUE(PayloadJson, '$.synthesised') = 'true';
            ");
        }
    }
}
