using ShipmentTracking.Domain.Entities;
using ShipmentTracking.Domain.Enums;

namespace ShipmentTracking.Application.Common.Interfaces;

public interface IShipmentRepository
{
    Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Shipment?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Shipment> Items, int TotalCount)> GetPagedAsync(
        ShipmentStatus? statusFilter,
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task AddAsync(Shipment shipment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetByShipmentIdAsync(Guid shipmentId, CancellationToken ct = default);
    Task AddAsync(Document document, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBlobStorageService
{
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string blobUri, CancellationToken ct = default);
    Task DeleteAsync(string blobUri, CancellationToken ct = default);
}

public interface IOutboxWriter
{
    Task EnqueueShipmentStatusChangedAsync(Guid shipmentId, string trackingNumber, string newStatus, CancellationToken ct = default);
}

/// <summary>Small abstraction so the application layer is not coupled to Redis.</summary>
public interface IShipmentCache
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    string CustomerId { get; }
    string UserId { get; }
    string UserName { get; }
}

public interface IShipmentReleasePolicy
{
    Task<bool> HasUnresolvedHoldsAsync(Guid shipmentId, CancellationToken ct = default);
}
