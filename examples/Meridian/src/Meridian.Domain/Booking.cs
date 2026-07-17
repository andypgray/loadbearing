namespace Meridian.Domain;

public sealed record Booking
{
    public required string Reference { get; init; }

    public required string CustomerName { get; init; }

    public required string Lane { get; init; }

    public required IReadOnlyList<string> ContainerNumbers { get; init; }

    public required DateTime CutoffUtc { get; init; }
}