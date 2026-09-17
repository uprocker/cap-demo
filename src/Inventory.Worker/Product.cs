namespace Inventory.Worker;

public sealed class Product
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int AvailableQuantity { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}