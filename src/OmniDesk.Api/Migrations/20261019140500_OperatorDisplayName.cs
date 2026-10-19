using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations;

public partial class OperatorDisplayName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "DisplayName", table: "AspNetUsers", type: "character varying(80)", maxLength: 80, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "DisplayName", table: "AspNetUsers");
}
