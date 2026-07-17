using Meridian.Quoting.Application.Abstractions;

namespace Meridian.Quoting.Application.Messages;

/// <summary>
///     Requests a priced quote for moving <paramref name="TeuCount" /> TEU on
///     <paramref name="Lane" /> for <paramref name="CustomerName" />, under the client-facing
///     <paramref name="Reference" /> assigned by the caller.
/// </summary>
public sealed record RequestQuoteCommand(string Reference, string Lane, string CustomerName, int TeuCount) : ICommand;