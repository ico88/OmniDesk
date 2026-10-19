using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations;

public sealed partial class ConcurrentDiagnosticJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(name: "IX_UpdateJobs_DeviceId", table: "UpdateJobs");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_UpdateJobs_DeviceId",
            table: "UpdateJobs",
            column: "DeviceId",
            unique: true,
            filter: "\"State\" IN ('queued','leased')");
}
