using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable
namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019110000_DiagnosticsPermission")]
public sealed class DiagnosticsPermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("INSERT INTO \"RolePermissions\" (\"Permission\", \"RoleId\") VALUES ('diagnostics.view', 'Administrator'), ('diagnostics.view', 'Operator') ON CONFLICT DO NOTHING;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM \"RolePermissions\" WHERE \"Permission\" = 'diagnostics.view';");
    }
}
