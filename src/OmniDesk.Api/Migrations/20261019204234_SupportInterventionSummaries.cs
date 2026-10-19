using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportInterventionSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportInterventionSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AuthorId = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FollowUp = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportInterventionSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportInterventionSummaries_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupportInterventionSummaries_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportInterventionSummaries_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportInterventionSummaries_AuthorId",
                table: "SupportInterventionSummaries",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportInterventionSummaries_DeviceId_ArtifactType_Artifact~",
                table: "SupportInterventionSummaries",
                columns: new[] { "DeviceId", "ArtifactType", "ArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportInterventionSummaries_TicketId_RecordedAt",
                table: "SupportInterventionSummaries",
                columns: new[] { "TicketId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportInterventionSummaries");
        }
    }
}
