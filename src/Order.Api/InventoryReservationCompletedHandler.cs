using Contracts;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;

namespace Order.Api;

public sealed class InventoryReservationCompletedHandler : ICapSubscribe
{
    private readonly OrderDbContext db;
    private readonly ILogger<InventoryReservationCompletedHandler> logger;

    public InventoryReservationCompletedHandler(
        OrderDbContext db,
        ILogger<InventoryReservationCompletedHandler> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    [CapSubscribe("inventory.reservation.completed", Group = "order-api")]
    public async Task HandleAsync(InventoryReservationResult message)
    {
        var order = await db.Orders
            .SingleOrDefaultAsync(x => x.Id == message.OrderId);

        if (order is null)
        {
            logger.LogWarning(
                "Inventory result received for unknown order {OrderId}.",
                message.OrderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            logger.LogInformation(
                "Ignoring duplicate inventory result for order {OrderId}.",
                message.OrderId);
            return;
        }

        order.Status = message.Succeeded
            ? OrderStatus.Confirmed
            : OrderStatus.Rejected;
        order.FailureReason = message.FailureReason;
        order.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Order {OrderId} updated to status {Status}.",
            message.OrderId,
            order.Status);
    }
}
