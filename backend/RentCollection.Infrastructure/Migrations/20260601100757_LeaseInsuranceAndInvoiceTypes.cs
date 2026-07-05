using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaseInsuranceAndInvoiceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DepositInvoiced",
                table: "Leases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceAmount",
                table: "Leases",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "InsuranceFrequency",
                table: "Leases",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "InsuranceStartDate",
                table: "Leases",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepositInvoiced",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "InsuranceAmount",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "InsuranceFrequency",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "InsuranceStartDate",
                table: "Leases");
        }
    }
}
