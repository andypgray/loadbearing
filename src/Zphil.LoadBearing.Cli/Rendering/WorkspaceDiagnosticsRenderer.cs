using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The one place the CLI writes workspace diagnostics to stderr, shared by the six verbs that render
///     them there — <c>check</c>, <c>status</c>, <c>graph</c>, <c>baseline</c>, <c>render</c> and
///     <c>explain</c>. Diagnostics carry the <c>warning:</c> prefix in human mode and go bare under
///     <c>--json</c>, where they also ride the document's <c>workspaceDiagnostics</c> array on stdout;
///     stdout purity is why nothing here ever writes to it. <c>context</c> is the deliberate exception to
///     that rule and so does not route through this class: it has no CLI verb and no <c>--json</c>, its
///     body is its only channel, and <c>ContextRunner</c> writes its own caveat
///     (<c>IncompleteModelGate.ContextCaveat</c>) straight to stdout, ahead of its answer.
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
///         <b>The note rides the composed list, not the write.</b> <see cref="Compose" /> appends it once,
///         and callers hand that one list to <em>both</em> renderers — stderr and the JSON document. That is
///         what makes it reachable from MCP, where the tools pass <see cref="TextWriter.Null" /> as the
///         error writer: the note was the single line the MCP surface lost, and it now arrives inside
///         <c>workspaceDiagnostics</c> like everything else. Appending at write time reached stderr only.
///     </para>
///     <para>
///         The note is <em>not</em> a workspace diagnostic. It never enters
///         <see cref="CodebaseSource.Diagnostics" />, which is the fail-closed gate's input: an
///         informational line there would flip <c>check</c>'s exit code to 2 on every run. Callers therefore
///         gate on the source's own list and render the composed one — two visibly different variables in
///         the same method.
///     </para>
/// </remarks>
internal static class WorkspaceDiagnosticsRenderer
{
    /// <summary>
    ///     The list both surfaces read: <paramref name="diagnostics" /> with the MSBuild selection note
    ///     appended, or empty for an empty input — a clean run says nothing about MSBuild anywhere.
    /// </summary>
    /// <param name="diagnostics">
    ///     The workspace-load diagnostics (and, for <c>check</c>, the merge notes). Never the fail-closed
    ///     gate's input: pass the source's own list there, not this one.
    /// </param>
    internal static IReadOnlyList<string> Compose(IReadOnlyList<string> diagnostics)
    {
        return diagnostics.Count == 0 ? [] : [.. diagnostics, MsBuildBootstrap.SelectionNote()];
    }

    /// <summary>
    ///     Writes <paramref name="diagnostics" /> to <paramref name="error" />, one line each. A no-op for
    ///     an empty list. Callers pass a <see cref="Compose" />d list, so the MSBuild note is already in it.
    /// </summary>
    /// <param name="error">The stderr writer; never stdout.</param>
    /// <param name="diagnostics">The composed diagnostics to echo.</param>
    /// <param name="json">
    ///     <see langword="true" /> under <c>--json</c>, which drops the <c>warning:</c> prefix — the
    ///     diagnostics are structured data in the document, and stderr is their unadorned echo.
    /// </param>
    internal static void Render(TextWriter error, IReadOnlyList<string> diagnostics, bool json = false)
    {
        foreach (string diagnostic in diagnostics) error.WriteLine(Line(diagnostic, json));
    }

    private static string Line(string text, bool json)
    {
        return json ? text : $"warning: {text}";
    }
}
