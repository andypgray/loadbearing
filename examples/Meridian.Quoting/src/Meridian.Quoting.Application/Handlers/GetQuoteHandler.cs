using Meridian.Quoting.Application.Abstractions;
using Meridian.Quoting.Application.Messages;
using Meridian.Quoting.Domain;

namespace Meridian.Quoting.Application.Handlers;

/// <summary>Reads a quote by reference and projects it to a <see cref="QuoteView" />, or null if there is none.</summary>
public sealed class GetQuoteHandler(IQuoteRepository quotes) : IQueryHandler<GetQuoteQuery, QuoteView?>
{
    public async Task<QuoteView?> HandleAsync(GetQuoteQuery query)
    {
        Quote? quote = await quotes.Get(query.Reference);
        if (quote is null) return null;

        return new QuoteView(
            quote.Reference,
            quote.Number,
            quote.Lane,
            quote.CustomerName,
            quote.TeuCount,
            quote.Price.Amount,
            quote.Price.Currency,
            quote.IssuedUtc,
            quote.ExpiresUtc);
    }
}