using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniDesk.Api.Data;
using OmniDesk.Api.Attachments;
using OmniDesk.Api.Endpoints;
using OmniDesk.Api.Security;
using OmniDesk.Api.Maintenance;
using OmniDesk.Api.ServiceDesk;
using OmniDesk.Remote.MeshCentral;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var connection = builder.Configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(connection))
{
    var secretPath = builder.Configuration["Database:PasswordFile"] ?? "/run/secrets/db_password";
    var cs = new NpgsqlConnectionStringBuilder {
        Host = builder.Configuration["Database:Host"] ?? "db", Database = "omnidesk", Username = "omnidesk",
        Password = File.Exists(secretPath) ? File.ReadAllText(secretPath).Trim() : ""
    };
    // Docker PostgreSQL uses password authentication, not optional Kerberos native libraries.
    cs["GSS Encryption Mode"] = "Disable";
    connection = cs.ConnectionString;
}
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddIdentity<AppUser, IdentityRole>(o => {
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 14;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDbContext>().AddUserStore<ProtectedUserStore>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => {
    o.Cookie.Name = "__Host-OmniDesk.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = ActiveAccountCookie.Validate;
});
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.AddAntiforgery(o => {
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "__Host-OmniDesk.Csrf";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});
var keysPath = builder.Configuration["DataProtection:Path"];
if (!string.IsNullOrEmpty(keysPath))
    builder.Services.AddDataProtection().SetApplicationName("OmniDesk").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddScoped<PermissionService>();
builder.Services.Configure<OutboundEmailOptions>(builder.Configuration.GetSection(OutboundEmailOptions.SectionName));
var outboundEmail = builder.Configuration.GetSection(OutboundEmailOptions.SectionName).Get<OutboundEmailOptions>() ?? new OutboundEmailOptions();
if (!outboundEmail.IsValid()) throw new InvalidOperationException("Outbound email configuration is invalid; SMTP with TLS on port 465 or 587 is required.");
builder.Services.AddSingleton<IOutboundEmailSender, SmtpOutboundEmailSender>();
builder.Services.AddSingleton<AttachmentStorage>();
builder.Services.AddSingleton<IAttachmentScanner, ClamAvAttachmentScanner>();
builder.Services.AddHostedService<AttachmentScanWorker>();
builder.Services.AddSingleton<IMeshCentralClient>(new MeshCentralClient(new MeshCentralOptions(builder.Configuration["MeshCentral:Origin"], builder.Configuration["MeshCentral:Username"], builder.Configuration["MeshCentral:PasswordFile"], builder.Configuration.GetValue("OMNIDESK_REMOTE_SESSION_MINUTES", 30))));
builder.Services.AddSingleton<IMeshProvisioningClient>(sp => (IMeshProvisioningClient)sp.GetRequiredService<IMeshCentralClient>());
builder.Services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DeviceAuthentication>(DeviceAuthentication.SchemeName, _ => {});
builder.Services.AddHostedService<AssertionCleanup>();
builder.Services.AddHostedService<RemoteSessionGuard>();
builder.Services.AddHostedService<RemoteProvisioningGuard>();
builder.Services.AddHostedService<TicketNotificationWorker>();
builder.Services.AddHostedService<EmailNotificationWorker>();
builder.Services.AddHostedService<SlaBreachWorker>();
builder.Services.AddHostedService<SlaEscalationWorker>();
builder.Services.Configure<DevicePresenceOptions>(builder.Configuration.GetSection(DevicePresenceOptions.SectionName));
builder.Services.AddHostedService<DevicePresenceWorker>();
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
builder.Services.AddHostedService<CoreOutboxRetentionWorker>();
builder.Services.AddAuthorization();
var trustedProxyNetwork = builder.Configuration["Proxy:Network"];
if (!string.IsNullOrEmpty(trustedProxyNetwork))
    builder.Services.Configure<ForwardedHeadersOptions>(o => {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(trustedProxyNetwork));
    });
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("enrollment", options => { options.PermitLimit = 30; options.Window = TimeSpan.FromMinutes(1); options.QueueLimit = 0; });
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}
if (args.Contains("--bootstrap"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (await db.Users.AnyAsync()) throw new InvalidOperationException("Bootstrap refused: users already exist.");
    var email = builder.Configuration["Bootstrap:Email"] ?? throw new InvalidOperationException("Bootstrap email required.");
    var file = builder.Configuration["Bootstrap:PasswordFile"] ?? "/run/secrets/bootstrap_password";
    var password = (await File.ReadAllTextAsync(file)).Trim();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    await using var tx = await db.Database.BeginTransactionAsync();
    var user = new AppUser { UserName = email, Email = email };
    var result = await users.CreateAsync(user, password);
    if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Code)));
    result = await users.AddToRoleAsync(user, "Administrator");
    if (!result.Succeeded) throw new InvalidOperationException("Cannot assign administrator.");
    db.AuditEvents.Add(new AuditEvent { ActorId = user.Id, Action = "user.bootstrap", Target = user.Id });
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    Console.WriteLine("Administrator created. Remove bootstrap secret file.");
    return;
}
app.UseExceptionHandler(handler => handler.Run(async c => {
    c.Response.StatusCode = 500;
    await c.Response.WriteAsJsonAsync(new { error = "Internal error", traceId = c.TraceIdentifier });
}));
app.Use(async (c, next) => {
    c.Response.Headers.CacheControl = "no-store";
    c.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});
if (!string.IsNullOrEmpty(trustedProxyNetwork)) app.UseForwardedHeaders();
app.Use(async (c, next) => {
    if (HttpMethods.IsPost(c.Request.Method) && c.Request.Path.Value?.EndsWith("/attachments", StringComparison.OrdinalIgnoreCase) == true)
    {
        const long attachmentRequestLimit = AttachmentStorage.MaxBytes + 512 * 1024;
        var limit = c.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = attachmentRequestLimit;
        if (c.Request.ContentLength > attachmentRequestLimit) { c.Response.StatusCode = 413; return; }
    }
    if (c.Request.Path.StartsWithSegments("/api/agent/support"))
    {
        var limit = c.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 750000;
        if (c.Request.ContentLength > 750000) { c.Response.StatusCode = 413; return; }
    }
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (c, next) => {
    if (c.Request.Method is not ("GET" or "HEAD" or "OPTIONS") &&
        c.GetEndpoint()?.Metadata.GetMetadata<MachineRequest>() is null &&
        c.GetEndpoint()?.Metadata.GetMetadata<SignedWebhookRequest>() is null)
    {
        try { await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c); }
        catch (AntiforgeryValidationException) { c.Response.StatusCode = 400; await c.Response.WriteAsJsonAsync(new { error = "Invalid CSRF token" }); return; }
    }
    await next();
});
app.MapGet("/api/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/api/health/ready", async (AppDbContext db) => {
    try { return await db.Database.CanConnectAsync() && !(await db.Database.GetPendingMigrationsAsync()).Any()
        ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapAuthEndpoints();
app.MapManagementEndpoints();
app.MapUserLifecycleEndpoints();
app.MapAgentEndpoints();
app.MapDeviceManagementEndpoints();
app.MapRemoteEndpoints();
app.MapRemoteProvisioningEndpoints();
app.MapSupportEndpoints();
app.MapUpdateEndpoints();
app.MapAccountEndpoints();
app.MapDashboardEndpoints();
app.MapDownloadEndpoints();
app.MapKnowledgeEndpoints();
app.MapNotificationEndpoints();
app.MapTicketWorkflowEndpoints();
app.MapSlaPolicyEndpoints();
app.MapEmailIntakeEndpoints();
app.MapEmailNotificationAdminEndpoints();
app.Run();
public partial class Program { }
