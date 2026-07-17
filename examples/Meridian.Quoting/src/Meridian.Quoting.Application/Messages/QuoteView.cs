namespace Meridian.Quoting.Application.Messages;

/// <summary>
///     The read model returned for a quote: the domain <c>Money</c> price is flattened to an
///     amount and currency for the boundary.
/// </summary>
public sealed record QuoteView(
    string Reference,
    long Number,
    string Lane,
    string CustomerName,
    int TeuCount,
    decimal Amount,
    string Currency,
    DateTime IssuedUtc,
    DateTime ExpiresUtc);