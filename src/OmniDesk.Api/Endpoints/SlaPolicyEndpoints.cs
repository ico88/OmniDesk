using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;

namespace OmniDesk.Api.Endpoints;

public static class SlaPolicyEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapSlaPolicyEndpoints(this WebApplication app)
    {
        var policies = app.MapGroup("/api/admin/support-sla/policies").RequireAuthorization();
        // Return only scopes the caller can manage; scoped managers must not see unrelated sites.
        app.MapGet("/api/admin/support-sla/scopes", async (HttpContext context, AppDbContext db, PermissionService permissions) =>
        {
            var userId = context.User.UserId();
            var canManageGlobal = await permissions.Allows(userId, "tickets.sla.manage");
            var sites = new List<object>();
            foreach (var site in await db.Sites.AsNoTracking().Where(x => !x.IsArchived).OrderBy(x => x.Name).ToArrayAsync())
                if (await permissions.Allows(userId, "tickets.sla.manage", siteId: site.Id))
                    sites.Add(new { site.Id, site.Name });
            if (!canManageGlobal && sites.Count == 0) return Results.Forbid();
            return Results.Ok(new { canManageGlobal, sites });
        }).RequireAuthorization();

        policies.MapGet("", async (HttpContext context, AppDbContext db, PermissionService permissions) =>
        {
            var userId = context.User.UserId();
            var rows = await db.SupportSlaPolicies.AsNoTracking().OrderBy(x => x.ScopeKey).ToArrayAsync();
            var result = new List<object>();
            foreach (var row in rows)
            {
                if (!await permissions.Allows(userId, "tickets.sla.manage", siteId: row.SiteId)) continue;
                var storedVersions = await db.SupportSlaPolicyVersions.AsNoTracking()
                    .Where(x => x.PolicyId == row.Id).OrderBy(x => x.Version).ToArrayAsync();
                var versions = storedVersions.Select(x => new
                {
                    x.Version, x.Name, x.SiteId,
                    definition = JsonSerializer.Deserialize<SlaPolicyDefinition>(x.DefinitionJson, Json),
                    x.PublishedAt
                }).ToArray();
                result.Add(new { row.Id, row.Name, row.SiteId, row.ScopeKey, row.CurrentVersion, row.Version, versions });
            }
            return Results.Ok(result);
        });

        policies.MapPost("", async (SlaPolicyCreate request, HttpContext context, AppDbContext db, PermissionService permissions) =>
        {
            string? validation = null;
            if (!ValidName(request.Name) || !TryValidate(request.Definition, out validation))
                return Results.BadRequest(new { error = validation ?? "Nome o definizione SLA non validi." });
            var userId = context.User.UserId();
            if (!await permissions.Allows(userId, "tickets.sla.manage", siteId: request.SiteId)) return Results.Forbid();
            if (request.SiteId is not null && !await db.Sites.AnyAsync(x => x.Id == request.SiteId && !x.IsArchived))
                return Results.BadRequest(new { error = "La sede selezionata non è disponibile." });

            var scopeKey = request.SiteId is null ? "global" : "site:" + request.SiteId.Value.ToString("D");
            if (await db.SupportSlaPolicies.AnyAsync(x => x.ScopeKey == scopeKey))
                return Results.Conflict(new { error = "Esiste già una policy SLA per questo ambito." });
            var policy = new SupportSlaPolicy { SiteId = request.SiteId, ScopeKey = scopeKey, Name = request.Name.Trim(), CurrentVersion = 1 };
            db.SupportSlaPolicies.Add(policy);
            db.SupportSlaPolicyVersions.Add(Version(policy, 1, request.Name, request.Definition, userId));
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.sla.policy.published", Target = policy.Id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Esiste già una policy SLA per questo ambito." }); }
            return Results.Created($"/api/admin/support-sla/policies/{policy.Id}", new { policy.Id, policy.ScopeKey, policy.CurrentVersion, policy.Version });
        });

        policies.MapPost("/{id:guid}/versions", async (Guid id, SlaPolicyVersionCreate request, HttpContext context, AppDbContext db, PermissionService permissions) =>
        {
            string? validation = null;
            if (request.ExpectedVersion < 1 || !ValidName(request.Name) || !TryValidate(request.Definition, out validation))
                return Results.BadRequest(new { error = validation ?? "Versione, nome o definizione SLA non validi." });
            var policy = await db.SupportSlaPolicies.SingleOrDefaultAsync(x => x.Id == id);
            if (policy is null) return Results.NotFound();
            var userId = context.User.UserId();
            if (!await permissions.Allows(userId, "tickets.sla.manage", siteId: policy.SiteId)) return Results.Forbid();
            if (policy.Version != request.ExpectedVersion)
                return Results.Conflict(new { error = "La policy è stata aggiornata. Ricarica la pagina.", currentVersion = policy.Version });

            var nextVersion = policy.CurrentVersion + 1;
            policy.CurrentVersion = nextVersion;
            policy.Version++;
            policy.UpdatedAt = DateTimeOffset.UtcNow;
            db.SupportSlaPolicyVersions.Add(Version(policy, nextVersion, request.Name, request.Definition, userId));
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.sla.policy.version.published", Target = id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "La policy è stata aggiornata. Ricarica la pagina." }); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { return Results.Conflict(new { error = "La versione è stata pubblicata da una richiesta concorrente. Ricarica la policy." }); }
            return Results.Ok(new { policy.Id, policy.CurrentVersion, policy.Version });
        });
    }

    private static SupportSlaPolicyVersion Version(SupportSlaPolicy policy, int version, string name, SlaPolicyDefinition definition, string userId) => new()
    {
        PolicyId = policy.Id,
        Version = version,
        Name = name.Trim(),
        SiteId = policy.SiteId,
        DefinitionJson = JsonSerializer.Serialize(definition, Json),
        PublishedBy = userId,
        PublishedAt = DateTimeOffset.UtcNow
    };

    private static bool TryValidate(SlaPolicyDefinition? definition, out string? error)
    {
        error = null;
        if (definition is null || definition.Calendar is null || definition.Calendar.WeeklyHours is null || definition.Calendar.Holidays is null ||
            definition.FirstResponseMinutes is < 1 or > 525600 || definition.ResolutionMinutes is < 1 or > 525600 ||
            definition.ResolutionMinutes < definition.FirstResponseMinutes || definition.Calendar.Holidays.Length > 3660 ||
            definition.Calendar.WeeklyHours.Count == 0 || definition.Calendar.WeeklyHours.Values.Any(ranges => ranges is null || ranges.Length == 0) ||
            definition.PauseStates is null || definition.PauseStates.Length > 20 ||
            definition.PauseStates.Distinct(StringComparer.Ordinal).Count() != definition.PauseStates.Length ||
            definition.PauseStates.Any(state => state is null || state is "resolved" or "closed" ||
                !Regex.IsMatch(state, "^[a-z][a-z0-9_]{0,39}$", RegexOptions.CultureInvariant)))
        {
            error = "Definizione SLA fuori dai limiti supportati.";
            return false;
        }

        try
        {
            BusinessCalendar.ValidateDefinition(new BusinessCalendarDefinition(
                definition.Calendar.TimeZoneId,
                definition.Calendar.WeeklyHours.ToDictionary(x => x.Key, x => (IReadOnlyList<BusinessTimeRange>)x.Value),
                definition.Calendar.Holidays.ToHashSet()));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            error = "Calendario o fuso orario non valido.";
            return false;
        }
    }

    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 100;
}
