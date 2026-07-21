using Meridian.Interchange.Processing;

namespace Meridian.Interchange.Host;

/// <summary>
///     Bridges the singleton dispatcher to the scoped outbox pipeline: for each poll it opens a DI
///     scope, resolves the scoped processor inside it, and runs one drain. Living in the composition
///     root, it is the only type outside registration that resolves a service from the provider.
/// </summary>
internal sealed class ScopedDispatchRunner(IServiceScopeFactory scopeFactory)
{
    /// <summary>Opens a scope, resolves the scoped outbox processor, and drains the outbox once.</summary>
    public async Task RunPendingAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        await processor.ProcessPendingAsync(cancellationToken);
    }
}