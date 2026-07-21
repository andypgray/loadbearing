namespace Meridian.Interchange.Outbox;

/// <summary>
///     One outbound interchange message queued for a trading partner — a booking confirmation, a
///     status update, or a customs filing — tagged with the channel that carries it.
/// </summary>
public sealed record OutboxMessage(string MessageId, string Channel, string Payload);