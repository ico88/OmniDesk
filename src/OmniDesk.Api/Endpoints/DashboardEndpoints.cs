using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
namespace OmniDesk.Api.Endpoints;
public sealed record LayoutRequest(string[] Widgets);
public static class DashboardEndpoints
{
    private static readonly string[] Allowed = ["devices", "permissions", "authentication"];
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var dashboard = app.MapGroup("/api/dashboard").RequireAuthorization();
        dashboard.MapGet("/layout", async (HttpContext c, AppDbContext db) => {
            var value = await db.DashboardLayouts.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == c.User.UserId());
            return Results.Ok(new { widgets = value is null ? Allowed : JsonSerializer.Deserialize<string[]>(value.Widgets) });
        });
        dashboard.MapPut("/layout", async (LayoutRequest r, HttpContext c, AppDbContext db) => {
            if (r.Widgets is null || r.Widgets.Length > 3 || r.Widgets.Distinct().Count() != r.Widgets.Length || r.Widgets.Any(x => !Allowed.Contains(x))) return Results.BadRequest();
            var value = await db.DashboardLayouts.FindAsync(c.User.UserId());
            if (value is null) db.DashboardLayouts.Add(new DashboardLayout { UserId = c.User.UserId(), Widgets = JsonSerializer.Serialize(r.Widgets) });
            else value.Widgets = JsonSerializer.Serialize(r.Widgets);
            await db.SaveChangesAsync(); return Results.NoContent();
        });
    }
}
