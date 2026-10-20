using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations;

public partial class SlaPauseAndCycles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PausedAt",
            table: "SupportTicketSlaTargets",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "AccumulatedPauseSeconds",
            table: "SupportTicketSlaTargets",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PausedAt", table: "SupportTicketSlaTargets");
        migrationBuilder.DropColumn(name: "AccumulatedPauseSeconds", table: "SupportTicketSlaTargets");
    }
}
