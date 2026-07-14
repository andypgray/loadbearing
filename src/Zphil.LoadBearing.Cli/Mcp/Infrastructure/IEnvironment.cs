namespace Zphil.LoadBearing.Cli.Mcp.Infrastructure;

/// <summary>
///     The single seam through which the MCP server reads process environment variables. Everything
///     else is a pure function of these values, which is what lets the xUnit suite drive the pipeline
///     without ever mutating real process state.
/// </summary>
internal interface IEnvironment
{
    /// <summary>Return the value of environment variable <paramref name="name" />, or <c>null</c> if unset.</summary>
    string? GetVariable(string name);
}