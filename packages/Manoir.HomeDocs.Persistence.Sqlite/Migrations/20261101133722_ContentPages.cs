using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoir.HomeDocs.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ContentPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    ContextsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RequiresAuthentication = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPages", x => x.Id);
                    table.CheckConstraint("CK_ContentPages_Category", "\"Category\" IN ('page','piece','emplacement','procedure','equipement','depannage','technique')");
                    table.CheckConstraint("CK_ContentPages_Contexts", "json_valid(\"ContextsJson\") AND json_type(\"ContextsJson\") = 'array'");
                    table.CheckConstraint("CK_ContentPages_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_ContentPages_Slug", "length(\"Slug\") BETWEEN 1 AND 80 AND \"Slug\" NOT GLOB '*[^a-z0-9-]*' AND \"Slug\" NOT LIKE '-%' AND \"Slug\" NOT LIKE '%-' AND \"Slug\" NOT LIKE '%--%'");
                    table.CheckConstraint("CK_ContentPages_Title", "length(\"Title\") BETWEEN 1 AND 200");
                });

            migrationBuilder.CreateTable(
                name: "ContentPageRedirects",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPageRedirects", x => x.Slug);
                    table.ForeignKey(
                        name: "FK_ContentPageRedirects_ContentPages_PageId",
                        column: x => x.PageId,
                        principalTable: "ContentPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PageTreeNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsLink = table.Column<bool>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageTreeNodes", x => x.Id);
                    table.CheckConstraint("CK_PageTreeNodes_Position", "\"Position\" >= 0");
                    table.ForeignKey(
                        name: "FK_PageTreeNodes_ContentPages_PageId",
                        column: x => x.PageId,
                        principalTable: "ContentPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PageTreeNodes_PageTreeNodes_ParentNodeId",
                        column: x => x.ParentNodeId,
                        principalTable: "PageTreeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsageContexts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RootNodeId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageContexts", x => x.Id);
                    table.CheckConstraint("CK_UsageContexts_Id", "length(\"Id\") BETWEEN 1 AND 64 AND \"Id\" NOT GLOB '*[^a-z0-9-]*' AND \"Id\" NOT LIKE '-%' AND \"Id\" NOT LIKE '%-' AND \"Id\" NOT LIKE '%--%'");
                    table.CheckConstraint("CK_UsageContexts_Name", "length(\"Name\") BETWEEN 1 AND 128");
                    table.ForeignKey(
                        name: "FK_UsageContexts_PageTreeNodes_RootNodeId",
                        column: x => x.RootNodeId,
                        principalTable: "PageTreeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPageRedirects_PageId",
                table: "ContentPageRedirects",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_FileId",
                table: "ContentPages",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_IsPublished_Category",
                table: "ContentPages",
                columns: new[] { "IsPublished", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_PageTreeNodes_PageId",
                table: "PageTreeNodes",
                column: "PageId",
                unique: true,
                filter: "\"IsLink\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PageTreeNodes_PageId_IsLink",
                table: "PageTreeNodes",
                columns: new[] { "PageId", "IsLink" });

            migrationBuilder.CreateIndex(
                name: "IX_PageTreeNodes_ParentNodeId_Position",
                table: "PageTreeNodes",
                columns: new[] { "ParentNodeId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageContexts_RootNodeId",
                table: "UsageContexts",
                column: "RootNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_Slug",
                table: "ContentPages",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageContexts");

            migrationBuilder.DropTable(
                name: "PageTreeNodes");

            migrationBuilder.DropTable(
                name: "ContentPageRedirects");

            migrationBuilder.DropTable(
                name: "ContentPages");
        }
    }
}
