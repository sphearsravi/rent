using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRedirectUrlToPaymentIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RedirectUrl",
                table: "PaymentIdempotencies",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RedirectUrl",
                table: "PaymentIdempotencies");
        }
    }
}
