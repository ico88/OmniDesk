using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019210000_TicketPrioritiesP1P4")]
public sealed class TicketPrioritiesP1P4 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        "UPDATE \"SupportTickets\" SET \"Priority\" = CASE \"Priority\" " +
        "WHEN 'urgent' THEN 'p1' WHEN 'high' THEN 'p2' WHEN 'normal' THEN 'p3' WHEN 'low' THEN 'p4' ELSE \"Priority\" END " +
        "WHERE \"Priority\" IN ('urgent', 'high', 'normal', 'low');");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        "UPDATE \"SupportTickets\" SET \"Priority\" = CASE \"Priority\" " +
        "WHEN 'p1' THEN 'urgent' WHEN 'p2' THEN 'high' WHEN 'p3' THEN 'normal' WHEN 'p4' THEN 'low' ELSE \"Priority\" END " +
        "WHERE \"Priority\" IN ('p1', 'p2', 'p3', 'p4');");
}
