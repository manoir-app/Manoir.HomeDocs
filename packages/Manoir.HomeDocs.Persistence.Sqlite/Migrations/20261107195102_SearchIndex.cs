using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoir.HomeDocs.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Local full-text index of the published pages. It is derived data: it is kept in step with
            // ContentPages by ContentPageOperations and rebuilt from the Markdown files at startup.
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE \"PageSearch\" USING fts5(" +
                "Title, Summary, Body, Category, " +
                "PageId UNINDEXED, Slug UNINDEXED, CategoryId UNINDEXED, Contexts UNINDEXED, Locked UNINDEXED, " +
                "tokenize = 'unicode61 remove_diacritics 2', prefix = '2 3')");
            migrationBuilder.Sql("CREATE VIRTUAL TABLE \"PageSearchVocab\" USING fts5vocab(\"PageSearch\", 'row')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE \"PageSearchVocab\"");
            migrationBuilder.Sql("DROP TABLE \"PageSearch\"");
        }
    }
}
