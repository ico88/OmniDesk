using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportQueues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "QueueId",
                table: "SupportTickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportQueues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportQueues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_QueueId_State_CreatedAt",
                table: "SupportTickets",
                columns: new[] { "QueueId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportQueues_NormalizedName",
                table: "SupportQueues",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_SupportQueues_QueueId",
                table: "SupportTickets",
                column: "QueueId",
                principalTable: "SupportQueues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_SupportQueues_QueueId",
                table: "SupportTickets");

            migrationBuilder.DropTable(
                name: "SupportQueues");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_QueueId_State_CreatedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "QueueId",
                table: "SupportTickets");
        }
    }
}
