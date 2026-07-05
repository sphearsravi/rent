using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePropertyForKuwaitGovernorates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ZipCode",
                table: "Properties",
                newName: "Governorate");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "Properties",
                newName: "Block");

            migrationBuilder.RenameColumn(
                name: "City",
                table: "Properties",
                newName: "Area");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Governorate",
                table: "Properties",
                newName: "ZipCode");

            migrationBuilder.RenameColumn(
                name: "Block",
                table: "Properties",
                newName: "State");

            migrationBuilder.RenameColumn(
                name: "Area",
                table: "Properties",
                newName: "City");
        }
    }
}
