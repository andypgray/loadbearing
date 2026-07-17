namespace Meridian.Quoting.Domain;

/// <summary>
///     A published rate for a lane, valid across a UTC effective window. Quoting reads the
///     card effective at the moment a quote is issued and prices per TEU from it.
/// </summary>
public sealed record RateCard
{
    /// <summary>The lane the rate applies to, as origin/destination UN/LOCODEs (for example, "CNSHA-&gt;USLGB").</summary>
    public required string Lane { get; init; }

    /// <summary>The price for one TEU on this lane.</summary>
    public required Money RatePerTeu { get; init; }

    /// <summary>Inclusive start of the window this card is effective from (UTC).</summary>
    public required DateTime EffectiveFromUtc { get; init; }

    /// <summary>Exclusive end of the window this card is effective until (UTC).</summary>
    public required DateTime EffectiveToUtc { get; init; }
}