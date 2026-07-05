using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnitClassificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableFromDate",
                table: "Units",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FloorNumber",
                table: "Units",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Units",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeBand",
                table: "Units",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // ── Backfill (Unit revamp Phase A) ──
            // Derive SizeBand from Bedrooms + UnitType so chip filters work
            // for the 675 existing units without operator intervention. Same
            // logic as UnitSizeBands.Derive — kept identical so re-running
            // is a no-op.
            migrationBuilder.Sql(@"
                UPDATE Units
                   SET SizeBand = CASE
                       WHEN UnitType IN ('Commercial', 'Shop', 'Office', 'Warehouse') THEN 'Commercial'
                       WHEN Bedrooms IS NULL OR Bedrooms = 0 THEN 'Studio'
                       WHEN Bedrooms = 1 THEN '1BHK'
                       WHEN Bedrooms = 2 THEN '2BHK'
                       ELSE '3BHK+'
                   END
                 WHERE SizeBand = '';
            ");

            // Status reconciliation — any unit with an active lease becomes
            // Occupied; any without becomes Vacant. Maintenance / Reserved
            // are operator-set holds and stay untouched. This is the same
            // logic the daily Hangfire job will run going forward, applied
            // once here to clear pre-existing drift (the SQL we found showed
            // 668 Occupied + 7 Vacant against 675 leases — likely close,
            // but make sure).
            migrationBuilder.Sql(@"
                ;WITH ActiveLeases AS (
                    SELECT DISTINCT UnitId FROM Leases WHERE Status = 'Active'
                )
                UPDATE u
                   SET u.Status = CASE
                       WHEN a.UnitId IS NOT NULL THEN 'Occupied'
                       ELSE 'Vacant'
                   END,
                       u.ModifiedDate = SYSUTCDATETIME()
                  FROM Units u
                  LEFT JOIN ActiveLeases a ON a.UnitId = u.UnitId
                 WHERE u.Status NOT IN ('Maintenance', 'Reserved');
            ");

            // AvailableFromDate seed — for units now Vacant whose most-recent
            // lease was terminated, stamp the termination date so 'Available
            // from …' shows real history not just blank.
            migrationBuilder.Sql(@"
                ;WITH LastTermination AS (
                    SELECT l.UnitId, MAX(l.TerminationDate) AS LastDate
                      FROM Leases l
                     WHERE l.Status IN ('Terminated', 'Expired', 'Renewed')
                       AND l.TerminationDate IS NOT NULL
                     GROUP BY l.UnitId
                )
                UPDATE u
                   SET u.AvailableFromDate = lt.LastDate
                  FROM Units u
                  JOIN LastTermination lt ON lt.UnitId = u.UnitId
                 WHERE u.Status = 'Vacant'
                   AND u.AvailableFromDate IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailableFromDate",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "FloorNumber",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "SizeBand",
                table: "Units");
        }
    }
}
