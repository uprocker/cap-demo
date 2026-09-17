namespace Contracts;

public sealed record InventoryReservationResult(
    Guid OrderId,
    bool Succeeded,
    string? FailureReason,
    DateTime ProcessedAtUtc);