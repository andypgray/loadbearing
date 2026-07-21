namespace Meridian.Interchange.Outbox;

/// <summary>The outbox of interchange messages awaiting transmission, scoped to one unit of work.</summary>
public interface IOutboxStore
{
    /// <summary>Returns the messages still awaiting transmission.</summary>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(CancellationToken cancellationToken);

    /// <summary>Marks a message as transmitted so it is not sent again.</summary>
    Task MarkSentAsync(string messageId, CancellationToken cancellationToken);
}