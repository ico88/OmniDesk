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
        var siteId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var deviceId = Guid.NewGuid();
        var legacyPriorities = new Dictionary<Guid, string>
        {
            [Guid.NewGuid()] = "urgent", [Guid.NewGuid()] = "high", [Guid.NewGuid()] = "normal", [Guid.NewGuid()] = "low"
        };
        var ticketId = legacyPriorities.Single(x => x.Value == "normal").Key;
        try
        {
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
                await migrator.MigrateAsync("20261019114141_ProcessControlPermission");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Sites\" (\"Id\",\"Name\",\"IsArchived\",\"CreatedAt\") VALUES ({siteId},'Migration fixture',false,now())");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DeviceGroups\" (\"Id\",\"Name\") VALUES ({groupId},'Migration fixture')");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Devices\" (\"Id\",\"Name\",\"GroupId\",\"SiteId\") VALUES ({deviceId},'Existing device',{groupId},{siteId})");
                foreach (var (legacyTicketId, priority) in legacyPriorities)
                {
                    var requesterKey = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SupportTickets\" (\"Id\",\"RequestId\",\"DeviceId\",\"SiteId\",\"RequesterKey\",\"Subject\",\"Description\",\"State\",\"Priority\",\"AssigneeId\",\"Resolution\",\"Version\",\"CreatedAt\",\"UpdatedAt\",\"Screenshot\") VALUES ({legacyTicketId},{Guid.NewGuid()},{deviceId},{siteId},{requesterKey},'Existing ticket','Before upgrade','open',{priority},NULL,NULL,1,now(),now(),NULL)");
                }
                await migrator.MigrateAsync("20261019155141_KnowledgeBase");
                var articleId = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"KnowledgeArticles\" (\"Id\",\"Slug\",\"NormalizedSlug\",\"Title\",\"Summary\",\"Body\",\"IsPublished\",\"Version\",\"CreatedAt\",\"UpdatedAt\",\"PublishedAt\") VALUES ({articleId},'legacy-guide','legacy-guide','Legacy guide','Before scope rollout','Existing content',true,4,now(),now(),now())");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"SupportTicketDevices\" WHERE \"TicketId\" IN ({legacyPriorities.Keys.ElementAt(0)},{legacyPriorities.Keys.ElementAt(1)},{legacyPriorities.Keys.ElementAt(2)},{legacyPriorities.Keys.ElementAt(3)})");
                await db.Database.MigrateAsync();
                var legacyRevision = await db.KnowledgeArticleRevisions.AsNoTracking().SingleAsync(x => x.ArticleId == articleId && x.Version == 4);
                Assert.Equal("Existing content", legacyRevision.Body);
                Assert.Null(legacyRevision.SiteId);
                Assert.Null(legacyRevision.GroupId);
                var existing = await db.SupportTickets.AsNoTracking().SingleAsync(x => x.Id == ticketId);
                Assert.True(existing.Number > 0);
                Assert.Equal("general", existing.Category);
                Assert.Null(existing.QueueId);
                var upgradedTickets = await db.SupportTickets.AsNoTracking().Where(x => legacyPriorities.Keys.Contains(x.Id)).ToArrayAsync();
                Assert.Equal(4, upgradedTickets.Length);
                Assert.Equal("p1", upgradedTickets.Single(x => legacyPriorities[x.Id] == "urgent").Priority);
                Assert.Equal("p2", upgradedTickets.Single(x => legacyPriorities[x.Id] == "high").Priority);
                Assert.Equal("p3", upgradedTickets.Single(x => legacyPriorities[x.Id] == "normal").Priority);
                Assert.Equal("p4", upgradedTickets.Single(x => legacyPriorities[x.Id] == "low").Priority);
                Assert.Equal(4, await db.SupportTicketDevices.CountAsync(link => legacyPriorities.Keys.Contains(link.TicketId)));
                var next = new SupportTicket { RequestId = Guid.NewGuid(), DeviceId = deviceId, SiteId = siteId,
                    RequesterKey = new string('D', 64), Subject = "New ticket", Description = "After upgrade" };
                db.SupportTickets.Add(next);
                db.SupportTicketDevices.Add(new SupportTicketDevice
                {
                    TicketId = next.Id, DeviceId = deviceId, DeviceNameSnapshot = "Existing device",
                    SiteIdSnapshot = siteId, SiteNameSnapshot = "Migration fixture"
                });
                await db.SaveChangesAsync();
                Assert.True(next.Number > existing.Number);
                Assert.Equal("general", next.Category);
                Assert.Null(next.QueueId);
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
