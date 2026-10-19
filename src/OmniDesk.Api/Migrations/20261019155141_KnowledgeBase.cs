using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedSlug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticles", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Permission", "RoleId" },
                values: new object[,]
                {
                    { "knowledge.manage", "Administrator" },
                    { "knowledge.view", "Administrator" },
                    { "knowledge.view", "Operator" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_IsPublished_UpdatedAt",
                table: "KnowledgeArticles",
                columns: new[] { "IsPublished", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_NormalizedSlug",
                table: "KnowledgeArticles",
                column: "NormalizedSlug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeArticles");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "knowledge.manage", "Administrator" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "knowledge.view", "Administrator" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "knowledge.view", "Operator" });
        }
    }
}
