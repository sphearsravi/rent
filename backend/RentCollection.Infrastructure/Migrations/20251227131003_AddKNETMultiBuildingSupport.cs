using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKNETMultiBuildingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "GovernmentId",
                table: "Tenants",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutPublicKey",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutSecretKey",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETAccountID",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETBankAccountNumber",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETBankName",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETMID",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutPaymentId",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CivilID",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasMismatch",
                table: "Payments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETAccountIDUsed",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KNETMIDUsed",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MismatchDetails",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KNETReconciliations",
                columns: table => new
                {
                    KNETReconciliationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    PropertyId = table.Column<int>(type: "int", nullable: false),
                    TransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CheckoutPaymentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    KNETAccountID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    KNETMID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BankSettlementRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettlementDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReconciliationStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReconciledDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReconciledBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HasDiscrepancy = table.Column<bool>(type: "bit", nullable: false),
                    DiscrepancyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DiscrepancyReason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KNETReconciliations", x => x.KNETReconciliationId);
                    table.ForeignKey(
                        name: "FK_KNETReconciliations_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KNETReconciliations_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "PropertyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAudits",
                columns: table => new
                {
                    PaymentAuditId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    AuditType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AuditTimestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AuditedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsMismatch = table.Column<bool>(type: "bit", nullable: false),
                    MismatchType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MismatchDetails = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpectedCivilID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActualCivilID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpectedBuildingId = table.Column<int>(type: "int", nullable: true),
                    ActualBuildingId = table.Column<int>(type: "int", nullable: true),
                    ExpectedKNETMID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActualKNETMID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpectedLeaseId = table.Column<int>(type: "int", nullable: true),
                    ActualLeaseId = table.Column<int>(type: "int", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAudits", x => x.PaymentAuditId);
                    table.ForeignKey(
                        name: "FK_PaymentAudits_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentIdempotencies",
                columns: table => new
                {
                    PaymentIdempotencyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PaymentId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsProcessed = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LeaseId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CivilID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PropertyId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIdempotencies", x => x.PaymentIdempotencyId);
                    table.ForeignKey(
                        name: "FK_PaymentIdempotencies_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_GovernmentId",
                table: "Tenants",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CheckoutPaymentId",
                table: "Payments",
                column: "CheckoutPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CivilID",
                table: "Payments",
                column: "CivilID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_HasMismatch",
                table: "Payments",
                column: "HasMismatch");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_KNETReconciliations_CheckoutPaymentId",
                table: "KNETReconciliations",
                column: "CheckoutPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_KNETReconciliations_PaymentId",
                table: "KNETReconciliations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_KNETReconciliations_PropertyId",
                table: "KNETReconciliations",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_KNETReconciliations_ReconciliationStatus",
                table: "KNETReconciliations",
                column: "ReconciliationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_KNETReconciliations_TransactionDate",
                table: "KNETReconciliations",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_KNETReconciliations_TransactionId",
                table: "KNETReconciliations",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAudits_AuditTimestamp",
                table: "PaymentAudits",
                column: "AuditTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAudits_IsMismatch",
                table: "PaymentAudits",
                column: "IsMismatch");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAudits_IsResolved",
                table: "PaymentAudits",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAudits_PaymentId",
                table: "PaymentAudits",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIdempotencies_CreatedAt",
                table: "PaymentIdempotencies",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIdempotencies_ExpiresAt",
                table: "PaymentIdempotencies",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIdempotencies_IdempotencyKey",
                table: "PaymentIdempotencies",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIdempotencies_PaymentId",
                table: "PaymentIdempotencies",
                column: "PaymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KNETReconciliations");

            migrationBuilder.DropTable(
                name: "PaymentAudits");

            migrationBuilder.DropTable(
                name: "PaymentIdempotencies");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_GovernmentId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CheckoutPaymentId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CivilID",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_HasMismatch",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CheckoutPublicKey",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "CheckoutSecretKey",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "KNETAccountID",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "KNETBankAccountNumber",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "KNETBankName",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "KNETMID",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "CheckoutPaymentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CivilID",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "HasMismatch",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "KNETAccountIDUsed",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "KNETMIDUsed",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "MismatchDetails",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "GovernmentId",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
