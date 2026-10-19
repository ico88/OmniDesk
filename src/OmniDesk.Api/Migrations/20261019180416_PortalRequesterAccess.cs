using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class PortalRequesterAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequesterUserId",
                table: "SupportTickets",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PortalDeviceAccesses",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalDeviceAccesses", x => new { x.UserId, x.DeviceId });
                    table.ForeignKey(
                        name: "FK_PortalDeviceAccesses_AspNetUsers_GrantedBy",
                        column: x => x.GrantedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PortalDeviceAccesses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PortalDeviceAccesses_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_RequesterUserId_CreatedAt",
                table: "SupportTickets",
                columns: new[] { "RequesterUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PortalDeviceAccesses_DeviceId",
                table: "PortalDeviceAccesses",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalDeviceAccesses_GrantedBy",
                table: "PortalDeviceAccesses",
                column: "GrantedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_AspNetUsers_RequesterUserId",
                table: "SupportTickets",
                column: "RequesterUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_AspNetUsers_RequesterUserId",
                table: "SupportTickets");

            migrationBuilder.DropTable(
                name: "PortalDeviceAccesses");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_RequesterUserId_CreatedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "RequesterUserId",
                table: "SupportTickets");
        }
    }
}
