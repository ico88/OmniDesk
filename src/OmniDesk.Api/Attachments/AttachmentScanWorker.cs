using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Attachments;

public sealed class AttachmentScanWorker(IServiceScopeFactory scopes, AttachmentStorage storage,
    IAttachmentScanner scanner, ILogger<AttachmentScanWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await ProcessNext(stoppingToken)) continue;
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    logger.LogWarning("Attachment scan worker failed: {Type}", e.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<bool> ProcessNext(CancellationToken ct)
    {
        SupportAttachment? attachment;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var candidates = await db.SupportAttachments.FromSqlInterpolated($"""
                SELECT a.* FROM "SupportAttachments" a
                WHERE (a."ScanState" = 'quarantined' AND a."NextScanAt" <= {now})
                   OR (a."ScanState" = 'scanning' AND a."ScanLeaseUntil" <= {now})
                ORDER BY a."NextScanAt", a."CreatedAt", a."Id"
                FOR UPDATE OF a SKIP LOCKED
                LIMIT 1
                """).ToListAsync(ct);
            attachment = candidates.SingleOrDefault();
            if (attachment is null) { await tx.CommitAsync(ct); return false; }
            attachment.ScanState = "scanning";
            attachment.ScanAttempts++;
            attachment.ScanLeaseUntil = now.AddMinutes(2);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        AttachmentScanVerdict? verdict = null;
        Exception? scanError = null;
        try
        {
            await using var file = storage.OpenRead(attachment.StorageKey);
            verdict = await scanner.ScanAsync(file, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { scanError = e; }

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = await db.SupportAttachments.SingleOrDefaultAsync(x => x.Id == attachment.Id && x.ScanState == "scanning", ct);
            if (saved is null) return true;
            var now = DateTimeOffset.UtcNow;
            saved.ScanLeaseUntil = null;
            if (verdict is not null)
            {
                saved.ScanState = verdict == AttachmentScanVerdict.Clean ? "clean" : "infected";
                saved.ScannedAt = now;
                saved.LastScanErrorType = null;
                db.AuditEvents.Add(new AuditEvent { Action = verdict == AttachmentScanVerdict.Clean ? "ticket.attachment.scan.clean" : "ticket.attachment.scan.infected", Target = saved.Id.ToString() });
            }
            else
            {
                saved.LastScanErrorType = scanError?.GetType().Name ?? "UnknownScannerError";
                if (saved.ScanAttempts >= MaxAttempts)
                {
                    saved.ScanState = "scan_failed";
                    db.AuditEvents.Add(new AuditEvent { Action = "ticket.attachment.scan.failed", Target = saved.Id.ToString(), Outcome = "failure" });
                }
                else
                {
                    saved.ScanState = "quarantined";
                    var delayMinutes = Math.Min(60, 1 << Math.Min(saved.ScanAttempts - 1, 5));
                    saved.NextScanAt = now.AddMinutes(delayMinutes);
                }
                logger.LogWarning("Attachment scan deferred for {AttachmentId}: {Type}", saved.Id, saved.LastScanErrorType);
            }
            await db.SaveChangesAsync(ct);
        }
        return true;
    }
}
