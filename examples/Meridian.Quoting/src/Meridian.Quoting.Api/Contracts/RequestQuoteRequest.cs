namespace Meridian.Quoting.Api.Contracts;

/// <summary>The POST body a client sends to request a quote. The reference is assigned by the server.</summary>
public sealed record RequestQuoteRequest(string Lane, string CustomerName, int TeuCount);