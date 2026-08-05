using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The one place the CLI writes workspace diagnostics to stderr, shared by the five verbs that render
///     them — <c>check</c>, <c>status</c>, <c>graph</c>, <c>baseline</c> and <c>render</c>. (<c>explain</c>
///     is the sixth workspace verb and takes no error writer at all; it renders no diagnostics.)
///     Diagnostics carry the <c>warning:</c> prefix in human mode and go bare under <c>--json</c>, where
///     they also ride the document's <c>workspaceDiagnostics</c> array on stdout; stdout purity is why
///     nothing here ever writes to it.
/// </summary>
/// <remarks>
///     <para>
///         When — and only when — there is at least one diagnostic, one further line names the MSBuild the
///         run registered and the environment variable that overrides it. A project that fails to load is
///         nearly always a question about which MSBuild opened it, and until this line existed the answer
///         was unobtainable: the selection was described in four places in
///         <see cref="MsBuildBootstrap" /> and printed in none, so the failure arrived as a bare exit code.
///         Quiet runs stay quiet — the note is diagnostic context, not a banner.
///     </para>
///     <para>
///         The note is <em>not</em> a workspace diagnostic. It never enters
///         <see cref="CodebaseSource.Diagnostics" />, which is the fail-closed gate's input: an
///         informational line there would flip <c>check</c>'s exit code to 2 on every run.
///     </para>
/// </remarks>
internal static class WorkspaceDiagnosticsRenderer
{
    /// <summary>
    ///     Writes <paramref name="diagnostics" /> to <paramref name="error" />, followed by the MSBuild
    ///     selection note when the list is non-empty. A no-op for an empty list.
    /// </summary>
    /// <param name="error">The stderr writer; never stdout.</param>
    /// <param name="diagnostics">The workspace-load diagnostics (and, for <c>check</c>, the merge notes).</param>
    /// <param name="json">
    ///     <see langword="true" /> under <c>--json</c>, which drops the <c>warning:</c> prefix — the
    ///     diagnostics are structured data in the document, and stderr is their unadorned echo.
    /// </param>
    internal static void Render(TextWriter error, IReadOnlyList<string> diagnostics, bool json = false)
    {
        if (diagnostics.Count == 0) return;

        foreach (string diagnostic in diagnostics) error.WriteLine(Line(diagnostic, json));
        error.WriteLine(Line(MsBuildNote(), json));
    }

    private static string Line(string text, bool json)
    {
        return json ? text : $"warning: {text}";
    }

    /// <summary>
    ///     The MSBuild-selection line that follows a non-empty diagnostics list. Internal because the
    ///     <c>graph</c> refusal carries its diagnostics inside a thrown message rather than through
    ///     <see cref="Render" />, and must not lose the one line that says which MSBuild opened the projects
    ///     that failed.
    /// </summary>
    // Reads the selection back through the quarantine's sanctioned boundary type. Null only if nothing
    // registered MSBuild at all, which for a verb that just opened a workspace is itself worth saying.
    internal static string MsBuildNote()
    {
        string selection = MsBuildBootstrap.LastSelection ?? "not registered by this process";
        return $"MSBuild for this run: {selection}. Set {LoadBearingEnvVars.VsInstallPath} to a Visual Studio "
               + "install root (the parent of MSBuild\\Current\\Bin) to select a different MSBuild.";
    }
}