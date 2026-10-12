using MediatR;
using ShipmentTracking.Application.Common.Interfaces;
using ShipmentTracking.Domain.Enums;
using ShipmentTracking.Domain.Exceptions;
using System.Text.Json;

namespace ShipmentTracking.Application.Features.Shipments.Queries.GetShipment;

// --- Query ---
public sealed record GetShipmentQuery(Guid ShipmentId) : IRequest<ShipmentDetailDto>;

// --- DTOs ---
public sealed record ShipmentDetailDto(
    Guid Id,
    string TrackingNumber,
    string SenderName,
    string SenderAddress,
    string RecipientName,
    string RecipientAddress,
    string Description,
    decimal WeightKg,
    ShipmentStatus Status,
    string StatusLabel,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedBy,
    IReadOnlyList<StatusHistoryDto> StatusHistory,
    IReadOnlyList<DocumentDto> Documents);

public sealed record StatusHistoryDto(
    ShipmentStatus Status,
    string StatusLabel,
    string Notes,
    string ChangedBy,
    DateTime ChangedAt);

public sealed record DocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedBy,
    DateTime UploadedAt);

// --- Handler ---
public sealed class GetShipmentHandler(IShipmentRepository repository, IShipmentCache cache, ICurrentUserService currentUser)
    : IRequestHandler<GetShipmentQuery, ShipmentDetailDto>
{
    public async Task<ShipmentDetailDto> Handle(GetShipmentQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.CustomerId)) throw new ShipmentNotFoundException(query.ShipmentId);
        var cacheKey = $"shipments:detail:{currentUser.CustomerId}:{query.ShipmentId:N}";
        var cached = await cache.GetAsync(cacheKey, ct);
        if (cached is not null)
        {
            var dto = JsonSerializer.Deserialize<ShipmentDetailDto>(cached);
            if (dto is not null) return dto;
        }

        var shipment = await repository.GetByIdWithDetailsAsync(query.ShipmentId, ct)
            ?? throw new ShipmentNotFoundException(query.ShipmentId);

        var result = new ShipmentDetailDto(
            shipment.Id,
            shipment.TrackingNumber,
            shipment.SenderName,
            shipment.SenderAddress,
            shipment.RecipientName,
            shipment.RecipientAddress,
            shipment.Description,
            shipment.WeightKg,
            shipment.Status,
            shipment.Status.ToString(),
            shipment.CreatedAt,
            shipment.UpdatedAt,
            shipment.CreatedBy,
            shipment.StatusHistory
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new StatusHistoryDto(
                    h.Status, h.Status.ToString(), h.Notes, h.ChangedBy, h.ChangedAt))
                .ToList(),
            shipment.Documents
                .Select(d => new DocumentDto(
                    d.Id, d.FileName, d.ContentType, d.FileSizeBytes, d.UploadedBy, d.UploadedAt))
                .ToList());

        await cache.SetAsync(cacheKey, JsonSerializer.Serialize(result), TimeSpan.FromMinutes(5), ct);
        return result;
    }
}
