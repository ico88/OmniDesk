using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportSlaPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportSlaPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSlaPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportSlaPolicies_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportSlaPolicyVersions",
                columns: table => new
                {
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublishedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSlaPolicyVersions", x => new { x.PolicyId, x.Version });
                    table.ForeignKey(
                        name: "FK_SupportSlaPolicyVersions_AspNetUsers_PublishedBy",
                        column: x => x.PublishedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportSlaPolicyVersions_SupportSlaPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "SupportSlaPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Permission", "RoleId" },
                values: new object[] { "tickets.sla.manage", "Administrator" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSlaPolicies_ScopeKey",
                table: "SupportSlaPolicies",
                column: "ScopeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportSlaPolicies_SiteId",
                table: "SupportSlaPolicies",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportSlaPolicyVersions_PublishedBy",
                table: "SupportSlaPolicyVersions",
                column: "PublishedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportSlaPolicyVersions");

            migrationBuilder.DropTable(
                name: "SupportSlaPolicies");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.sla.manage", "Administrator" });
        }
    }
}
