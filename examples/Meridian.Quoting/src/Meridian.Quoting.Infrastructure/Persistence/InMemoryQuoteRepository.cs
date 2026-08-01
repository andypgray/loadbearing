using Meridian.Quoting.Domain;

namespace Meridian.Quoting.Infrastructure.Persistence;

/// <summary>Implements the quote persistence port over <see cref="InMemoryDatabase" />.</summary>
public sealed class InMemoryQuoteRepository(InMemoryDatabase database) : IQuoteRepository
{
    /// <inheritdoc />
    public Task<long> NextNumber()
    {
        return Task.FromResult(database.ReserveNextNumber());
    }

    /// <inheritdoc />
    public Task Add(Quote quote)
    {
        database.AddQuote(quote);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Quote?> Get(string reference)
    {
        return Task.FromResult(database.FindQuote(reference));
    }
}