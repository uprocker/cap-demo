using Contracts;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Inventory.Worker;

public sealed class OrderSubmittedHandler : ICapSubscribe
{
    private readonly InventoryDbContext db;
    private readonly ILogger<OrderSubmittedHandler> logger;
    private readonly ICapPublisher publisher;
    private readonly IConfiguration configuration;
    private static readonly ConcurrentDictionary<Guid, int> FailureAttempts = new();

    public OrderSubmittedHandler(
        InventoryDbContext db,
        ILogger<OrderSubmittedHandler> logger,
        ICapPublisher publisher,
        IConfiguration configuration)
    {
        this.db = db;
        this.logger = logger;
        this.publisher = publisher;
        this.configuration = configuration;
    }

    [CapSubscribe("order.submitted", Group = "inventory-worker")]
    public async Task HandleAsync(OrderSubmitted message)
    {
        SimulateFailureIfConfigured(message);

        // The database write and CAP publication commit together.
        await using var transaction =
            await db.Database.BeginTransactionAsync(publisher);

        if (await ReservationAlreadyExistsAsync(message.OrderId))
        {
            logger.LogInformation(
                "Ignoring duplicate order {OrderId}; reservation already exists.",
                message.OrderId);
            return;
        }

        var reservation = await CreateReservationAsync(message);

        db.InventoryReservations.Add(reservation);
        await db.SaveChangesAsync();

        await PublishReservationResultAsync(message.OrderId, reservation);

        await transaction.CommitAsync();

        logger.LogInformation(
            "Inventory reservation for order {OrderId} completed with status {Status}.",
            message.OrderId,
            reservation.Status);
    }

    private void SimulateFailureIfConfigured(OrderSubmitted message)
    {
        var simulationEnabled = configuration.GetValue<bool>(
            "CapDemo:FailureSimulation:Enabled");
        var simulatedProductId = configuration.GetValue<int?>(
            "CapDemo:FailureSimulation:ProductId");
        var failFirstAttempts = configuration.GetValue<int>(
            "CapDemo:FailureSimulation:FailFirstAttempts");

        if (!simulationEnabled ||
            simulatedProductId != message.ProductId ||
            failFirstAttempts <= 0)
        {
            return;
        }

        var attempt = FailureAttempts.AddOrUpdate(
            message.OrderId,
            1,
            (_, currentAttempt) => currentAttempt + 1);

        if (attempt <= failFirstAttempts)
        {
            logger.LogWarning(
                "Simulating failure for order {OrderId}, attempt {Attempt} of {FailFirstAttempts}.",
                message.OrderId,
                attempt,
                failFirstAttempts);

            throw new InvalidOperationException(
                "Configured CAP failure simulation.");
        }

        FailureAttempts.TryRemove(message.OrderId, out _);
    }

    private async Task<bool> ReservationAlreadyExistsAsync(Guid orderId)
    {
        return await db.InventoryReservations
            .AnyAsync(x => x.OrderId == orderId);
    }

    private async Task<InventoryReservation> CreateReservationAsync(
        OrderSubmitted message)
    {
        var product = await db.Products
            .SingleOrDefaultAsync(x => x.Id == message.ProductId);

        var canReserve = product is not null &&
            product.AvailableQuantity >= message.Quantity;

        if (canReserve)
        {
            product!.AvailableQuantity -= message.Quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;
        }

        return new InventoryReservation
        {
            Id = Guid.NewGuid(),
            OrderId = message.OrderId,
            ProductId = message.ProductId,
            Quantity = message.Quantity,
            Status = canReserve
                ? InventoryReservationStatus.Reserved
                : InventoryReservationStatus.Rejected,
            FailureReason = product is null
                ? "Product was not found."
                : canReserve
                    ? null
                    : "Insufficient inventory.",
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private Task PublishReservationResultAsync(
        Guid orderId,
        InventoryReservation reservation)
    {
        return publisher.PublishAsync(
            "inventory.reservation.completed",
            new InventoryReservationResult(
                OrderId: orderId,
                Succeeded: reservation.Status == InventoryReservationStatus.Reserved,
                FailureReason: reservation.FailureReason,
                ProcessedAtUtc: DateTime.UtcNow));
    }
}