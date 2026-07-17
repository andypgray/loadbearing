using Meridian.Quoting.Domain;

namespace Meridian.Quoting.Infrastructure.Persistence;

/// <summary>
///     The subsystem's in-memory store: quotes keyed by reference, a quote-number sequence, and
///     seeded rate cards. It exposes capture/restore so the unit of work can roll a failed batch
///     of writes back to the state it started from.
/// </summary>
public sealed class InMemoryDatabase
{
    private readonly Dictionary<string, Quote> quotes = new(StringComparer.Ordinal);
    private readonly List<RateCard> rateCards;
    private long lastQuoteNumber;

    public InMemoryDatabase()
    {
        // Seeded with fixed UTC literals, never a clock read: the store must stand up the same
        // way every time it is constructed, independent of the wall clock.
        rateCards =
        [
            new RateCard
            {
                Lane = "CNSHA->USLGB",
                RatePerTeu = new Money(2400m, "USD"),
                EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EffectiveToUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new RateCard
            {
                Lane = "NLRTM->USNYC",
                RatePerTeu = new Money(1850m, "USD"),
                EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EffectiveToUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new RateCard
            {
                Lane = "SGSIN->AUSYD",
                RatePerTeu = new Money(1200m, "USD"),
                EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EffectiveToUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        ];
    }

    /// <summary>Advances the quote-number sequence and returns the reserved value.</summary>
    public long ReserveNextNumber()
    {
        return ++lastQuoteNumber;
    }

    /// <summary>Stores a quote, keyed by its reference.</summary>
    public void AddQuote(Quote quote)
    {
        quotes[quote.Reference] = quote;
    }

    /// <summary>Returns the stored quote with the given reference, or null if there is none.</summary>
    public Quote? FindQuote(string reference)
    {
        return quotes.GetValueOrDefault(reference);
    }

    /// <summary>Returns the seeded rate card effective for the lane at the given instant, or null if none applies.</summary>
    public RateCard? FindRateCard(string lane, DateTime asOfUtc)
    {
        return rateCards.FirstOrDefault(card =>
            card.Lane == lane && card.EffectiveFromUtc <= asOfUtc && asOfUtc < card.EffectiveToUtc);
    }

    /// <summary>Captures the current state so a later <see cref="Restore" /> can return the store to this point.</summary>
    public Snapshot Capture()
    {
        return new Snapshot(new Dictionary<string, Quote>(quotes, StringComparer.Ordinal), lastQuoteNumber);
    }

    /// <summary>Returns the store to a captured state, discarding every write made since.</summary>
    public void Restore(Snapshot snapshot)
    {
        quotes.Clear();
        foreach (var entry in snapshot.Quotes) quotes[entry.Key] = entry.Value;

        lastQuoteNumber = snapshot.LastQuoteNumber;
    }

    /// <summary>An immutable copy of the store's mutable state, taken for rollback.</summary>
    public sealed record Snapshot(IReadOnlyDictionary<string, Quote> Quotes, long LastQuoteNumber);
}