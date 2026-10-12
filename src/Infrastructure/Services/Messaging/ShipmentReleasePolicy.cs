using Microsoft.EntityFrameworkCore;
using ShipmentTracking.Application.Common.Interfaces;
using ShipmentTracking.Infrastructure.Persistence;
namespace ShipmentTracking.Infrastructure.Services.Messaging;

/// <summary>Conservative command gate based on the latest recorded state of each hold.</summary>
public sealed class ShipmentReleasePolicy(ApplicationDbContext db) : IShipmentReleasePolicy
{
    public async Task<bool> HasUnresolvedHoldsAsync(Guid shipmentId,CancellationToken ct=default)
    {
        var records=await db.OperationalRecords.Where(e=>e.ShipmentId==shipmentId && e.Type.StartsWith("hold_")).ToArrayAsync(ct);
        return records.GroupBy(e=>e.ReferenceId).Any(g=>g.OrderByDescending(e=>e.OccurredAt).ThenByDescending(e=>e.ReceivedAt).First().Type=="hold_opened");
    }
}
