using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortOps.Domain;
using ShipmentTracking.Infrastructure.Persistence;

namespace ShipmentTracking.WebAPI.Controllers;

[ApiController]
[Authorize]
public sealed class OperationsController(ApplicationDbContext db) : ControllerBase
{
    private static readonly HashSet<string> Types = ["arrival_plan", "gate_in", "gate_out", "hold_opened", "hold_released"];
    [HttpGet("api/operations")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var shipments = await db.Shipments.AsNoTracking().OrderBy(s => s.CreatedAt).Take(100).ToArrayAsync(ct);
        var ids = shipments.Select(s => s.Id).ToArray();
        var events = await db.OperationalRecords.AsNoTracking().Where(e => ids.Contains(e.ShipmentId)).ToArrayAsync(ct);
        return Ok(new { synthetic = true, scenarioClock = "2026-10-12T12:00:00Z", limit = 100,
            shipments = shipments.Select(s => new { s.Id, s.TrackingNumber, s.Description, status = (int)s.Status,
                statusLabel = s.Status.ToString(), events = events.Where(e => e.ShipmentId == s.Id).OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId).Select(Envelope) }) });
    }
    [HttpGet("api/shipments/{shipmentId:guid}/events")]
    public async Task<IActionResult> Events(Guid shipmentId, CancellationToken ct)
    {
        if (!await db.Shipments.AnyAsync(s => s.Id == shipmentId, ct)) return NotFound();
        var events=await db.OperationalRecords.Where(e=>e.ShipmentId==shipmentId).OrderBy(e=>e.OccurredAt).ToArrayAsync(ct);
        return Ok(events.Select(Envelope));
    }
    [HttpPost("api/shipments/{shipmentId:guid}/events")]
    [Authorize(Roles = "Dispatcher,Administrator")]
    public async Task<IActionResult> Record(Guid shipmentId, RecordEventRequest request, CancellationToken ct)
    {
        if (!await db.Shipments.AnyAsync(s => s.Id == shipmentId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.EventId) || request.EventId.Length > 80 ||
            string.IsNullOrWhiteSpace(request.ReferenceId) || request.ReferenceId.Length > 80 ||
            request.Type is null || !Types.Contains(request.Type) || string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 80 ||
            string.IsNullOrWhiteSpace(request.Detail) || request.Detail.Length > 500 || request.OccurredAt == default || request.ObservedAt == default)
            return BadRequest(new {title="Use a supported type, explicit timestamps, and bounded non-empty event/reference/source/detail values."});
        var existing=await db.OperationalRecords.SingleOrDefaultAsync(e=>e.ShipmentId==shipmentId && e.EventId==request.EventId,ct);
        if(existing is not null)
        {
            if(existing.ReferenceId!=request.ReferenceId || existing.Type!=request.Type || existing.OccurredAt!=request.OccurredAt || existing.ObservedAt!=request.ObservedAt || existing.Source!=request.Source || existing.Detail!=request.Detail)
                return Conflict(new {title="This event ID already identifies different evidence."});
            Response.Headers["Event-Replayed"]="true"; return Ok(Envelope(existing));
        }
        var prior=await db.OperationalRecords.Where(e=>e.ShipmentId==shipmentId).ToArrayAsync(ct);
        if(request.Type is "gate_in" or "gate_out" && prior.Any(e=>e.Type==request.Type))
            return Conflict(new {title="This preview supports one terminal visit per shipment; an existing gate event cannot be replaced."});
        if(request.Type=="gate_out")
        {
            var arrival=prior.SingleOrDefault(e=>e.Type=="gate_in");
            if(arrival is null || request.OccurredAt < arrival.OccurredAt) return BadRequest(new {title="Departure requires a recorded earlier gate arrival."});
            if(prior.Where(e=>e.OccurredAt<=request.OccurredAt && e.Type.StartsWith("hold_")).GroupBy(e=>e.ReferenceId)
                .Any(g=>g.OrderByDescending(e=>e.OccurredAt).ThenByDescending(e=>e.ReceivedAt).First().Type=="hold_opened"))
                return Conflict(new {title="Release active holds before recording departure."});
        }
        if(request.Type=="hold_released" && !prior.Any(e=>e.Type=="hold_opened" && e.ReferenceId==request.ReferenceId && e.OccurredAt<=request.OccurredAt))
            return BadRequest(new {title="A hold release requires a matching earlier hold."});
        var record=new OperationalRecord{ShipmentId=shipmentId,CustomerId=db.CustomerId,EventId=request.EventId,ReferenceId=request.ReferenceId,
            Type=request.Type,OccurredAt=request.OccurredAt,ObservedAt=request.ObservedAt,Source=request.Source,Detail=request.Detail};
        db.OperationalRecords.Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateException) { return Conflict(new {title="This event is being recorded concurrently; retry with the same event ID."}); }
        return Created($"/api/shipments/{shipmentId}/events",Envelope(record));
    }
    private static ShipmentEventEnvelope Envelope(OperationalRecord e)=>new(e.ShipmentId.ToString(),e.CustomerId,e.EventId,
        new(e.Type,e.OccurredAt,new(e.EventId,e.Source,e.ObservedAt,e.Detail)), CorrelationId:e.ReferenceId);
}
public sealed record RecordEventRequest(string EventId,string ReferenceId,string Type,DateTimeOffset OccurredAt,
    DateTimeOffset ObservedAt,string Source,string Detail);
