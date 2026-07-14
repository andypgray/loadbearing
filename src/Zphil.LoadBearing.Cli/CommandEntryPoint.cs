namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The outer shell shared by every command action: run the action (which crosses the MSBuild gate)
///     and hand any failure to <see cref="CliErrorMapper" /> (which renders it and returns exit 2). Its
///     own body touches no Roslyn type, so it stays outside the JIT quarantine.
/// </summary>
internal static class CommandEntryPoint
{
    public static async Task<int> RunAsync(Func<Task<int>> action, TextWriter error)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            return CliErrorMapper.Write(ex, error);
        }
    }
}