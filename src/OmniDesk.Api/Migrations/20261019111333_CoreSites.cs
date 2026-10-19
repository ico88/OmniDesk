using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class CoreSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ScopeGrant_OneScope",
                table: "ScopeGrants");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "SupportTickets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-4111-8111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ScopeGrants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "EnrollmentTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-4111-8111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Devices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-4111-8111-111111111111"));

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "Sites" ("Id", "Name", "IsArchived", "CreatedAt")
                VALUES ('11111111-1111-4111-8111-111111111111', 'Sede da assegnare', FALSE, NOW());

                UPDATE "SupportTickets" AS ticket
                SET "SiteId" = device."SiteId"
                FROM "Devices" AS device
                WHERE device."Id" = ticket."DeviceId";

                ALTER TABLE "Devices" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "EnrollmentTokens" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "SupportTickets" ALTER COLUMN "SiteId" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_SiteId",
                table: "SupportTickets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeGrants_SiteId",
                table: "ScopeGrants",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeGrants_UserId_RoleId_SiteId",
                table: "ScopeGrants",
                columns: new[] { "UserId", "RoleId", "SiteId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ScopeGrant_OneScope",
                table: "ScopeGrants",
                sql: "num_nonnulls(\"GroupId\", \"DeviceId\", \"SiteId\") = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentTokens_SiteId",
                table: "EnrollmentTokens",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SiteId",
                table: "Devices",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Name",
                table: "Sites",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_Sites_SiteId",
                table: "Devices",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EnrollmentTokens_Sites_SiteId",
                table: "EnrollmentTokens",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopeGrants_Sites_SiteId",
                table: "ScopeGrants",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_Sites_SiteId",
                table: "SupportTickets",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_Sites_SiteId",
                table: "Devices");

            migrationBuilder.DropForeignKey(
                name: "FK_EnrollmentTokens_Sites_SiteId",
                table: "EnrollmentTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_ScopeGrants_Sites_SiteId",
                table: "ScopeGrants");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_Sites_SiteId",
                table: "SupportTickets");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_SiteId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_ScopeGrants_SiteId",
                table: "ScopeGrants");

            migrationBuilder.DropIndex(
                name: "IX_ScopeGrants_UserId_RoleId_SiteId",
                table: "ScopeGrants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ScopeGrant_OneScope",
                table: "ScopeGrants");

            migrationBuilder.DropIndex(
                name: "IX_EnrollmentTokens_SiteId",
                table: "EnrollmentTokens");

            migrationBuilder.DropIndex(
                name: "IX_Devices_SiteId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ScopeGrants");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "EnrollmentTokens");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Devices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ScopeGrant_OneScope",
                table: "ScopeGrants",
                sql: "(\"GroupId\" IS NOT NULL) <> (\"DeviceId\" IS NOT NULL)");
        }
    }
}
