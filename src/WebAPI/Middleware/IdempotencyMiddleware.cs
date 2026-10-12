using Microsoft.EntityFrameworkCore;
using ShipmentTracking.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using ShipmentTracking.WebAPI.Observability;

namespace ShipmentTracking.WebAPI.Middleware;

/// <summary>Replays shipment creation for one authenticated customer and caller only.</summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || context.Request.Path != "/api/shipments" ||
            context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(db.CustomerId) ||
            !context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || string.IsNullOrWhiteSpace(keys))
        { await next(context); return; }
        var key = keys.ToString();
        if (key.Length > 128) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { title = "Idempotency-Key is maximaal 128 tekens." }); return; }
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{db.CustomerId}:{userId}:POST:/api/shipments")));
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.Scope == scope && x.Key == key, context.RequestAborted);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) { context.Response.StatusCode = 409; Telemetry.IdempotencyConflicts.Add(1); await context.Response.WriteAsJsonAsync(new { title = "Idempotency-Key werd al voor een ander request gebruikt." }); return; }
            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = "application/json";
            context.Response.Headers.Append("Idempotency-Replayed", "true");
            Telemetry.IdempotencyReplays.Add(1);
            await context.Response.WriteAsync(existing.ResponseBody, context.RequestAborted);
            return;
        }
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(context.RequestAborted) : null;
        try
        {
            await next(context);
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                buffer.Position = 0;
                var response = await new StreamReader(buffer, Encoding.UTF8, leaveOpen: true).ReadToEndAsync(context.RequestAborted);
                db.IdempotencyRecords.Add(IdempotencyRecord.Create(scope, key, hash, context.Response.StatusCode, response));
                await db.SaveChangesAsync(context.RequestAborted);
                if (transaction is not null) await transaction.CommitAsync(context.RequestAborted);
            }
            else if (transaction is not null) await transaction.RollbackAsync(context.RequestAborted);
        }
        catch (DbUpdateException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(context.RequestAborted);
            logger.LogWarning(ex, "Concurrent shipment creation request rejected");
            buffer.SetLength(0);
            context.Response.Clear(); context.Response.StatusCode = 409; context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { title = "Hetzelfde request wordt al verwerkt; probeer opnieuw met dezelfde sleutel." });
        }
        finally
        {
            buffer.Position = 0;
            context.Response.Body = original;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
    }
}
