using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations;

public partial class ManualSupportTickets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "Origin",
            table: "SupportTickets",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "device");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Origin", table: "SupportTickets");
}
