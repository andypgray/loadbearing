using System.Text;
using Meridian.Interchange.Configuration;
using Meridian.Interchange.Outbox;
using Microsoft.Extensions.Options;

namespace Meridian.Interchange.Partners;

/// <summary>
///     Adapter for Meridian's own legacy manifest gateway, whose SDK exposes only synchronous entry
///     points. Serialization, transport, and the single-flight gate are all async underneath, so the
///     adapter blocks on them here — the one corner the async migration has not yet reached.
/// </summary>
internal sealed class LegacyManifestClient : IPartnerClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http;
    private readonly InterchangeOptions _options;

    public LegacyManifestClient(HttpClient http, IOptions<InterchangeOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string Channel => "legacy";

    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // The legacy manifest SDK is synchronous while everything beneath it is async, so this
        // adapter blocks at three points: serializing the manifest, taking the single-flight gate,
        // and posting it. These are the only blocking calls in the subsystem.
        string manifest = SerializeAsync(message, cancellationToken).GetAwaiter().GetResult();

        using StringContent content = new(manifest, Encoding.UTF8, "application/xml");

        _gate.WaitAsync(cancellationToken).Wait(cancellationToken);
        try
        {
            HttpResponseMessage response = _http.PostAsync("manifests", content, cancellationToken).Result;
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _gate.Release();
        }

        return Task.CompletedTask;
    }

    private Task<string> SerializeAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // The gateway expects each manifest addressed to the configured legacy system.
        var manifest =
            $"""<manifest system="{_options.Partners.LegacyManifest}" id="{message.MessageId}">{message.Payload}</manifest>""";
        return Task.FromResult(manifest);
    }
}