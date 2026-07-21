using Meridian.Interchange.Configuration;
using Meridian.Interchange.Host;
using Microsoft.Extensions.Options;

namespace Meridian.Interchange.Dispatch;

/// <summary>
///     The outbound dispatcher: a hosted BackgroundService that wakes on a fixed interval and drains
///     the outbox each time. It is a singleton, so it captures no scoped services itself — it
///     delegates each poll to the scoped runner and reads only its singleton-safe options.
/// </summary>
internal sealed class OutboxDispatcher(ScopedDispatchRunner runner, IOptions<InterchangeOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan pollInterval = options.Value.PollInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            await runner.RunPendingAsync(stoppingToken);

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // The host is shutting down; the loop condition ends the drain on the next check.
            }
        }
    }
}