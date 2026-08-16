using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The client's response budget in characters — the one number both halves of the over-budget answer are
///     measured against, so a document can never degrade against one cap and then be truncated against another.
/// </summary>
internal interface IResponseBudget
{
    /// <summary>
    ///     The cap right now. Called per tool call, never cached: a client that moves its budget mid-session
    ///     moves it for the next call, and a snapshot taken at construction would answer for the whole
    ///     process lifetime.
    /// </summary>
    int MaxChars();
}

/// <summary>
///     The budget as the server reads it: the client's
///     <see cref="LoadBearingEnvVars.MaxMcpOutputTokens" /> through the
///     <see cref="IEnvironment" /> seam, converted by
///     <see cref="ResponseTruncator.ComputeMaxChars" /> — which stays the pure computation its own tests pin.
/// </summary>
/// <remarks>
///     A singleton over a seam that is itself read per call. That is deliberate and is the single likeliest
///     own-goal here: reading <see cref="IEnvironment" /> once at construction passes every unit test and
///     silently pins the first call's budget for the life of the server.
/// </remarks>
internal sealed class ResponseBudget(IEnvironment environment) : IResponseBudget
{
    /// <inheritdoc />
    public int MaxChars()
    {
        return ResponseTruncator.ComputeMaxChars(environment.GetVariable(LoadBearingEnvVars.MaxMcpOutputTokens));
    }
}
