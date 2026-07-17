namespace Meridian.Domain;

public sealed record RateCard
{
    public required string Lane { get; init; }

    public required decimal RatePerTeuUsd { get; init; }

    public required DateTime ValidFromUtc { get; init; }

    public required DateTime ValidUntilUtc { get; init; }
}