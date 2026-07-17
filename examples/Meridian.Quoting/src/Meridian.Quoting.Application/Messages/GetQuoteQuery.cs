using Meridian.Quoting.Application.Abstractions;

namespace Meridian.Quoting.Application.Messages;

/// <summary>Reads back the quote with the given reference as a <see cref="QuoteView" />.</summary>
public sealed record GetQuoteQuery(string Reference) : IQuery<QuoteView?>;