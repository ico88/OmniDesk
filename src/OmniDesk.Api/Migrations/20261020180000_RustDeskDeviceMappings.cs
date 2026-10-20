using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020180000_RustDeskDeviceMappings")]
public sealed class RustDeskDeviceMappings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RustDeskDeviceMappings",
            columns: table => new
            {
                DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                PeerId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RustDeskDeviceMappings", x => x.DeviceId);
                table.ForeignKey(
                    name: "FK_RustDeskDeviceMappings_Devices_DeviceId",
                    column: x => x.DeviceId,
                    principalTable: "Devices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RustDeskDeviceMappings_PeerId",
            table: "RustDeskDeviceMappings",
            column: "PeerId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RustDeskDeviceMappings");
    }
}
