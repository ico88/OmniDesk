using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class TicketCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "SupportTickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "general");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_SiteId_Category_State_CreatedAt",
                table: "SupportTickets",
                columns: new[] { "SiteId", "Category", "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_SiteId_Category_State_CreatedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "SupportTickets");
        }
    }
}
