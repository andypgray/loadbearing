using Zphil.LoadBearing.Roslyn;

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
///     What gets written is <see cref="WorkspaceDiagnostics.Rendered" /> (or, for <c>check</c>,
///     <see cref="WorkspaceDiagnostics.RenderedWithMergeNotes" />) — composed once by the caller and handed
///     to <em>both</em> surfaces, stderr and the JSON document, so the MSBuild-selection note reaches the
///     MCP tools, which pass <see cref="TextWriter.Null" /> here. This class therefore only echoes: it never
///     appends, and never sees the load failures the gate keys on.
/// </remarks>
internal static class WorkspaceDiagnosticsRenderer
{
    /// <summary>
    ///     Writes <paramref name="diagnostics" /> to <paramref name="error" />, one line each. A no-op for
    ///     an empty list.
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
