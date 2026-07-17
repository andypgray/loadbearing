using Meridian.Quoting.Domain;

namespace Meridian.Quoting.Infrastructure.Persistence;

/// <summary>Implements the quote persistence port over <see cref="InMemoryDatabase" />.</summary>
public sealed class InMemoryQuoteRepository(InMemoryDatabase database) : IQuoteRepository
{
    public Task<long> NextNumber()
    {
        return Task.FromResult(database.ReserveNextNumber());
    }

    public Task Add(Quote quote)
    {
        database.AddQuote(quote);
        return Task.CompletedTask;
    }

    public Task<Quote?> Get(string reference)
    {
        return Task.FromResult(database.FindQuote(reference));
    }
}