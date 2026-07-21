namespace Meridian.Interchange.Configuration;

/// <summary>
///     Binds the <c>Interchange</c> configuration section: how often the dispatcher drains the
///     outbox, how many times a message is retried against its partner, and the endpoint each
///     partner channel transmits to.
/// </summary>
public sealed class InterchangeOptions
{
    /// <summary>How long the dispatcher waits between outbox polls.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a message is retried against its partner before it waits for the next poll.</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>The endpoint each partner channel transmits to.</summary>
    public PartnerEndpoints Partners { get; set; } = new();
}