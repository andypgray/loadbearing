namespace Meridian.Web.Models;

public sealed record QuoteResponse(
    string Reference,
    string Lane,
    decimal AmountUsd,
    DateTime ExpiresUtc);