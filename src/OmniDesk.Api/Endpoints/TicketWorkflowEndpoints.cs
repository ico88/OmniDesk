using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public sealed record TicketWorkflowDraftRequest(long ExpectedVersion, string Name, TicketWorkflowDefinition Definition);
public sealed record TicketWorkflowCreateRequest(string Name, Guid? SiteId, TicketWorkflowDefinition Definition);

public static class TicketWorkflowEndpoints
{
    public static void MapTicketWorkflowEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/support-workflows").RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) =>
                await ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>()
                    .Allows(ctx.HttpContext.User.UserId(), "administration")
                    ? await next(ctx) : Results.Forbid());

        admin.MapGet("", async (AppDbContext db) =>
        {
            var rows = await db.SupportWorkflows.AsNoTracking().OrderBy(x => x.ScopeKey).ToArrayAsync();
            var result = new List<object>();
            foreach (var row in rows)
            {
                var activeJson = row.ActiveVersion == 0 ? null : await db.SupportWorkflowVersions.AsNoTracking()
                    .Where(x => x.WorkflowId == row.Id && x.Version == row.ActiveVersion)
                    .Select(x => x.DefinitionJson).SingleOrDefaultAsync();
                result.Add(new
                {
                    row.Id, row.Name, row.SiteId,
                    siteName = row.SiteId == null ? null : await db.Sites.Where(x => x.Id == row.SiteId).Select(x => x.Name).SingleOrDefaultAsync(),
                    row.ScopeKey, row.Version, row.ActiveVersion,
                    draftDefinition = TicketWorkflowPolicy.Deserialize(row.DraftDefinitionJson),
                    activeDefinition = activeJson is null ? null : TicketWorkflowPolicy.Deserialize(activeJson)
                });
            }
            return Results.Ok(result);
        });

        admin.MapPost("", async (TicketWorkflowCreateRequest request, HttpContext c, AppDbContext db) =>
        {
            var definitionValid = TicketWorkflowPolicy.Validate(request.Definition, out var validation);
            if (!ValidName(request.Name) || !definitionValid)
                return Results.BadRequest(new { error = string.IsNullOrEmpty(validation) ? "Nome workflow non valido." : validation });
            if (request.SiteId is not null && !await db.Sites.AnyAsync(x => x.Id == request.SiteId && !x.IsArchived))
                return Results.BadRequest(new { error = "La sede selezionata non è disponibile." });
            var scopeKey = request.SiteId is null ? "global" : "site:" + request.SiteId.Value.ToString("D");
            if (await db.SupportWorkflows.AnyAsync(x => x.ScopeKey == scopeKey)) return Results.Conflict(new { error = "Esiste già un workflow per questo ambito." });
            var workflow = new SupportWorkflow { SiteId = request.SiteId, ScopeKey = scopeKey, Name = request.Name.Trim(),
                DraftDefinitionJson = TicketWorkflowPolicy.Serialize(request.Definition) };
            db.SupportWorkflows.Add(workflow);
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.workflow.created", Target = workflow.Id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Esiste già un workflow per questo ambito." }); }
            return Results.Created($"/api/admin/support-workflows/{workflow.Id}", new { workflow.Id, workflow.ScopeKey, workflow.Version, workflow.ActiveVersion });
        });

        admin.MapPut("/{id:guid}", async (Guid id, TicketWorkflowDraftRequest request, HttpContext c, AppDbContext db) =>
        {
            var definitionValid = TicketWorkflowPolicy.Validate(request.Definition, out var validation);
            if (request.ExpectedVersion < 1 || !ValidName(request.Name) || !definitionValid)
                return Results.BadRequest(new { error = string.IsNullOrEmpty(validation) ? "Versione, nome o definizione workflow non validi." : validation });
            var workflow = await db.SupportWorkflows.SingleOrDefaultAsync(x => x.Id == id);
            if (workflow is null) return Results.NotFound();
            if (workflow.Version != request.ExpectedVersion) return Results.Conflict(new { error = "La bozza è stata aggiornata. Ricarica il workflow.", currentVersion = workflow.Version });
            workflow.Name = request.Name.Trim();
            workflow.DraftDefinitionJson = TicketWorkflowPolicy.Serialize(request.Definition);
            workflow.Version++;
            workflow.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.workflow.draft.updated", Target = id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "La bozza è stata aggiornata. Ricarica il workflow." }); }
            return Results.Ok(new { workflow.Id, workflow.Name, workflow.Version, workflow.ActiveVersion });
        });

        admin.MapPost("/{id:guid}/publish", async (Guid id, long expectedVersion, HttpContext c, AppDbContext db) =>
        {
            if (expectedVersion < 1) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            var workflow = await db.SupportWorkflows.SingleOrDefaultAsync(x => x.Id == id);
            if (workflow is null) return Results.NotFound();
            if (workflow.Version != expectedVersion) return Results.Conflict(new { error = "La bozza è stata aggiornata. Ricarica il workflow.", currentVersion = workflow.Version });
            var definition = TicketWorkflowPolicy.Deserialize(workflow.DraftDefinitionJson);
            if (definition is null) return Results.Conflict(new { error = "La bozza non è valida; nessuna versione è stata pubblicata." });
            var nextVersion = (await db.SupportWorkflowVersions.Where(x => x.WorkflowId == id).Select(x => (int?)x.Version).MaxAsync() ?? 0) + 1;
            db.SupportWorkflowVersions.Add(new SupportWorkflowVersion { WorkflowId = id, Version = nextVersion,
                Name = workflow.Name, SiteId = workflow.SiteId, DefinitionJson = TicketWorkflowPolicy.Serialize(definition),
                PublishedBy = c.User.UserId(), PublishedAt = DateTimeOffset.UtcNow });
            workflow.ActiveVersion = nextVersion;
            workflow.Version++;
            workflow.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.workflow.published", Target = id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Il workflow è cambiato durante la pubblicazione. Ricarica e riprova." }); }
            await tx.CommitAsync();
            return Results.Ok(new { workflow.Id, activeVersion = nextVersion, workflow.Version });
        });

        admin.MapPost("/{id:guid}/deactivate", async (Guid id, long expectedVersion, HttpContext c, AppDbContext db) =>
        {
            if (expectedVersion < 1) return Results.BadRequest();
            var workflow = await db.SupportWorkflows.SingleOrDefaultAsync(x => x.Id == id);
            if (workflow is null) return Results.NotFound();
            if (workflow.Version != expectedVersion) return Results.Conflict(new { error = "Il workflow è stato aggiornato. Ricarica la pagina.", currentVersion = workflow.Version });
            if (workflow.ActiveVersion == 0) return Results.NoContent();
            workflow.ActiveVersion = 0;
            workflow.Version++;
            workflow.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.workflow.deactivated", Target = id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Il workflow è stato aggiornato. Ricarica la pagina." }); }
            return Results.NoContent();
        });
    }

    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 100;
}
