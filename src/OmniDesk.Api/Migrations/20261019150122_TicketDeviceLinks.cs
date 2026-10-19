using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class TicketDeviceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportTicketDevices",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkedBy = table.Column<string>(type: "text", nullable: true),
                    DeviceNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SiteIdSnapshot = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketDevices", x => new { x.TicketId, x.DeviceId });
                    table.ForeignKey(
                        name: "FK_SupportTicketDevices_AspNetUsers_LinkedBy",
                        column: x => x.LinkedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupportTicketDevices_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTicketDevices_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketDevices_DeviceId_TicketId",
                table: "SupportTicketDevices",
                columns: new[] { "DeviceId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketDevices_LinkedBy",
                table: "SupportTicketDevices",
                column: "LinkedBy");

            migrationBuilder.Sql("""
                INSERT INTO "SupportTicketDevices"
                    ("TicketId", "DeviceId", "LinkedAt", "DeviceNameSnapshot", "SiteIdSnapshot", "SiteNameSnapshot")
                SELECT t."Id", d."Id", t."CreatedAt", d."Name", d."SiteId", s."Name"
                FROM "SupportTickets" t
                JOIN "Devices" d ON d."Id" = t."DeviceId"
                JOIN "Sites" s ON s."Id" = d."SiteId"
                ON CONFLICT ("TicketId", "DeviceId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportTicketDevices");
        }
    }
}
