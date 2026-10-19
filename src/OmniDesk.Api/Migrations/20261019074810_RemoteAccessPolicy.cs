using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoteAccessPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AccessPolicyVersion",
                table: "RemoteSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "ConsentRequired",
                table: "RemoteSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // All pre-existing sessions used local consent. Preserve that history.
            migrationBuilder.Sql("UPDATE \"RemoteSessions\" SET \"ConsentRequired\" = TRUE");

            migrationBuilder.AddColumn<long>(
                name: "AccessPolicyVersion",
                table: "RemoteProvisionings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "UnattendedAllowed",
                table: "RemoteProvisionings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessPolicyVersion",
                table: "RemoteSessions");

            migrationBuilder.DropColumn(
                name: "ConsentRequired",
                table: "RemoteSessions");

            migrationBuilder.DropColumn(
                name: "AccessPolicyVersion",
                table: "RemoteProvisionings");

            migrationBuilder.DropColumn(
                name: "UnattendedAllowed",
                table: "RemoteProvisionings");
        }
    }
}
