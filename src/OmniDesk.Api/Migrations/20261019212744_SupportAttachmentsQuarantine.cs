using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportAttachmentsQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploaderId = table.Column<string>(type: "text", nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScanState = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ScanAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextScanAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScanLeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScannerVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastScanErrorType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAttachments", x => x.Id);
                    table.CheckConstraint("CK_SupportAttachment_ScanState", "\"ScanState\" IN ('quarantined', 'scanning', 'clean', 'infected', 'scan_failed')");
                    table.CheckConstraint("CK_SupportAttachment_Sha256", "length(\"Sha256\") = 64");
                    table.CheckConstraint("CK_SupportAttachment_Size", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 10485760");
                    table.CheckConstraint("CK_SupportAttachment_Visibility", "\"Visibility\" IN ('public', 'internal')");
                    table.ForeignKey(
                        name: "FK_SupportAttachments_AspNetUsers_UploaderId",
                        column: x => x.UploaderId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupportAttachments_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_ScanState_NextScanAt_ScanLeaseUntil",
                table: "SupportAttachments",
                columns: new[] { "ScanState", "NextScanAt", "ScanLeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_StorageKey",
                table: "SupportAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_TicketId_CreatedAt",
                table: "SupportAttachments",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_UploaderId",
                table: "SupportAttachments",
                column: "UploaderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportAttachments");
        }
    }
}
