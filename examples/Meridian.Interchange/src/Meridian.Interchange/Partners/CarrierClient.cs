using System.Text;

namespace Meridian.Interchange.Partners;

/// <summary>
///     Typed client for the carrier's booking API. Its HttpClient is pooled by IHttpClientFactory
///     and configured with the carrier endpoint in the composition root.
/// </summary>
internal sealed class CarrierClient(HttpClient http) : IPartnerClient
{
    public string Channel => "carrier";

    public async Task SendAsync(PartnerEnvelope envelope, CancellationToken cancellationToken)
    {
        using StringContent content = new(envelope.Payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync("bookings", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}