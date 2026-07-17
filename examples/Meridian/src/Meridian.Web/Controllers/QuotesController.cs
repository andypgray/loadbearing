using Meridian.Domain;
using Meridian.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class QuotesController(
    IQuoteRepository quotes,
    IRateCardRepository rateCards,
    IClock clock) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQuoteRequest request)
    {
        RateCard? rateCard = await rateCards.GetForLane(request.Lane, clock.UtcNow);
        if (rateCard is null) return NotFound($"No active rate card for lane {request.Lane}.");

        decimal amountUsd = rateCard.RatePerTeuUsd * request.TeuCount;
        var reference = $"Q-{Guid.NewGuid():N}";
        DateTime expiresUtc = clock.UtcNow.AddHours(24);

        var quote = new Quote
        {
            Reference = reference,
            Lane = request.Lane,
            CustomerName = request.CustomerName,
            TeuCount = request.TeuCount,
            AmountUsd = amountUsd,
            ExpiresUtc = expiresUtc
        };
        await quotes.Add(quote);

        return Ok(new QuoteResponse(reference, request.Lane, amountUsd, expiresUtc));
    }

    [HttpGet("{reference}")]
    public async Task<IActionResult> Get(string reference)
    {
        Quote? quote = await quotes.Get(reference);
        if (quote is null) return NotFound();

        return Ok(new QuoteResponse(quote.Reference, quote.Lane, quote.AmountUsd, quote.ExpiresUtc));
    }
}