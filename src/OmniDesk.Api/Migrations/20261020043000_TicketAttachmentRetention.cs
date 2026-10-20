using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020043000_TicketAttachmentRetention")]
public sealed class TicketAttachmentRetention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SupportAttachment_ScanState",
            table: "SupportAttachments");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SupportAttachment_ScanState",
            table: "SupportAttachments",
            sql: "\"ScanState\" IN ('quarantined', 'scanning', 'clean', 'infected', 'scan_failed', 'retention_pending')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SupportAttachment_ScanState",
            table: "SupportAttachments");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SupportAttachment_ScanState",
            table: "SupportAttachments",
            sql: "\"ScanState\" IN ('quarantined', 'scanning', 'clean', 'infected', 'scan_failed')");
    }
}
