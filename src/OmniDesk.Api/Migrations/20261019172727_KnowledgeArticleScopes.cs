using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeArticleScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "KnowledgeArticles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "KnowledgeArticles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "KnowledgeArticleRevisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "KnowledgeArticleRevisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_GroupId",
                table: "KnowledgeArticles",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_SiteId",
                table: "KnowledgeArticles",
                column: "SiteId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KnowledgeArticle_OneScope",
                table: "KnowledgeArticles",
                sql: "num_nonnulls(\"GroupId\", \"SiteId\") <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticleRevisions_GroupId",
                table: "KnowledgeArticleRevisions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticleRevisions_SiteId",
                table: "KnowledgeArticleRevisions",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeArticleRevisions_DeviceGroups_GroupId",
                table: "KnowledgeArticleRevisions",
                column: "GroupId",
                principalTable: "DeviceGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeArticleRevisions_Sites_SiteId",
                table: "KnowledgeArticleRevisions",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeArticles_DeviceGroups_GroupId",
                table: "KnowledgeArticles",
                column: "GroupId",
                principalTable: "DeviceGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeArticles_Sites_SiteId",
                table: "KnowledgeArticles",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeArticleRevisions_DeviceGroups_GroupId",
                table: "KnowledgeArticleRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeArticleRevisions_Sites_SiteId",
                table: "KnowledgeArticleRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeArticles_DeviceGroups_GroupId",
                table: "KnowledgeArticles");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeArticles_Sites_SiteId",
                table: "KnowledgeArticles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeArticles_GroupId",
                table: "KnowledgeArticles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeArticles_SiteId",
                table: "KnowledgeArticles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KnowledgeArticle_OneScope",
                table: "KnowledgeArticles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeArticleRevisions_GroupId",
                table: "KnowledgeArticleRevisions");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeArticleRevisions_SiteId",
                table: "KnowledgeArticleRevisions");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "KnowledgeArticles");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "KnowledgeArticles");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "KnowledgeArticleRevisions");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "KnowledgeArticleRevisions");
        }
    }
}
