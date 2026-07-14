namespace Zphil.LoadBearing.Cli.Mcp.Infrastructure;

/// <summary>
///     The production <see cref="IEnvironment" /> backed by <see cref="System.Environment" />.
/// </summary>
internal sealed class SystemEnvironment : IEnvironment
{
    public string? GetVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }
}