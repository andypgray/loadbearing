namespace Meridian.Web.Models;

public sealed record CreateQuoteRequest(
    string Lane,
    string CustomerName,
    int TeuCount);