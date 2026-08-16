using Zphil.LoadBearing.Roslyn.Hosting;

namespace Zphil.LoadBearing.Cli.Mcp.Infrastructure;

/// <summary>
///     The single seam through which the MCP server reads process environment variables. Everything
///     else is a pure function of these values, which is what lets the xUnit suite drive the pipeline
///     without ever mutating real process state.
/// </summary>
internal interface IEnvironment
{
    /// <summary>Returns the value of environment variable <paramref name="name" />, or <c>null</c> if unset.</summary>
    string? GetVariable(string name);
}

/// <summary>
///     The readings a run takes off the <see cref="IEnvironment" /> seam, so each is spelled once.
/// </summary>
internal static class EnvironmentReadings
{
    /// <summary>
    ///     The operator's cache-root override (<see cref="LoadBearingEnvVars.CacheDirectory" />), or
    ///     <c>null</c> when it is unset or blank.
    /// </summary>
    /// <remarks>
    ///     One owner because a run's binlog capture store and its extraction-fragment cache have to agree on
    ///     where the cache is: they read the same variable through the same seam, and two normalizations of
    ///     "blank means unset" are two chances for them to disagree.
    /// </remarks>
    internal static string? CacheRootOverride(this IEnvironment environment)
    {
        string? cacheRoot = environment.GetVariable(LoadBearingEnvVars.CacheDirectory);
        return string.IsNullOrWhiteSpace(cacheRoot) ? null : cacheRoot;
    }
}
