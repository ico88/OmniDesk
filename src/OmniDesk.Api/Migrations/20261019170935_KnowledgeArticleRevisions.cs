using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeArticleRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeArticleRevisions",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticleRevisions", x => new { x.ArticleId, x.Version });
                    table.ForeignKey(
                        name: "FK_KnowledgeArticleRevisions_AspNetUsers_RecordedBy",
                        column: x => x.RecordedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_KnowledgeArticleRevisions_KnowledgeArticles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "KnowledgeArticles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticleRevisions_RecordedBy",
                table: "KnowledgeArticleRevisions",
                column: "RecordedBy");

            migrationBuilder.Sql("""
                INSERT INTO "KnowledgeArticleRevisions"
                    ("ArticleId", "Version", "Slug", "Title", "Summary", "Body", "IsPublished", "PublishedAt", "RecordedAt", "RecordedBy")
                SELECT "Id", "Version", "Slug", "Title", "Summary", "Body", "IsPublished", "PublishedAt", "UpdatedAt", NULL
                FROM "KnowledgeArticles";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeArticleRevisions");
        }
    }
}
