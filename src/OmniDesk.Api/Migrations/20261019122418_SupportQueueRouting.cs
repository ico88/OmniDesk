using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportQueueRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportQueueRoutingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QueueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportQueueRoutingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportQueueRoutingRules_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportQueueRoutingRules_SupportQueues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "SupportQueues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportQueueRoutingRules_Category",
                table: "SupportQueueRoutingRules",
                column: "Category",
                unique: true,
                filter: "\"SiteId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupportQueueRoutingRules_QueueId",
                table: "SupportQueueRoutingRules",
                column: "QueueId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportQueueRoutingRules_SiteId_Category",
                table: "SupportQueueRoutingRules",
                columns: new[] { "SiteId", "Category" },
                unique: true,
                filter: "\"SiteId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportQueueRoutingRules");
        }
    }
}
