using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020061000_ResolvedTicketContentRetention")]
public sealed class ResolvedTicketContentRetention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ContentPurged",
            table: "SupportMessages",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "ContentPurged",
            table: "SupportTickets",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ContentPurged", table: "SupportMessages");
        migrationBuilder.DropColumn(name: "ContentPurged", table: "SupportTickets");
    }
}
