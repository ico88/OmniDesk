using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportWorkflowVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowId",
                table: "SupportTickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowVersion",
                table: "SupportTickets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DraftDefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    ActiveVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportWorkflows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportWorkflows_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportWorkflowVersions",
                columns: table => new
                {
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublishedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportWorkflowVersions", x => new { x.WorkflowId, x.Version });
                    table.ForeignKey(
                        name: "FK_SupportWorkflowVersions_SupportWorkflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "SupportWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_WorkflowId_WorkflowVersion",
                table: "SupportTickets",
                columns: new[] { "WorkflowId", "WorkflowVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportWorkflows_ScopeKey",
                table: "SupportWorkflows",
                column: "ScopeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportWorkflows_SiteId",
                table: "SupportWorkflows",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_SupportWorkflowVersions_WorkflowId_WorkflowV~",
                table: "SupportTickets",
                columns: new[] { "WorkflowId", "WorkflowVersion" },
                principalTable: "SupportWorkflowVersions",
                principalColumns: new[] { "WorkflowId", "Version" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_SupportWorkflowVersions_WorkflowId_WorkflowV~",
                table: "SupportTickets");

            migrationBuilder.DropTable(
                name: "SupportWorkflowVersions");

            migrationBuilder.DropTable(
                name: "SupportWorkflows");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_WorkflowId_WorkflowVersion",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "WorkflowId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                table: "SupportTickets");
        }
    }
}
