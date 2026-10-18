using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OmniDesk.Api.Data;

public sealed class AppUser : IdentityUser { }
public sealed class DeviceGroup { public Guid Id { get; set; } public string Name { get; set; } = ""; }
public sealed class Device { public Guid Id { get; set; } public string Name { get; set; } = ""; public Guid? GroupId { get; set; } }
public sealed class RolePermission { public string RoleId { get; set; } = ""; public string Permission { get; set; } = ""; }
public sealed class ScopeGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public Guid? GroupId { get; set; }
    public Guid? DeviceId { get; set; }
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
    public DbSet<DeviceGroup> DeviceGroups => Set<DeviceGroup>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ScopeGrant> ScopeGrants => Set<ScopeGrant>();
    public DbSet<Branding> Branding => Set<Branding>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<DeviceGroup>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Device>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Device>().HasOne<DeviceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.Permission });
        b.Entity<RolePermission>().HasOne<IdentityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().ToTable(t => t.HasCheckConstraint("CK_ScopeGrant_OneScope", "(\"GroupId\" IS NOT NULL) <> (\"DeviceId\" IS NOT NULL)"));
        b.Entity<ScopeGrant>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasOne<IdentityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasOne<DeviceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScopeGrant>().HasIndex(x => new { x.UserId, x.RoleId, x.GroupId }).IsUnique();
        b.Entity<ScopeGrant>().HasIndex(x => new { x.UserId, x.RoleId, x.DeviceId }).IsUnique();
        b.Entity<Branding>().ToTable(t => t.HasCheckConstraint("CK_Branding_Singleton", "\"Id\" = 1"));
        b.Entity<Branding>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<Branding>().Property(x => x.LogoUrl).HasMaxLength(2048);
        b.Entity<Branding>().HasData(new Branding());
        b.Entity<AuditEvent>().HasIndex(x => x.At);
        foreach (var role in Security.Permissions.Roles)
        {
            b.Entity<IdentityRole>().HasData(new IdentityRole { Id = role, Name = role, NormalizedName = role.ToUpperInvariant(), ConcurrencyStamp = role });
            foreach (var permission in Security.Permissions.ForRole(role))
                b.Entity<RolePermission>().HasData(new RolePermission { RoleId = role, Permission = permission });
        }
    }
}
