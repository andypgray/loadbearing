using Meridian.Interchange.Outbox;

namespace Meridian.Interchange.Partners;

/// <summary>
///     A trading-partner endpoint Meridian transmits outbound interchange to — a carrier, a customs
///     system, or a terminal. Each client owns one channel and sends the messages routed to it.
/// </summary>
public interface IPartnerClient
{
    /// <summary>The interchange channel this client transmits to (carrier, customs, terminal, legacy).</summary>
    string Channel { get; }

    /// <summary>Transmits one outbound message to the partner.</summary>
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken);
}