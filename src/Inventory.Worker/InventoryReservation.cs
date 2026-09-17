namespace Inventory.Worker;

public sealed class InventoryReservation
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public InventoryReservationStatus Status { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum InventoryReservationStatus
{
    Reserved,
    Rejected
}