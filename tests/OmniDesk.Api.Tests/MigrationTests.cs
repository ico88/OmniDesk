using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using OmniDesk.Api.Data;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task Ticket_number_migration_preserves_existing_tickets_and_assigns_unique_numbers()
    {
        var baseConnection = Environment.GetEnvironmentVariable("TEST_DATABASE")
            ?? throw new InvalidOperationException("TEST_DATABASE must point to an isolated PostgreSQL test database");
        var schema = "migration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(baseConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
        var siteId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var deviceId = Guid.NewGuid(); var ticketId = Guid.NewGuid();
        try
        {
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
                await migrator.MigrateAsync("20261019114141_ProcessControlPermission");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Sites\" (\"Id\",\"Name\",\"IsArchived\",\"CreatedAt\") VALUES ({siteId},'Migration fixture',false,now())");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DeviceGroups\" (\"Id\",\"Name\") VALUES ({groupId},'Migration fixture')");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Devices\" (\"Id\",\"Name\",\"GroupId\",\"SiteId\") VALUES ({deviceId},'Existing device',{groupId},{siteId})");
                var oldRequestId = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SupportTickets\" (\"Id\",\"RequestId\",\"DeviceId\",\"SiteId\",\"RequesterKey\",\"Subject\",\"Description\",\"State\",\"Priority\",\"AssigneeId\",\"Resolution\",\"Version\",\"CreatedAt\",\"UpdatedAt\",\"Screenshot\") VALUES ({ticketId},{oldRequestId},{deviceId},{siteId},{new string('C', 64)},'Existing ticket','Before upgrade','open','normal',NULL,NULL,1,now(),now(),NULL)");
                await db.Database.MigrateAsync();
                var existing = await db.SupportTickets.AsNoTracking().SingleAsync(x => x.Id == ticketId);
                Assert.True(existing.Number > 0);
                Assert.Equal("general", existing.Category);
                var next = new SupportTicket { RequestId = Guid.NewGuid(), DeviceId = deviceId, SiteId = siteId,
                    RequesterKey = new string('D', 64), Subject = "New ticket", Description = "After upgrade" };
                db.SupportTickets.Add(next);
                await db.SaveChangesAsync();
                Assert.True(next.Number > existing.Number);
                Assert.Equal("general", next.Category);
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
