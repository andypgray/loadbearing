namespace Meridian.Domain;

public sealed record Quote
{
    public required string Reference { get; init; }

    public required string Lane { get; init; }

    public required string CustomerName { get; init; }

    public required int TeuCount { get; init; }

    public required decimal AmountUsd { get; init; }

    public required DateTime ExpiresUtc { get; init; }
}