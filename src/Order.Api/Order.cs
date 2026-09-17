namespace Order.Api;

public sealed record CreateOrderRequest(
    int ProductId,
    int Quantity);

public sealed class Order
{
    public Guid Id { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Rejected
}