using FluentValidation;
using MediatR;
using ShipmentTracking.Application.Common.Interfaces;
using ShipmentTracking.Domain.Enums;
using ShipmentTracking.Domain.Exceptions;

namespace ShipmentTracking.Application.Features.Shipments.Commands.UpdateShipmentStatus;

public sealed record UpdateShipmentStatusCommand(
    Guid ShipmentId,
    ShipmentStatus NewStatus,
    string Notes) : IRequest;

public sealed class UpdateShipmentStatusValidator : AbstractValidator<UpdateShipmentStatusCommand>
{
    public UpdateShipmentStatusValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(500);
    }
}

public sealed class UpdateShipmentStatusHandler(
    IShipmentRepository repository,
    IOutboxWriter outbox,
    ICurrentUserService currentUser,
    IShipmentCache cache,
    IShipmentReleasePolicy releasePolicy)
    : IRequestHandler<UpdateShipmentStatusCommand>
{
    public async Task Handle(UpdateShipmentStatusCommand command, CancellationToken ct)
    {
        var shipment = await repository.GetByIdAsync(command.ShipmentId, ct)
            ?? throw new ShipmentNotFoundException(command.ShipmentId);

        if (command.NewStatus is ShipmentStatus.OutForDelivery or ShipmentStatus.Delivered && await releasePolicy.HasUnresolvedHoldsAsync(shipment.Id, ct))
            throw new DomainException("Active operational holds must be released before delivery.");
        shipment.UpdateStatus(command.NewStatus, command.Notes, currentUser.UserName);
        // The outbox row and shipment status are written by the same DbContext transaction.
        await outbox.EnqueueShipmentStatusChangedAsync(shipment.Id, shipment.TrackingNumber, command.NewStatus.ToString(), ct);
        await repository.SaveChangesAsync(ct);
        await cache.RemoveAsync($"shipments:detail:{currentUser.CustomerId}:{shipment.Id:N}", ct);
        await cache.RemoveAsync($"shipments:list:{currentUser.CustomerId}", ct);

    }
}
