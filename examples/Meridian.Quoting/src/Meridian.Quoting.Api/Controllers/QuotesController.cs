using Meridian.Quoting.Api.Contracts;
using Meridian.Quoting.Application.Abstractions;
using Meridian.Quoting.Application.Messages;
using Meridian.Quoting.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Quoting.Api.Controllers;

/// <summary>
///     The quoting endpoints. Writes go through the <see cref="ICommandBus" />; reads go straight
///     to the query handler — both dispatch styles in one controller.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class QuotesController(
    ICommandBus commandBus,
    IQueryHandler<GetQuoteQuery, QuoteView?> quotes) : ControllerBase
{
    /// <summary>Requests a quote, then returns the priced result read back through the query handler.</summary>
    [HttpPost]
    public async Task<IActionResult> RequestQuote([FromBody] RequestQuoteRequest request)
    {
        var reference = $"Q-{Guid.NewGuid():N}";
        var command = new RequestQuoteCommand(reference, request.Lane, request.CustomerName, request.TeuCount);

        try
        {
            await commandBus.SendAsync(command);
        }
        catch (RateCardNotFoundException ex)
        {
            return UnprocessableEntity(ex.Message);
        }

        QuoteView? view = await quotes.HandleAsync(new GetQuoteQuery(reference));
        return CreatedAtAction(nameof(GetQuote), new { reference }, view);
    }

    /// <summary>Returns the quote with the given reference, or 404 if there is none.</summary>
    [HttpGet("{reference}")]
    public async Task<IActionResult> GetQuote(string reference)
    {
        QuoteView? view = await quotes.HandleAsync(new GetQuoteQuery(reference));
        return view is null ? NotFound() : Ok(view);
    }
}