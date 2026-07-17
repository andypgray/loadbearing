namespace Meridian.Quoting.Domain;

/// <summary>
///     A priced quote for moving a number of TEU on a lane. Every instant is supplied by the
///     issuing handler from the injected clock; the record never reads the wall clock itself.
/// </summary>
public sealed record Quote
{
    /// <summary>The client-facing reference the quote is retrieved by.</summary>
    public required string Reference { get; init; }

    /// <summary>The sequential quote number reserved from the store when the quote was issued.</summary>
    public required long Number { get; init; }

    /// <summary>The lane quoted, as origin/destination UN/LOCODEs (for example, "CNSHA-&gt;USLGB").</summary>
    public required string Lane { get; init; }

    /// <summary>The customer the quote was issued to.</summary>
    public required string CustomerName { get; init; }

    /// <summary>The number of twenty-foot equivalent units quoted.</summary>
    public required int TeuCount { get; init; }

    /// <summary>The total price: the lane's per-TEU rate scaled by <see cref="TeuCount" />.</summary>
    public required Money Price { get; init; }

    /// <summary>When the quote was issued (UTC).</summary>
    public required DateTime IssuedUtc { get; init; }

    /// <summary>When the quote expires (UTC).</summary>
    public required DateTime ExpiresUtc { get; init; }
}