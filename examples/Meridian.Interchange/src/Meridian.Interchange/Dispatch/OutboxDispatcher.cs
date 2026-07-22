using Meridian.Interchange.Configuration;
using Meridian.Interchange.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Interchange.Dispatch;

/// <summary>
///     The outbound dispatcher: a hosted BackgroundService that wakes on a fixed interval and drains
///     the outbox each time. It is a singleton, so it captures no scoped services itself — it
///     delegates each poll to the scoped runner and reads only its singleton-safe options. A failed
///     dispatch cycle is logged and the loop carries on to the next poll, so one bad cycle never
///     stops the host.
/// </summary>
internal sealed class OutboxDispatcher(ScopedDispatchRunner runner, IOptions<InterchangeOptions> options, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan pollInterval = options.Value.PollInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runner.RunPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dispatch cycle failed; carrying on to the next poll.");
            }

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