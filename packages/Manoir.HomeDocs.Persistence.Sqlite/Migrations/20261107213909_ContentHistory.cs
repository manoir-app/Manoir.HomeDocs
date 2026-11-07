using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoir.HomeDocs.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ContentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentPageVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Event = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    ContextsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RequiresAuthentication = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    Markdown = table.Column<string>(type: "TEXT", nullable: false),
                    SavedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    RestoredFromRevision = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPageVersions", x => x.Id);
                    table.CheckConstraint("CK_ContentPageVersions_Event", "\"Event\" IN ('created','edited','published','unpublished','restored','baseline')");
                    table.CheckConstraint("CK_ContentPageVersions_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_ContentPageVersions_ContentPages_PageId",
                        column: x => x.PageId,
                        principalTable: "ContentPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPageVersions_PageId_Revision",
                table: "ContentPageVersions",
                columns: new[] { "PageId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentPageVersions");
        }
    }
}
