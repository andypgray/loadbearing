using System.Text;

namespace Meridian.Interchange.Partners;

/// <summary>
///     Typed client for the terminal status gateway. Its HttpClient is pooled by IHttpClientFactory
///     and configured with the terminal endpoint in the composition root.
/// </summary>
internal sealed class TerminalClient(HttpClient http) : IPartnerClient
{
    public string Channel => "terminal";

    public async Task SendAsync(PartnerEnvelope envelope, CancellationToken cancellationToken)
    {
        using StringContent content = new(envelope.Payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync("events", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}