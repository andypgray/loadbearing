namespace Meridian.Interchange.Configuration;

/// <summary>The base URL Meridian transmits each interchange channel to.</summary>
public sealed class PartnerEndpoints
{
    /// <summary>Carrier booking API.</summary>
    public string Carrier { get; set; } = "https://carrier.partners.example/";

    /// <summary>Customs filing system.</summary>
    public string Customs { get; set; } = "https://customs.gov.example/";

    /// <summary>Terminal status gateway.</summary>
    public string Terminal { get; set; } = "https://terminal.partners.example/";

    /// <summary>Meridian's own legacy manifest gateway.</summary>
    public string LegacyManifest { get; set; } = "https://manifest.internal.example/";
}