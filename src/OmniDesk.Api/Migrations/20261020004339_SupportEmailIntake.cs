using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportEmailIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportEmailIntakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SenderAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BodyText = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QualifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QualifiedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PromotedTicketId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportEmailIntakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportEmailIntakes_AspNetUsers_QualifiedBy",
                        column: x => x.QualifiedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupportEmailIntakes_SupportTickets_PromotedTicketId",
                        column: x => x.PromotedTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportEmailIntakes_MailboxAddress_MessageId",
                table: "SupportEmailIntakes",
                columns: new[] { "MailboxAddress", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportEmailIntakes_PromotedTicketId",
                table: "SupportEmailIntakes",
                column: "PromotedTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportEmailIntakes_QualifiedBy",
                table: "SupportEmailIntakes",
                column: "QualifiedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SupportEmailIntakes_State_ReceivedAt",
                table: "SupportEmailIntakes",
                columns: new[] { "State", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportEmailIntakes");
        }
    }
}
