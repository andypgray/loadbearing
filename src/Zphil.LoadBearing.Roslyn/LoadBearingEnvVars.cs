namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     The environment variables the Roslyn layer reads. Names only in Phase 2; the richer
///     inventory and env seam arrive with the CLI/MCP phases.
/// </summary>
internal static class LoadBearingEnvVars
{
    /// <summary>Explicit solution-file path; bypasses CWD-walk discovery.</summary>
    internal const string SolutionPath = "LOADBEARING_SOLUTION_PATH";

    /// <summary>Overrides the auto-selected Visual Studio install root for MSBuild registration.</summary>
    internal const string VsInstallPath = "LOADBEARING_VS_INSTALL_PATH";
}