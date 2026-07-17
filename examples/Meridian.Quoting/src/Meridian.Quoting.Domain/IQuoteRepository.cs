namespace Meridian.Quoting.Domain;

/// <summary>
///     Persistence port for quotes. The Domain owns the port; the Infrastructure layer implements it.
/// </summary>
public interface IQuoteRepository
{
    /// <summary>Reserves and returns the next sequential quote number, advancing the store's sequence.</summary>
    Task<long> NextNumber();

    /// <summary>Persists a quote.</summary>
    Task Add(Quote quote);

    /// <summary>Returns the quote with the given reference, or null if there is none.</summary>
    Task<Quote?> Get(string reference);
}