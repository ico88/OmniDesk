using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportQueueSiteScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportQueues_NormalizedName",
                table: "SupportQueues");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "SupportQueues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportQueues_NormalizedName",
                table: "SupportQueues",
                column: "NormalizedName",
                unique: true,
                filter: "\"SiteId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupportQueues_SiteId_NormalizedName",
                table: "SupportQueues",
                columns: new[] { "SiteId", "NormalizedName" },
                unique: true,
                filter: "\"SiteId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportQueues_Sites_SiteId",
                table: "SupportQueues",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportQueues_Sites_SiteId",
                table: "SupportQueues");

            migrationBuilder.DropIndex(
                name: "IX_SupportQueues_NormalizedName",
                table: "SupportQueues");

            migrationBuilder.DropIndex(
                name: "IX_SupportQueues_SiteId_NormalizedName",
                table: "SupportQueues");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "SupportQueues");

            migrationBuilder.CreateIndex(
                name: "IX_SupportQueues_NormalizedName",
                table: "SupportQueues",
                column: "NormalizedName",
                unique: true);
        }
    }
}
