using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class TicketAssignmentAndPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_SiteId",
                table: "SupportTickets");

            migrationBuilder.AddColumn<string>(
                name: "AssigneeId",
                table: "SupportTickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "SupportTickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssigneeId",
                table: "SupportTickets",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_SiteId_State_Priority_CreatedAt",
                table: "SupportTickets",
                columns: new[] { "SiteId", "State", "Priority", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_AspNetUsers_AssigneeId",
                table: "SupportTickets",
                column: "AssigneeId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_AspNetUsers_AssigneeId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_AssigneeId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_SiteId_State_Priority_CreatedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssigneeId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "SupportTickets");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_SiteId",
                table: "SupportTickets",
                column: "SiteId");
        }
    }
}
