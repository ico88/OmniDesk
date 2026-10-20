using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SlaEscalationSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportTicketSlaEscalations",
                columns: table => new
                {
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepMinutes = table.Column<int>(type: "integer", nullable: false),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketSlaEscalations", x => new { x.TargetId, x.StepMinutes });
                    table.ForeignKey(
                        name: "FK_SupportTicketSlaEscalations_SupportTicketSlaTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "SupportTicketSlaTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportTicketSlaEscalations");
        }
    }
}
