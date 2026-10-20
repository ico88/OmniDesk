using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020070000_DevicePresenceNotifications")]
public sealed class DevicePresenceNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_UserNotifications_SupportTickets_TicketId", "UserNotifications");
        migrationBuilder.AlterColumn<Guid>(name: "TicketId", table: "UserNotifications", type: "uuid", nullable: true, oldClrType: typeof(Guid), oldType: "uuid");
        migrationBuilder.AddColumn<Guid>(name: "DeviceId", table: "UserNotifications", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_UserNotifications_DeviceId", table: "UserNotifications", column: "DeviceId");
        migrationBuilder.AddForeignKey(name: "FK_UserNotifications_SupportTickets_TicketId", table: "UserNotifications", column: "TicketId",
            principalTable: "SupportTickets", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey(name: "FK_UserNotifications_Devices_DeviceId", table: "UserNotifications", column: "DeviceId",
            principalTable: "Devices", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddCheckConstraint(name: "CK_UserNotification_Target", table: "UserNotifications",
            sql: "(\"TicketId\" IS NOT NULL AND \"DeviceId\" IS NULL) OR (\"TicketId\" IS NULL AND \"DeviceId\" IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM \"EmailNotificationDeliveries\" WHERE \"NotificationId\" IN (SELECT \"Id\" FROM \"UserNotifications\" WHERE \"DeviceId\" IS NOT NULL)");
        migrationBuilder.Sql("DELETE FROM \"UserNotifications\" WHERE \"DeviceId\" IS NOT NULL");
        migrationBuilder.DropCheckConstraint("CK_UserNotification_Target", "UserNotifications");
        migrationBuilder.DropForeignKey("FK_UserNotifications_Devices_DeviceId", "UserNotifications");
        migrationBuilder.DropForeignKey("FK_UserNotifications_SupportTickets_TicketId", "UserNotifications");
        migrationBuilder.DropIndex("IX_UserNotifications_DeviceId", "UserNotifications");
        migrationBuilder.DropColumn("DeviceId", "UserNotifications");
        migrationBuilder.AlterColumn<Guid>(name: "TicketId", table: "UserNotifications", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        migrationBuilder.AddForeignKey(name: "FK_UserNotifications_SupportTickets_TicketId", table: "UserNotifications", column: "TicketId",
            principalTable: "SupportTickets", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
    }
}
