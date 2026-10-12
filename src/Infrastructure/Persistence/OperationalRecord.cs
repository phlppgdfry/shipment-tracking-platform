namespace ShipmentTracking.Infrastructure.Persistence;

/// <summary>Append-only source event, customer-scoped independently of the external evidence claim.</summary>
public sealed class OperationalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipmentId { get; set; }
    public string CustomerId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Source { get; set; } = "";
    public string Detail { get; set; } = "";
}
