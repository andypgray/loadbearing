using Zphil.LoadBearing.Cli.Mcp.Infrastructure;

namespace Zphil.LoadBearing.Tests.Mcp.TestDoubles;

/// <summary>
///     A hand-rolled <see cref="IEnvironment" /> for tests: dictionary-backed variables (the LoadBearing
///     seam reads only <c>MAX_MCP_OUTPUT_TOKENS</c>). Using this instead of mutating real process
///     environment variables keeps the parallel test run free of shared-state races.
/// </summary>
internal sealed class FakeEnvironment : IEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    public string? GetVariable(string name)
    {
        return _variables.GetValueOrDefault(name);
    }

    /// <summary>Set (or, with a <c>null</c> value, clear) an environment variable. Returns <c>this</c> for chaining.</summary>
    public FakeEnvironment SetVariable(string name, string? value)
    {
        if (value is null)
            _variables.Remove(name);
        else
            _variables[name] = value;

        return this;
    }
}