using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OmniDesk.Api.Data;

public sealed class AppUser : IdentityUser { public bool IsDisabled { get; set; } }
public sealed class DeviceGroup { public Guid Id { get; set; } public string Name { get; set; } = ""; }
public sealed class Site { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; public bool IsArchived { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; }
public sealed class Device { public Guid Id { get; set; } public string Name { get; set; } = ""; public Guid? GroupId { get; set; } public Guid SiteId { get; set; } }
public sealed class RolePermission { public string RoleId { get; set; } = ""; public string Permission { get; set; } = ""; }
public sealed class ScopeGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public Guid? GroupId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? SiteId { get; set; }
}
public sealed class Branding
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "OmniDesk";
    public string LogoUrl { get; set; } = "";
    public string PrimaryColor { get; set; } = "#2563eb";
    public string Theme { get; set; } = "dark";
}
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Outcome { get; set; } = "success";
}
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportQueue> SupportQueues => Set<SupportQueue>();
    public DbSet<DeviceGroup> DeviceGroups => Set<DeviceGroup>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ScopeGrant> ScopeGrants => Set<ScopeGrant>();
    public DbSet<Branding> Branding => Set<Branding>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<DeviceAssertion> DeviceAssertions => Set<DeviceAssertion>();
    public DbSet<DeviceMetrics> DeviceMetrics => Set<DeviceMetrics>();
    public DbSet<DeviceInventory> DeviceInventories => Set<DeviceInventory>();
    public DbSet<MeshDeviceMapping> MeshDeviceMappings => Set<MeshDeviceMapping>();
    public DbSet<RemoteProvisioning> RemoteProvisionings => Set<RemoteProvisioning>();
    public DbSet<RemoteSession> RemoteSessions => Set<RemoteSession>();
    public DbSet<UpdateJob> UpdateJobs => Set<UpdateJob>();
    public DbSet<DashboardLayout> DashboardLayouts => Set<DashboardLayout>();
    public DbSet<CoreOutboxEvent> CoreOutboxEvents => Set<CoreOutboxEvent>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<RemoteProvisioning>().HasKey(x => x.DeviceId);
        b.Entity<RemoteProvisioning>().HasOne<Device>().WithOne().HasForeignKey<RemoteProvisioning>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportMessage>().HasOne<SupportTicket>().WithMany().HasForeignKey(x=>x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportMessage>().HasIndex(x=>new {x.TicketId,x.Id});
        b.Entity<SupportMessage>().Property(x=>x.Body).HasMaxLength(8000);
        b.Entity<SupportMessage>().Property(x=>x.Kind).HasMaxLength(20);
        b.Entity<SupportMessage>().Property(x=>x.FromState).HasMaxLength(20);
        b.Entity<SupportMessage>().Property(x=>x.ToState).HasMaxLength(20);
        b.Entity<SupportTicket>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasIndex(x => new { x.DeviceId, x.RequesterKey, x.RequestId }).IsUnique();
        b.Entity<SupportTicket>().HasIndex(x => new { x.DeviceId, x.CreatedAt });
        b.Entity<SupportTicket>().Property(x => x.Number).UseIdentityByDefaultColumn();
        b.Entity<SupportTicket>().HasIndex(x => x.Number).IsUnique();
        b.Entity<SupportTicket>().Property(x => x.RequesterKey).HasMaxLength(64);
        b.Entity<SupportTicket>().Property(x => x.Subject).HasMaxLength(200);
        b.Entity<SupportTicket>().Property(x => x.Description).HasMaxLength(8000);
        b.Entity<SupportTicket>().Property(x => x.Resolution).HasMaxLength(8000);
        b.Entity<SupportTicket>().Property(x => x.State).HasMaxLength(20);
        b.Entity<SupportTicket>().Property(x => x.Priority).HasMaxLength(20).HasDefaultValue("normal");
        b.Entity<SupportTicket>().Property(x => x.Category).HasMaxLength(32).HasDefaultValue("general");
        b.Entity<SupportTicket>().HasOne<SupportQueue>().WithMany().HasForeignKey(x => x.QueueId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportQueue>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<SupportQueue>().Property(x => x.NormalizedName).HasMaxLength(100);
        b.Entity<SupportQueue>().HasIndex(x => x.NormalizedName).IsUnique();
        b.Entity<SupportTicket>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasIndex(x => new { x.SiteId, x.State, x.Priority, x.CreatedAt });
        b.Entity<SupportTicket>().HasIndex(x => new { x.SiteId, x.Category, x.State, x.CreatedAt });
        b.Entity<SupportTicket>().HasIndex(x => new { x.QueueId, x.State, x.CreatedAt });
        b.Entity<SupportTicket>().HasIndex(x => x.AssigneeId);

        b.Entity<DashboardLayout>().HasKey(x => x.UserId);
        b.Entity<DashboardLayout>().HasOne<AppUser>().WithOne().HasForeignKey<DashboardLayout>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DashboardLayout>().Property(x => x.Widgets).HasColumnType("jsonb");
        b.Entity<UpdateJob>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<UpdateJob>().HasIndex(x => new { x.DeviceId, x.RequestId }).IsUnique();
        b.Entity<UpdateJob>().HasIndex(x => x.DeviceId).IsUnique().HasFilter("\"State\" IN ('queued','leased')");
        b.Entity<UpdateJob>().Property(x => x.Payload).HasColumnType("jsonb");
        b.Entity<UpdateJob>().Property(x => x.Result).HasColumnType("jsonb");
        b.Entity<MeshDeviceMapping>().HasKey(x => x.DeviceId);
        b.Entity<MeshDeviceMapping>().HasIndex(x => x.NodeId).IsUnique();
        b.Entity<MeshDeviceMapping>().HasOne<Device>().WithOne().HasForeignKey<MeshDeviceMapping>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RemoteSession>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RemoteSession>().HasIndex(x => new { x.DeviceId, x.CreatedAt });
        b.Entity<EnrollmentToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<EnrollmentToken>().HasOne<DeviceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EnrollmentToken>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DeviceCredential>().HasKey(x => x.DeviceId);
        b.Entity<DeviceCredential>().HasOne<Device>().WithOne().HasForeignKey<DeviceCredential>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeviceAssertion>().HasIndex(x => x.ExpiresAt);
        b.Entity<DeviceMetrics>().HasKey(x => x.DeviceId);
        b.Entity<DeviceMetrics>().Property(x => x.Payload).HasColumnType("jsonb");
        b.Entity<DeviceMetrics>().HasOne<Device>().WithOne().HasForeignKey<DeviceMetrics>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeviceInventory>().HasKey(x => x.DeviceId);
        b.Entity<DeviceInventory>().Property(x => x.Payload).HasColumnType("jsonb");
        b.Entity<DeviceInventory>().HasOne<Device>().WithOne().HasForeignKey<DeviceInventory>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeviceGroup>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Device>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Device>().HasOne<DeviceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Device>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Device>().HasIndex(x => x.SiteId);
        b.Entity<Site>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Site>().HasIndex(x => x.Name).IsUnique();
        b.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.Permission });
        b.Entity<RolePermission>().HasOne<IdentityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().ToTable(t => t.HasCheckConstraint("CK_ScopeGrant_OneScope", "num_nonnulls(\"GroupId\", \"DeviceId\", \"SiteId\") = 1"));
        b.Entity<ScopeGrant>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasOne<IdentityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasOne<DeviceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ScopeGrant>().HasIndex(x => new { x.UserId, x.RoleId, x.GroupId }).IsUnique();
        b.Entity<ScopeGrant>().HasIndex(x => new { x.UserId, x.RoleId, x.DeviceId }).IsUnique();
        b.Entity<ScopeGrant>().HasIndex(x => new { x.UserId, x.RoleId, x.SiteId }).IsUnique();
        b.Entity<Branding>().ToTable(t => t.HasCheckConstraint("CK_Branding_Singleton", "\"Id\" = 1"));
        b.Entity<Branding>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<Branding>().Property(x => x.LogoUrl).HasMaxLength(2048);
        b.Entity<Branding>().HasData(new Branding());
        b.Entity<AuditEvent>().HasIndex(x => x.At);
        b.Entity<CoreOutboxEvent>().Property(x => x.EventType).HasMaxLength(100);
        b.Entity<CoreOutboxEvent>().Property(x => x.AggregateType).HasMaxLength(50);
        b.Entity<CoreOutboxEvent>().Property(x => x.CorrelationId).HasMaxLength(64);
        b.Entity<CoreOutboxEvent>().Property(x => x.Payload).HasColumnType("jsonb");
        b.Entity<CoreOutboxEvent>().HasIndex(x => new { x.NextAttemptAt, x.OccurredAt })
            .HasFilter("\"PublishedAt\" IS NULL");
        foreach (var role in Security.Permissions.Roles)
        {
            b.Entity<IdentityRole>().HasData(new IdentityRole { Id = role, Name = role, NormalizedName = role.ToUpperInvariant(), ConcurrencyStamp = role });
            foreach (var permission in Security.Permissions.ForRole(role))
                b.Entity<RolePermission>().HasData(new RolePermission { RoleId = role, Permission = permission });
        }
    }
}
