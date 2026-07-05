using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentCollection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentLinkTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentLinks",
                columns: table => new
                {
                    DocumentLinkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLinks", x => x.DocumentLinkId);
                    table.ForeignKey(
                        name: "FK_DocumentLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_DocumentId_EntityType_EntityId",
                table: "DocumentLinks",
                columns: new[] { "DocumentId", "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_EntityType_EntityId",
                table: "DocumentLinks",
                columns: new[] { "EntityType", "EntityId" });

            // Backfill: copy every existing Document.RelatedEntityType/Id pair
            // into a DocumentLink row. NOT EXISTS guard makes this idempotent
            // so re-running (e.g. partial rollback then re-apply) doesn't
            // double-insert. The legacy columns stay on Document for a
            // release as a read-fallback; drop them in a later migration
            // once we're confident nothing reads them.
            migrationBuilder.Sql(@"
                INSERT INTO DocumentLinks (DocumentId, EntityType, EntityId, LinkedAt, LinkedBy)
                SELECT d.DocumentId, d.RelatedEntityType, d.RelatedEntityId, d.UploadedDate, d.UploadedBy
                FROM Documents d
                WHERE d.RelatedEntityType IS NOT NULL
                  AND d.RelatedEntityId IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM DocumentLinks l
                      WHERE l.DocumentId = d.DocumentId
                        AND l.EntityType = d.RelatedEntityType
                        AND l.EntityId   = d.RelatedEntityId
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentLinks");
        }
    }
}
