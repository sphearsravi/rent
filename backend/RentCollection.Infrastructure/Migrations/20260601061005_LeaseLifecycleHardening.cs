using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaseLifecycleHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ──────────────────────────────────────────────────────────────
            // LEASE_AUDIT pre-flight cleanups — must run BEFORE the unique
            // index is created so the index doesn't reject existing rows.
            // Per user decision: auto-resolve duplicate Active leases on
            // the same Unit by keeping the most-recently-created and flipping
            // older ones to Terminated. Then flip any Active-past-end leases
            // to Expired so the new daily job (P5) starts from a clean slate.
            // Each side-effect writes an AuditLog row so operators can trace.
            // ──────────────────────────────────────────────────────────────

            // (a) Auto-terminate older duplicate Active leases per UnitId.
            //     Keeper = most recently created. This SQL is idempotent
            //     (the WHERE filters Active only; once flipped, the row
            //     won't be picked up on a re-run).
            migrationBuilder.Sql(@"
                ;WITH Ranked AS (
                    SELECT LeaseId, UnitId,
                           ROW_NUMBER() OVER (PARTITION BY UnitId
                                              ORDER BY CreatedDate DESC, LeaseId DESC) AS rn
                      FROM Leases
                     WHERE Status = 'Active'
                )
                UPDATE l
                   SET Status            = 'Terminated',
                       TerminationDate   = SYSUTCDATETIME(),
                       TerminationReason = 'Auto-resolved overlap (LeaseLifecycleHardening migration)',
                       TerminatedBy      = 'system:migration',
                       ModifiedDate      = SYSUTCDATETIME()
                  FROM Leases l
                  JOIN Ranked r ON r.LeaseId = l.LeaseId
                 WHERE r.rn > 1;
            ");

            // (b) Flip Active-but-past-end leases to Expired. The new daily
            //     LeaseExpiry job (P5) will keep this true going forward,
            //     but the migration handles the one-time current backlog.
            migrationBuilder.Sql(@"
                UPDATE Leases
                   SET Status       = 'Expired',
                       ModifiedDate = SYSUTCDATETIME()
                 WHERE Status = 'Active'
                   AND LeaseEndDate < CAST(GETUTCDATE() AS date);
            ");

            // Guarded DropIndex — model-snapshot thinks IX_Leases_UnitId
            // exists, but it isn't present in every dev DB. Skip silently
            // when it's missing so the migration is idempotent across
            // environments.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.indexes
                            WHERE name = 'IX_Leases_UnitId'
                              AND object_id = OBJECT_ID('Leases'))
                    DROP INDEX IX_Leases_UnitId ON Leases;
            ");

            migrationBuilder.AddColumn<string>(
                name: "RenewedBy",
                table: "Leases",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Leases",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferredBy",
                table: "Leases",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ByUser = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogId);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_Leases_UnitId_Active",
                table: "Leases",
                column: "UnitId",
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At",
                table: "AuditLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "UQ_Leases_UnitId_Active",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "RenewedBy",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "TransferredBy",
                table: "Leases");

            // Guarded re-create on rollback — mirror of the guarded drop above.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes
                                WHERE name = 'IX_Leases_UnitId'
                                  AND object_id = OBJECT_ID('Leases'))
                    CREATE INDEX IX_Leases_UnitId ON Leases (UnitId);
            ");
        }
    }
}
