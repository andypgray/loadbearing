namespace Meridian.Quoting.Domain;

/// <summary>
///     Thrown when no rate card is effective for a lane at the moment a quote is requested.
/// </summary>
public sealed class RateCardNotFoundException(string lane, DateTime asOfUtc)
    : Exception($"No rate card is effective for lane {lane} at {asOfUtc:u}.")
{
    /// <summary>The lane that had no effective rate card.</summary>
    public string Lane { get; } = lane;

    /// <summary>The UTC instant the lookup was made for.</summary>
    public DateTime AsOfUtc { get; } = asOfUtc;
}