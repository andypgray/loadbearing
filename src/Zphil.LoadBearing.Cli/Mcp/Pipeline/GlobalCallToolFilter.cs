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
    ///     Wraps every <c>tools/call</c> so that an expected user-facing error (a
    ///     <see cref="UserErrorException" /> or a <see cref="Zphil.LoadBearing.Validation.SpecValidationException" />,
    ///     rendered through <see cref="CliErrorMapper.UserFacingMessage" />) is returned to the client as
    ///     an <see cref="CallToolResult.IsError" /> result <em>without</em> logging (it is expected, not a
    ///     bug), any other exception is logged as exactly one warning before being surfaced, and
    ///     successful text is passed through <see cref="ResponseTruncator" />. Before dispatch it runs
    ///     <see cref="UnknownParameterGuard" /> so a hallucinated argument key becomes an actionable error
    ///     rather than a silently-dropped argument, and the whole body is bracketed by
    ///     <see cref="IdleTimeoutWatchdog.EnterCall" />/<see cref="IdleTimeoutWatchdog.ExitCall" />.
    /// </summary>
    public static IMcpServerBuilder WithGlobalCallToolFilter(this IMcpServerBuilder builder)
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
                        if (FindUserFacingMessage(ex) is { } message) return ErrorResult(message);

                        context.Server.Services?.GetService<ILoggerFactory>()
                            ?.CreateLogger(typeof(GlobalCallToolFilter))
                            .LogWarning(ex, "Tool '{ToolName}' failed", context.Params.Name);

                        return ErrorResult(ex.Message);
                    }

                    if (result.IsError is not true)
                    {
                        int maxChars = ResponseTruncator.ComputeMaxChars(
                            context.Server.Services?.GetService<IEnvironment>()?.GetVariable("MAX_MCP_OUTPUT_TOKENS"));
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

    private static CallToolResult ErrorResult(string message)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
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