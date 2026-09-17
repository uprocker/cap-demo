namespace Contracts;

public sealed record OrderSubmitted(
    Guid OrderId,
    int ProductId,
    int Quantity,
    DateTime SubmittedAtUtc);