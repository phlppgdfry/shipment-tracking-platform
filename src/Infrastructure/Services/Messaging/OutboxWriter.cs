using ShipmentTracking.Application.Common.Interfaces;
using ShipmentTracking.Infrastructure.Persistence;
using System.Text.Json;

namespace ShipmentTracking.Infrastructure.Services.Messaging;

public sealed class OutboxWriter(ApplicationDbContext db) : IOutboxWriter
{
    public Task EnqueueShipmentStatusChangedAsync(Guid shipmentId, string trackingNumber, string newStatus, CancellationToken ct = default)
    {
        db.OutboxMessages.Add(OutboxMessage.Create("ShipmentStatusChanged", JsonSerializer.Serialize(new
        {
            shipmentId,
            customerId = db.CustomerId,
            trackingNumber,
            newStatus,
            occurredAt = DateTime.UtcNow
        })));
        return Task.CompletedTask;
    }
}
