using Meridian.Quoting.Application.Abstractions;
using Meridian.Quoting.Application.Messages;
using Meridian.Quoting.Domain;

namespace Meridian.Quoting.Application.Handlers;

/// <summary>
///     Prices and persists a requested quote. It writes twice — reserving a quote number and
///     storing the quote — so the <see cref="TransactionalAttribute" /> is load-bearing: run
///     without a unit of work, a failure between the two writes would burn a quote number with
///     no quote to show for it.
/// </summary>
[Transactional]
public sealed class RequestQuoteHandler(
    IRateCardRepository rateCards,
    IQuoteRepository quotes,
    IClock clock) : ICommandHandler<RequestQuoteCommand>
{
    public async Task HandleAsync(RequestQuoteCommand command)
    {
        DateTime issuedUtc = clock.UtcNow;

        RateCard? rateCard = await rateCards.GetForLane(command.Lane, issuedUtc);
        if (rateCard is null) throw new RateCardNotFoundException(command.Lane, issuedUtc);

        long number = await quotes.NextNumber();
        Money price = rateCard.RatePerTeu * command.TeuCount;

        var quote = new Quote
        {
            Reference = command.Reference,
            Number = number,
            Lane = command.Lane,
            CustomerName = command.CustomerName,
            TeuCount = command.TeuCount,
            Price = price,
            IssuedUtc = issuedUtc,
            ExpiresUtc = issuedUtc.AddDays(14)
        };

        await quotes.Add(quote);
    }
}