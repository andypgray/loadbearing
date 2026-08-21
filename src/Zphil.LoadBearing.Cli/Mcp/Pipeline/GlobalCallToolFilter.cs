using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The single point where tool-call exceptions become error results and successful responses are
///     truncated. Tool methods never <c>try/catch</c>: they throw, and this filter shapes the outcome.
///     It also brackets every call with the idle watchdog's in-flight count so a long cold-solution load
///     can never self-trip the timeout.
/// </summary>
internal static class GlobalCallToolFilter
{
    // The SDK's argument-marshalling layer wraps a coercer-thrown UserErrorException one or two
    // JsonExceptions deep; 8 is loose headroom against a pathological chain.
    private const int MaxExceptionChainDepth = 8;

    /// <summary>
    ///     Wraps every <c>tools/call</c> so an expected user-facing error comes back as an error result,
    ///     anything unexpected is logged exactly once, and successful text is truncated to the client's budget.
    /// </summary>
    /// <remarks>
    ///     An expected user-facing error — a <see cref="UserErrorException" /> or a
    ///     <see cref="Zphil.LoadBearing.Validation.SpecValidationException" />, rendered through
    ///     <see cref="CliErrorMapper.UserFacingMessage" /> — becomes an
    ///     <see cref="CallToolResult.IsError" /> result <em>without</em> logging, because it is expected
    ///     rather than a bug; any other exception is logged as exactly one warning before being surfaced.
    ///     Successful text passes through <see cref="ResponseTruncator" />. Before dispatch the filter runs
    ///     <see cref="UnknownParameterGuard" /> so a hallucinated argument key becomes an actionable error
    ///     rather than a silently-dropped argument, and the whole body is bracketed by
    ///     <see cref="IdleTimeoutWatchdog.EnterCall" />/<see cref="IdleTimeoutWatchdog.ExitCall" />. On an
    ///     unbound server every error also carries <see cref="ServerInstructions.UnboundCallCoda" />, because
    ///     the banner that named a recovery was delivered once, at the handshake, and a reader arriving at a
    ///     failed tool call may never have seen it.
    /// </remarks>
    /// <param name="builder">The server builder being composed.</param>
    /// <param name="bindingFailure">
    ///     The discovery refusal, or <see langword="null" /> when the server bound. Only its nullness is
    ///     read; it takes the same shape as <see cref="ServerInstructions.For" />'s parameter so one call
    ///     site passes one value to both and the handshake and the per-call replies cannot disagree about
    ///     whether this server is bound. Required rather than optional: a forgotten coda would be a silent
    ///     hole in the only channel an unbound session still reads.
    /// </param>
    public static IMcpServerBuilder WithGlobalCallToolFilter(this IMcpServerBuilder builder, string? bindingFailure)
    {
        return builder.WithRequestFilters(filters =>
        {
            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                IdleTimeoutWatchdog.EnterCall();
                try
                {
                    CallToolResult result;
                    try
                    {
                        // Reject unknown argument keys before binding; its message is a UserErrorException,
                        // so it flows through the silent-user-error path below.
                        if (UnknownParameterGuard.Validate(context.Params.Name, context.Params.Arguments) is { } unknownParameterError)
                            throw new UserErrorException(unknownParameterError);

                        result = await next(context, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Expected user-facing error (bad input, missing solution, spec validation, a
                        // tampered baseline) — possibly wrapped in JsonException(s) by the SDK's argument
                        // binder. Walk the chain: the first UserFacingMessage surfaces silently, exactly as
                        // a directly-thrown one would. Anything else is a bug: log one warning, then surface.
                        if (FindUserFacingMessage(ex) is { } message) return ErrorResult(message, bindingFailure);

                        context.Server.Services?.GetService<ILoggerFactory>()
                            ?.CreateLogger(typeof(GlobalCallToolFilter))
                            .LogWarning(ex, "Tool '{ToolName}' failed", context.Params.Name);

                        return ErrorResult(ex.Message, bindingFailure);
                    }

                    if (result.IsError is not true)
                    {
                        // The same budget the tools degrade against, resolved from the request context — one
                        // service, so a response can never be composed against one cap and cut against
                        // another. Required, not optional: the old GetService chain fell back to the default
                        // cap whenever the service was missing, which turned a composition bug into a
                        // silently-wrong number. The provider cannot be absent by the time a result exists —
                        // the tool that produced it was itself constructed from it — unlike the logger above,
                        // which is best-effort by design.
                        int maxChars = context.Server.Services!.GetRequiredService<IResponseBudget>()
                            .MaxChars();
                        string toolName = context.Params.Name;
                        foreach (ContentBlock contentBlock in result.Content)
                            if (contentBlock is TextContentBlock textBlock)
                                textBlock.Text = ResponseTruncator.TruncateIfNeeded(textBlock.Text, toolName, maxChars);
                    }

                    return result;
                }
                finally
                {
                    IdleTimeoutWatchdog.ExitCall();
                }
            });
        });
    }

    /// <summary>
    ///     The error result for <paramref name="message" />, with the session's recovery appended when the
    ///     server never bound to a solution.
    /// </summary>
    /// <remarks>
    ///     The coda rides every error while unbound, not just the discovery refusal. While unbound every
    ///     dispatched call ends in that refusal anyway, and the calls that fail earlier — the unknown-key
    ///     guard, a coercer — would only reach it once the argument was fixed, so the advice is true of all
    ///     of them. Scoping it to the refusal's own text would instead couple this filter to prose that is
    ///     pinned verbatim elsewhere. Errors bypass the truncator, so the coda can never be the part that
    ///     gets cut.
    /// </remarks>
    private static CallToolResult ErrorResult(string message, string? bindingFailure)
    {
        string text = bindingFailure is null
            ? message
            : message + "\n\n" + ServerInstructions.UnboundCallCoda;

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            IsError = true
        };
    }

    /// <summary>
    ///     Walks up to <see cref="MaxExceptionChainDepth" /> inner exceptions for the first one
    ///     <see cref="CliErrorMapper.UserFacingMessage" /> recognizes (the SDK's argument binder can bury a
    ///     coercer-thrown <see cref="UserErrorException" /> inside <c>JsonException</c>(s)), returning its
    ///     rendered message or <see langword="null" /> when the failure is a genuine unexpected error.
    /// </summary>
    private static string? FindUserFacingMessage(Exception? ex)
    {
        for (var depth = 0; ex is not null && depth < MaxExceptionChainDepth; depth++)
        {
            if (CliErrorMapper.UserFacingMessage(ex) is { } message) return message;
            ex = ex.InnerException;
        }

        return null;
    }
}
