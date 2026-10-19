using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class TicketSlaTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportTicketSlaTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    Cycle = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BreachedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketSlaTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTicketSlaTargets_SupportSlaPolicyVersions_PolicyId_P~",
                        columns: x => new { x.PolicyId, x.PolicyVersion },
                        principalTable: "SupportSlaPolicyVersions",
                        principalColumns: new[] { "PolicyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTicketSlaTargets_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketSlaTargets_DueAt_CompletedAt_BreachedAt",
                table: "SupportTicketSlaTargets",
                columns: new[] { "DueAt", "CompletedAt", "BreachedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketSlaTargets_PolicyId_PolicyVersion",
                table: "SupportTicketSlaTargets",
                columns: new[] { "PolicyId", "PolicyVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketSlaTargets_TicketId_TargetType_Cycle",
                table: "SupportTicketSlaTargets",
                columns: new[] { "TicketId", "TargetType", "Cycle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportTicketSlaTargets");
        }
    }
}
