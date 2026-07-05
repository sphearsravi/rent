using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyImageUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_PaymentStatus",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Payments");

            migrationBuilder.AddColumn<bool>(
                name: "AcceptBankTransfer",
                table: "Properties",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptCash",
                table: "Properties",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptKNET",
                table: "Properties",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETMerchantId",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETMerchantKey",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedDate",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectedBy",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedDate",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentType",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BankTransfers",
                columns: table => new
                {
                    BankTransferId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PropertyId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TransferDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ProofFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ProofFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProofFileType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProofFileSize = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransfers", x => x.BankTransferId);
                    table.ForeignKey(
                        name: "FK_BankTransfers_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankTransfers_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "PropertyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashPayments",
                columns: table => new
                {
                    CashPaymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PropertyId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CollectedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashPayments", x => x.CashPaymentId);
                    table.ForeignKey(
                        name: "FK_CashPayments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashPayments_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CashPayments_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "PropertyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashPayments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId",
                table: "Payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentType",
                table: "Payments",
                column: "PaymentType");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PropertyId",
                table: "Payments",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status",
                table: "Payments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_InvoiceId",
                table: "BankTransfers",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_PaymentId",
                table: "BankTransfers",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_PropertyId",
                table: "BankTransfers",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_ReferenceNumber",
                table: "BankTransfers",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_Status",
                table: "BankTransfers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_SubmittedDate",
                table: "BankTransfers",
                column: "SubmittedDate");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_TenantId",
                table: "BankTransfers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_InvoiceId",
                table: "CashPayments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_PaymentDate",
                table: "CashPayments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_PaymentId",
                table: "CashPayments",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_PropertyId",
                table: "CashPayments",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_ReceiptNumber",
                table: "CashPayments",
                column: "ReceiptNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_TenantId",
                table: "CashPayments",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Invoices_InvoiceId",
                table: "Payments",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "InvoiceId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Properties_PropertyId",
                table: "Payments",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "PropertyId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Invoices_InvoiceId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Properties_PropertyId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "BankTransfers");

            migrationBuilder.DropTable(
                name: "CashPayments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_InvoiceId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaymentType",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PropertyId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcceptBankTransfer",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "AcceptCash",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "AcceptKNET",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "KNETMerchantId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "KNETMerchantKey",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ApprovedDate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CollectedBy",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ModifiedDate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentType",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReceiptNumber",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Payments");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentStatus",
                table: "Payments",
                column: "PaymentStatus");
        }
    }
}
