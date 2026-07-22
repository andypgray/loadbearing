using System.Text;

namespace Meridian.Interchange.Partners;

/// <summary>
///     Typed client for the customs filing system. Its HttpClient is pooled by IHttpClientFactory
///     and configured with the customs endpoint in the composition root.
/// </summary>
internal sealed class CustomsFilingClient(HttpClient http) : IPartnerClient
{
    public string Channel => "customs";

    public async Task SendAsync(PartnerEnvelope envelope, CancellationToken cancellationToken)
    {
        using StringContent content = new(envelope.Payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync("filings", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}