using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OmniDesk.Api.Data;

public sealed class AppUser : IdentityUser { public bool IsDisabled { get; set; } public string? DisplayName { get; set; } public bool EmailNotificationsEnabled { get; set; } public DateTimeOffset? EmailNotificationsEnabledAt { get; set; } }
public sealed class DeviceGroup { public Guid Id { get; set; } public string Name { get; set; } = ""; }
public sealed class Site { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; public bool IsArchived { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; }
public sealed class Device { public Guid Id { get; set; } public string Name { get; set; } = ""; public Guid? GroupId { get; set; } public Guid SiteId { get; set; } }
public sealed class DevicePresence { public Guid DeviceId { get; set; } public bool IsOnline { get; set; } public DateTimeOffset ChangedAt { get; set; } }
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
public sealed class BackupNasSettings
{
    public int Id { get; set; } = 1;
    public string ProtectedConfiguration { get; set; } = "";
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string LastStatus { get; set; } = "never";
    public string? LastErrorCode { get; set; }
}
public sealed class OutboundEmailSettings
{
    public int Id { get; set; } = 1;
    public string ProtectedConfiguration { get; set; } = "";
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
    public DbSet<SupportTicketDevice> SupportTicketDevices => Set<SupportTicketDevice>();
    public DbSet<SupportTicketParticipant> SupportTicketParticipants => Set<SupportTicketParticipant>();
    public DbSet<SupportEmailIntake> SupportEmailIntakes => Set<SupportEmailIntake>();
    public DbSet<SupportAttachment> SupportAttachments => Set<SupportAttachment>();
    public DbSet<SupportInterventionSummary> SupportInterventionSummaries => Set<SupportInterventionSummary>();
    public DbSet<PortalDeviceAccess> PortalDeviceAccesses => Set<PortalDeviceAccess>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportWorkflow> SupportWorkflows => Set<SupportWorkflow>();
    public DbSet<SupportWorkflowVersion> SupportWorkflowVersions => Set<SupportWorkflowVersion>();
    public DbSet<SupportSlaPolicy> SupportSlaPolicies => Set<SupportSlaPolicy>();
    public DbSet<SupportSlaPolicyVersion> SupportSlaPolicyVersions => Set<SupportSlaPolicyVersion>();
    public DbSet<SupportTicketSlaTarget> SupportTicketSlaTargets => Set<SupportTicketSlaTarget>();
    public DbSet<SupportTicketSlaEscalation> SupportTicketSlaEscalations => Set<SupportTicketSlaEscalation>();
    public DbSet<SupportQueue> SupportQueues => Set<SupportQueue>();
    public DbSet<SupportQueueMember> SupportQueueMembers => Set<SupportQueueMember>();
    public DbSet<SupportQueueRoutingRule> SupportQueueRoutingRules => Set<SupportQueueRoutingRule>();
    public DbSet<DeviceGroup> DeviceGroups => Set<DeviceGroup>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DevicePresence> DevicePresence => Set<DevicePresence>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ScopeGrant> ScopeGrants => Set<ScopeGrant>();
    public DbSet<Branding> Branding => Set<Branding>();
    public DbSet<BackupNasSettings> BackupNasSettings => Set<BackupNasSettings>();
    public DbSet<OutboundEmailSettings> OutboundEmailSettings => Set<OutboundEmailSettings>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<DeviceAssertion> DeviceAssertions => Set<DeviceAssertion>();
    public DbSet<DeviceMetrics> DeviceMetrics => Set<DeviceMetrics>();
    public DbSet<DeviceInventory> DeviceInventories => Set<DeviceInventory>();
    public DbSet<MeshDeviceMapping> MeshDeviceMappings => Set<MeshDeviceMapping>();
    public DbSet<RustDeskDeviceMapping> RustDeskDeviceMappings => Set<RustDeskDeviceMapping>();
    public DbSet<RemoteProvisioning> RemoteProvisionings => Set<RemoteProvisioning>();
    public DbSet<RemoteSession> RemoteSessions => Set<RemoteSession>();
    public DbSet<UpdateJob> UpdateJobs => Set<UpdateJob>();
    public DbSet<DashboardLayout> DashboardLayouts => Set<DashboardLayout>();
    public DbSet<CoreOutboxEvent> CoreOutboxEvents => Set<CoreOutboxEvent>();
    public DbSet<CoreOutboxDelivery> CoreOutboxDeliveries => Set<CoreOutboxDelivery>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<EmailNotificationDelivery> EmailNotificationDeliveries => Set<EmailNotificationDelivery>();
    public DbSet<EmailLoginChallenge> EmailLoginChallenges => Set<EmailLoginChallenge>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<KnowledgeArticleRevision> KnowledgeArticleRevisions => Set<KnowledgeArticleRevision>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(80);
        b.Entity<RemoteProvisioning>().HasKey(x => x.DeviceId);
        b.Entity<RemoteProvisioning>().HasOne<Device>().WithOne().HasForeignKey<RemoteProvisioning>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportMessage>().HasOne<SupportTicket>().WithMany().HasForeignKey(x=>x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportMessage>().HasIndex(x=>new {x.TicketId,x.Id});
        b.Entity<SupportMessage>().Property(x=>x.Body).HasMaxLength(8000);
        b.Entity<SupportMessage>().Property(x=>x.Kind).HasMaxLength(20);
        b.Entity<SupportMessage>().Property(x=>x.Visibility).HasMaxLength(20).HasDefaultValue("public");
        b.Entity<SupportMessage>().Property(x=>x.FromState).HasMaxLength(20);
        b.Entity<SupportMessage>().Property(x=>x.ToState).HasMaxLength(20);
        b.Entity<SupportTicketDevice>().HasKey(x => new { x.TicketId, x.DeviceId });
        b.Entity<SupportTicketDevice>().Property(x => x.DeviceNameSnapshot).HasMaxLength(200);
        b.Entity<SupportTicketDevice>().Property(x => x.SiteNameSnapshot).HasMaxLength(200);
        b.Entity<SupportTicketDevice>().HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportTicketDevice>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicketDevice>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.LinkedBy).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportTicketDevice>().HasIndex(x => new { x.DeviceId, x.TicketId });
        b.Entity<SupportTicketParticipant>().HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportTicketParticipant>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportTicketParticipant>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportTicketParticipant>().HasIndex(x => new { x.TicketId, x.UserId }).IsUnique();
        b.Entity<SupportTicketParticipant>().HasIndex(x => new { x.UserId, x.AddedAt });
        b.Entity<SupportEmailIntake>().Property(x => x.MailboxAddress).HasMaxLength(320);
        b.Entity<SupportEmailIntake>().Property(x => x.MessageId).HasMaxLength(512);
        b.Entity<SupportEmailIntake>().Property(x => x.SenderAddress).HasMaxLength(320);
        b.Entity<SupportEmailIntake>().Property(x => x.Subject).HasMaxLength(200);
        b.Entity<SupportEmailIntake>().Property(x => x.BodyText).HasMaxLength(20000);
        b.Entity<SupportEmailIntake>().Property(x => x.PayloadSha256).HasMaxLength(64);
        b.Entity<SupportEmailIntake>().Property(x => x.State).HasMaxLength(20);
        b.Entity<SupportEmailIntake>().Property(x => x.QualifiedBy).HasMaxLength(450);
        b.Entity<SupportEmailIntake>().HasIndex(x => new { x.MailboxAddress, x.MessageId }).IsUnique();
        b.Entity<SupportEmailIntake>().HasIndex(x => new { x.State, x.ReceivedAt });
        b.Entity<SupportEmailIntake>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.QualifiedBy).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportEmailIntake>().HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.PromotedTicketId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportAttachment>().HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportAttachment>().HasOne<SupportMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportAttachment>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploaderId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportAttachment>().Property(x => x.StorageKey).HasMaxLength(32);
        b.Entity<SupportAttachment>().Property(x => x.OriginalFileName).HasMaxLength(255);
        b.Entity<SupportAttachment>().Property(x => x.MediaType).HasMaxLength(128);
        b.Entity<SupportAttachment>().Property(x => x.Sha256).HasMaxLength(64);
        b.Entity<SupportAttachment>().Property(x => x.Visibility).HasMaxLength(20);
        b.Entity<SupportAttachment>().Property(x => x.ScanState).HasMaxLength(24);
        b.Entity<SupportAttachment>().Property(x => x.ScannerVersion).HasMaxLength(64);
        b.Entity<SupportAttachment>().Property(x => x.LastScanErrorType).HasMaxLength(64);
        b.Entity<SupportAttachment>().ToTable(t => {
            t.HasCheckConstraint("CK_SupportAttachment_Size", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 10485760");
            t.HasCheckConstraint("CK_SupportAttachment_Visibility", "\"Visibility\" IN ('public', 'internal')");
            t.HasCheckConstraint("CK_SupportAttachment_ScanState", "\"ScanState\" IN ('quarantined', 'scanning', 'clean', 'infected', 'scan_failed', 'retention_pending')");
            t.HasCheckConstraint("CK_SupportAttachment_Sha256", "length(\"Sha256\") = 64");
        });
        b.Entity<SupportAttachment>().HasIndex(x => x.StorageKey).IsUnique();
        b.Entity<SupportAttachment>().HasIndex(x => new { x.TicketId, x.CreatedAt });
        b.Entity<SupportAttachment>().HasIndex(x => x.MessageId);
        b.Entity<SupportAttachment>().HasIndex(x => new { x.ScanState, x.NextScanAt, x.ScanLeaseUntil });
        b.Entity<SupportInterventionSummary>().HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportInterventionSummary>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportInterventionSummary>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportInterventionSummary>().Property(x => x.ArtifactType).HasMaxLength(20);
        b.Entity<SupportInterventionSummary>().Property(x => x.Capability).HasMaxLength(40);
        b.Entity<SupportInterventionSummary>().Property(x => x.Summary).HasMaxLength(4000);
        b.Entity<SupportInterventionSummary>().Property(x => x.Outcome).HasMaxLength(24);
        b.Entity<SupportInterventionSummary>().Property(x => x.FollowUp).HasMaxLength(1000);
        b.Entity<SupportInterventionSummary>().HasIndex(x => new { x.TicketId, x.RecordedAt });
        b.Entity<SupportInterventionSummary>().HasIndex(x => new { x.DeviceId, x.ArtifactType, x.ArtifactId });
        b.Entity<SupportTicket>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SupportTicket>().HasIndex(x => new { x.RequesterUserId, x.CreatedAt });
        b.Entity<SupportTicket>().HasIndex(x => new { x.DeviceId, x.RequesterKey, x.RequestId }).IsUnique();
        b.Entity<SupportTicket>().HasIndex(x => new { x.DeviceId, x.CreatedAt });
        b.Entity<SupportTicket>().Property(x => x.Number).UseIdentityByDefaultColumn();
        b.Entity<SupportTicket>().HasIndex(x => x.Number).IsUnique();
        b.Entity<SupportTicket>().Property(x => x.RequesterKey).HasMaxLength(64);
        b.Entity<SupportTicket>().Property(x => x.Origin).HasMaxLength(20).HasDefaultValue("device");
        b.Entity<SupportTicket>().Property(x => x.Subject).HasMaxLength(200);
        b.Entity<SupportTicket>().Property(x => x.Description).HasMaxLength(8000);
        b.Entity<SupportTicket>().Property(x => x.Resolution).HasMaxLength(8000);
        b.Entity<SupportTicket>().Property(x => x.State).HasMaxLength(20);
        b.Entity<SupportTicket>().Property(x => x.Priority).HasMaxLength(20).HasDefaultValue("normal");
        b.Entity<SupportTicket>().Property(x => x.Category).HasMaxLength(32).HasDefaultValue("general");
        b.Entity<PortalDeviceAccess>().HasKey(x => new { x.UserId, x.DeviceId });
        b.Entity<PortalDeviceAccess>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PortalDeviceAccess>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PortalDeviceAccess>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.SetNull);
        b.Entity<PortalDeviceAccess>().HasIndex(x => x.DeviceId);
        b.Entity<SupportTicket>().HasOne<SupportQueue>().WithMany().HasForeignKey(x => x.QueueId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportQueue>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<SupportQueue>().Property(x => x.NormalizedName).HasMaxLength(100);
        b.Entity<SupportQueue>().Property(x => x.Version).HasDefaultValue(1L);
        b.Entity<SupportQueue>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportQueue>().HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"SiteId\" IS NULL");
        b.Entity<SupportQueue>().HasIndex(x => new { x.SiteId, x.NormalizedName }).IsUnique().HasFilter("\"SiteId\" IS NOT NULL");
        b.Entity<SupportQueueMember>().HasKey(x => new { x.QueueId, x.UserId });
        b.Entity<SupportQueueMember>().HasOne<SupportQueue>().WithMany().HasForeignKey(x => x.QueueId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportQueueMember>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportQueueMember>().HasIndex(x => x.UserId);
        b.Entity<SupportQueueRoutingRule>().Property(x => x.Category).HasMaxLength(32);
        b.Entity<SupportQueueRoutingRule>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
        b.Entity<SupportQueueRoutingRule>().HasOne<SupportQueue>().WithMany().HasForeignKey(x => x.QueueId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportQueueRoutingRule>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportQueueRoutingRule>().HasIndex(x => x.Category).IsUnique().HasFilter("\"SiteId\" IS NULL");
        b.Entity<SupportQueueRoutingRule>().HasIndex(x => new { x.SiteId, x.Category }).IsUnique().HasFilter("\"SiteId\" IS NOT NULL");
        b.Entity<SupportTicket>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasIndex(x => new { x.SiteId, x.State, x.Priority, x.CreatedAt });
        b.Entity<SupportTicket>().HasIndex(x => new { x.SiteId, x.Category, x.State, x.CreatedAt });
        b.Entity<SupportTicket>().HasIndex(x => new { x.QueueId, x.State, x.CreatedAt });
        b.Entity<SupportTicket>().HasIndex(x => x.AssigneeId);
        b.Entity<SupportWorkflow>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<SupportWorkflow>().Property(x => x.ScopeKey).HasMaxLength(64);
        b.Entity<SupportWorkflow>().Property(x => x.DraftDefinitionJson).HasColumnType("jsonb");
        b.Entity<SupportWorkflow>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
        b.Entity<SupportWorkflow>().HasIndex(x => x.ScopeKey).IsUnique();
        b.Entity<SupportWorkflow>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportWorkflowVersion>().HasKey(x => new { x.WorkflowId, x.Version });
        b.Entity<SupportWorkflowVersion>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<SupportWorkflowVersion>().Property(x => x.DefinitionJson).HasColumnType("jsonb");
        b.Entity<SupportWorkflowVersion>().Property(x => x.PublishedBy).HasMaxLength(450);
        b.Entity<SupportWorkflowVersion>().HasOne<SupportWorkflow>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<SupportWorkflowVersion>().WithMany()
            .HasForeignKey(x => new { x.WorkflowId, x.WorkflowVersion }).OnDelete(DeleteBehavior.Restrict);

        b.Entity<SupportSlaPolicy>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<SupportSlaPolicy>().Property(x => x.ScopeKey).HasMaxLength(64);
        b.Entity<SupportSlaPolicy>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
        b.Entity<SupportSlaPolicy>().HasIndex(x => x.ScopeKey).IsUnique();
        b.Entity<SupportSlaPolicy>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportSlaPolicyVersion>().HasKey(x => new { x.PolicyId, x.Version });
        b.Entity<SupportSlaPolicyVersion>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<SupportSlaPolicyVersion>().Property(x => x.DefinitionJson).HasColumnType("jsonb");
        b.Entity<SupportSlaPolicyVersion>().Property(x => x.PublishedBy).HasMaxLength(450);
        b.Entity<SupportSlaPolicyVersion>().HasOne<SupportSlaPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportSlaPolicyVersion>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicketSlaTarget>().Property(x => x.TargetType).HasMaxLength(24);
        b.Entity<SupportTicketSlaTarget>().HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportTicketSlaTarget>().HasOne<SupportSlaPolicyVersion>().WithMany()
            .HasForeignKey(x => new { x.PolicyId, x.PolicyVersion }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicketSlaTarget>().HasIndex(x => new { x.TicketId, x.TargetType, x.Cycle }).IsUnique();
        b.Entity<SupportTicketSlaTarget>().HasIndex(x => new { x.DueAt, x.CompletedAt, x.BreachedAt });
        b.Entity<SupportTicketSlaEscalation>().HasKey(x => new { x.TargetId, x.StepMinutes });
        b.Entity<SupportTicketSlaEscalation>().HasOne<SupportTicketSlaTarget>().WithMany()
            .HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<DashboardLayout>().HasKey(x => x.UserId);
        b.Entity<DashboardLayout>().HasOne<AppUser>().WithOne().HasForeignKey<DashboardLayout>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DashboardLayout>().Property(x => x.Widgets).HasColumnType("jsonb");
        b.Entity<UpdateJob>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<UpdateJob>().HasIndex(x => new { x.DeviceId, x.RequestId }).IsUnique();
        b.Entity<UpdateJob>().Property(x => x.Payload).HasColumnType("jsonb");
        b.Entity<UpdateJob>().Property(x => x.Result).HasColumnType("jsonb");
        b.Entity<MeshDeviceMapping>().HasKey(x => x.DeviceId);
        b.Entity<MeshDeviceMapping>().HasIndex(x => x.NodeId).IsUnique();
        b.Entity<MeshDeviceMapping>().HasOne<Device>().WithOne().HasForeignKey<MeshDeviceMapping>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RustDeskDeviceMapping>().HasKey(x => x.DeviceId);
        b.Entity<RustDeskDeviceMapping>().Property(x => x.PeerId).HasMaxLength(32);
        b.Entity<RustDeskDeviceMapping>().HasIndex(x => x.PeerId).IsUnique();
        b.Entity<RustDeskDeviceMapping>().HasOne<Device>().WithOne().HasForeignKey<RustDeskDeviceMapping>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
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
        b.Entity<DevicePresence>().HasKey(x => x.DeviceId);
        b.Entity<DevicePresence>().HasOne<Device>().WithOne().HasForeignKey<DevicePresence>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
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
        b.Entity<BackupNasSettings>().ToTable(t => t.HasCheckConstraint("CK_BackupNasSettings_Singleton", "\"Id\" = 1"));
        b.Entity<BackupNasSettings>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<BackupNasSettings>().Property(x => x.ProtectedConfiguration).HasColumnType("text");
        b.Entity<BackupNasSettings>().Property(x => x.LastStatus).HasMaxLength(20);
        b.Entity<BackupNasSettings>().Property(x => x.LastErrorCode).HasMaxLength(40);
        b.Entity<OutboundEmailSettings>().ToTable(t => t.HasCheckConstraint("CK_OutboundEmailSettings_Singleton", "\"Id\" = 1"));
        b.Entity<OutboundEmailSettings>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<OutboundEmailSettings>().Property(x => x.ProtectedConfiguration).HasColumnType("text");
        b.Entity<AuditEvent>().HasIndex(x => x.At);
        b.Entity<CoreOutboxEvent>().Property(x => x.EventType).HasMaxLength(100);
        b.Entity<CoreOutboxEvent>().Property(x => x.AggregateType).HasMaxLength(50);
        b.Entity<CoreOutboxEvent>().Property(x => x.CorrelationId).HasMaxLength(64);
        b.Entity<CoreOutboxEvent>().Property(x => x.Payload).HasColumnType("jsonb");
        b.Entity<CoreOutboxEvent>().HasIndex(x => new { x.NextAttemptAt, x.OccurredAt })
            .HasFilter("\"PublishedAt\" IS NULL");
        b.Entity<CoreOutboxDelivery>().HasKey(x => new { x.EventId, x.ConsumerName });
        b.Entity<CoreOutboxDelivery>().Property(x => x.ConsumerName).HasMaxLength(100);
        b.Entity<CoreOutboxDelivery>().Property(x => x.LastErrorType).HasMaxLength(100);
        b.Entity<CoreOutboxDelivery>().HasOne<CoreOutboxEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CoreOutboxDelivery>().HasIndex(x => new { x.ConsumerName, x.ProcessedAt, x.DeadLetteredAt, x.NextAttemptAt });
        b.Entity<UserNotification>().Property(x => x.Kind).HasMaxLength(40);
        b.Entity<UserNotification>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserNotification>().HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserNotification>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserNotification>().HasOne<CoreOutboxEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserNotification>().HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
        b.Entity<UserNotification>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<UserNotification>().ToTable(t => t.HasCheckConstraint("CK_UserNotification_Target", "(\"TicketId\" IS NOT NULL AND \"DeviceId\" IS NULL) OR (\"TicketId\" IS NULL AND \"DeviceId\" IS NOT NULL)"));
        b.Entity<EmailNotificationDelivery>().HasKey(x => x.NotificationId);
        b.Entity<EmailNotificationDelivery>().HasOne<UserNotification>().WithOne().HasForeignKey<EmailNotificationDelivery>(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<EmailNotificationDelivery>().Property(x => x.State).HasMaxLength(20);
        b.Entity<EmailNotificationDelivery>().Property(x => x.LastErrorType).HasMaxLength(64);
        b.Entity<EmailNotificationDelivery>().HasIndex(x => new { x.State, x.NextAttemptAt, x.LeaseUntil });
        b.Entity<EmailLoginChallenge>().HasKey(x => x.Id);
        b.Entity<EmailLoginChallenge>().Property(x => x.UserId).HasMaxLength(450);
        b.Entity<EmailLoginChallenge>().Property(x => x.TokenHash).HasMaxLength(64);
        b.Entity<EmailLoginChallenge>().Property(x => x.CodeHash).HasMaxLength(512);
        b.Entity<EmailLoginChallenge>().Property(x => x.SecurityStamp).HasMaxLength(64);
        b.Entity<EmailLoginChallenge>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<EmailLoginChallenge>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<EmailLoginChallenge>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<EmailLoginChallenge>().HasIndex(x => x.ExpiresAt);
        b.Entity<KnowledgeArticle>().Property(x => x.Slug).HasMaxLength(120);
        b.Entity<KnowledgeArticle>().Property(x => x.NormalizedSlug).HasMaxLength(120);
        b.Entity<KnowledgeArticle>().Property(x => x.Title).HasMaxLength(160);
        b.Entity<KnowledgeArticle>().Property(x => x.Summary).HasMaxLength(500);
        b.Entity<KnowledgeArticle>().Property(x => x.Body).HasMaxLength(30000);
        b.Entity<KnowledgeArticle>().HasIndex(x => x.NormalizedSlug).IsUnique();
        b.Entity<KnowledgeArticle>().HasIndex(x => new { x.IsPublished, x.UpdatedAt });
        b.Entity<KnowledgeArticle>().ToTable(t => t.HasCheckConstraint("CK_KnowledgeArticle_OneScope", "num_nonnulls(\"GroupId\", \"SiteId\") <= 1"));
        b.Entity<KnowledgeArticle>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KnowledgeArticle>().HasOne<DeviceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KnowledgeArticle>().HasIndex(x => x.SiteId);
        b.Entity<KnowledgeArticle>().HasIndex(x => x.GroupId);
        b.Entity<KnowledgeArticleRevision>().HasKey(x => new { x.ArticleId, x.Version });
        b.Entity<KnowledgeArticleRevision>().Property(x => x.Slug).HasMaxLength(120);
        b.Entity<KnowledgeArticleRevision>().Property(x => x.Title).HasMaxLength(160);
        b.Entity<KnowledgeArticleRevision>().Property(x => x.Summary).HasMaxLength(500);
        b.Entity<KnowledgeArticleRevision>().Property(x => x.Body).HasMaxLength(30000);
        b.Entity<KnowledgeArticleRevision>().Property(x => x.RecordedBy).HasMaxLength(450);
        b.Entity<KnowledgeArticleRevision>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KnowledgeArticleRevision>().HasOne<DeviceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KnowledgeArticleRevision>().HasOne<KnowledgeArticle>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<KnowledgeArticleRevision>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.SetNull);
        foreach (var role in Security.Permissions.Roles)
        {
            b.Entity<IdentityRole>().HasData(new IdentityRole { Id = role, Name = role, NormalizedName = role.ToUpperInvariant(), ConcurrencyStamp = role });
            foreach (var permission in Security.Permissions.ForRole(role))
                b.Entity<RolePermission>().HasData(new RolePermission { RoleId = role, Permission = permission });
        }
    }
}
