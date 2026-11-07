using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoir.HomeDocs.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AltText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Folder = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.CheckConstraint("CK_MediaItems_Kind", "\"Kind\" IN ('image','pdf')");
                    table.CheckConstraint("CK_MediaItems_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_MediaItems_Size", "\"Size\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_Folder",
                table: "MediaItems",
                column: "Folder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaItems");
        }
    }
}
