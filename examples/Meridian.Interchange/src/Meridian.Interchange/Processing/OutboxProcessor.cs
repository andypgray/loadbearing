using Meridian.Interchange.Configuration;
using Meridian.Interchange.Outbox;
using Meridian.Interchange.Partners;
using Microsoft.Extensions.Options;

namespace Meridian.Interchange.Processing;

/// <summary>
///     Drains the outbox for one poll: it reads the pending messages, routes each to the partner
///     client that owns its channel, and marks the delivered ones sent. Being scoped, it reads its
///     delivery settings from an <see cref="IOptionsSnapshot{T}" /> refreshed per scope — the
///     correct options type for a scoped consumer.
/// </summary>
internal sealed class OutboxProcessor(
    IOutboxStore store,
    IEnumerable<IPartnerClient> partners,
    IOptionsSnapshot<InterchangeOptions> options) : IOutboxProcessor
{
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        int maxAttempts = options.Value.MaxDeliveryAttempts;
        var pending = await store.GetPendingAsync(cancellationToken);

        foreach (OutboxMessage message in pending)
        {
            IPartnerClient partner = partners.First(client => client.Channel == message.Channel);
            bool delivered = await TryDeliverAsync(partner, message, maxAttempts, cancellationToken);
            if (delivered) await store.MarkSentAsync(message.MessageId, cancellationToken);
        }
    }

    private static async Task<bool> TryDeliverAsync(
        IPartnerClient partner,
        OutboxMessage message,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
            try
            {
                await partner.SendAsync(new PartnerEnvelope(message.MessageId, message.Payload), cancellationToken);
                return true;
            }
            catch (HttpRequestException)
            {
                // Transient partner failure; retry until the attempt budget is spent, then leave
                // the message pending for the next poll.
            }

        return false;
    }
}