using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020100000_SupportAttachmentMessages")]
public sealed class SupportAttachmentMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "MessageId", table: "SupportAttachments", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_SupportAttachments_MessageId", table: "SupportAttachments", column: "MessageId");
        migrationBuilder.AddForeignKey(name: "FK_SupportAttachments_SupportMessages_MessageId", table: "SupportAttachments",
            column: "MessageId", principalTable: "SupportMessages", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_SupportAttachments_SupportMessages_MessageId", "SupportAttachments");
        migrationBuilder.DropIndex("IX_SupportAttachments_MessageId", "SupportAttachments");
        migrationBuilder.DropColumn("MessageId", "SupportAttachments");
    }
}
