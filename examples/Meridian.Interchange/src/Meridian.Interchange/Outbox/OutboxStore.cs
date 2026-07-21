namespace Meridian.Interchange.Outbox;

/// <summary>
///     In-memory outbox seeded with a few pending messages so the worker has something to transmit
///     on a cold start. A real deployment would back this with the interchange table; it is scoped,
///     so each poll reads it inside its own unit of work.
/// </summary>
internal sealed class OutboxStore : IOutboxStore
{
    private readonly List<OutboxMessage> _pending =
    [
        new("MSG-6001", "carrier", """{"booking":"BKG-4821","status":"confirmed"}"""),
        new("MSG-6002", "customs", """{"filing":"CUS-2290","status":"lodged"}"""),
        new("MSG-6003", "terminal", """{"container":"MSKU7346095","event":"gate-in"}"""),
        new("MSG-6004", "legacy", """{"manifest":"MAN-5573","voyage":"VOY-118"}""")
    ];

    public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxMessage> snapshot = _pending.ToList();
        return Task.FromResult(snapshot);
    }

    public Task MarkSentAsync(string messageId, CancellationToken cancellationToken)
    {
        _pending.RemoveAll(message => message.MessageId == messageId);
        return Task.CompletedTask;
    }
}