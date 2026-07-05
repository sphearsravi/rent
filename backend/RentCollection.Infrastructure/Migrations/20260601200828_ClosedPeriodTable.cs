using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClosedPeriodTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClosedPeriods",
                columns: table => new
                {
                    ClosedPeriodId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Period = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosedPeriods", x => x.ClosedPeriodId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClosedPeriods_Period",
                table: "ClosedPeriods",
                column: "Period",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosedPeriods");
        }
    }
}
