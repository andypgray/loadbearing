namespace Meridian.Interchange.Processing;

/// <summary>Drains the outbox once: sends each pending message to its partner and marks the delivered ones sent.</summary>
public interface IOutboxProcessor
{
    /// <summary>Transmits every pending outbox message to the partner channel that owns it.</summary>
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}