namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>The parsed inputs to a <c>render</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to render from the partial model when a project fails to load. Default (<c>false</c>): any
///     workspace-load failure refuses the whole command with exit 2 and writes nothing, because the files
///     render writes are committed context — a card whose project failed to load places nowhere, and
///     <c>--diagram</c> would draw a survey missing whole projects. Keys on exactly what <c>check</c> keys
///     on (<c>IncompleteModelGate</c>).
/// </param>
/// <param name="Diagram">The <c>--diagram</c> target file, or null to render no diagram.</param>
/// <param name="DiagramOnly">The <c>--diagram-only</c> allow-list: project-name globs, semicolon-separated.</param>
/// <param name="DiagramExclude">The <c>--diagram-exclude</c> deny-list: project-name globs, semicolon-separated.</param>
internal sealed record RenderRequest(
    string? Solution,
    string? Spec,
    string WorkingDirectory,
    bool AllowWorkspaceDiagnostics,
    string? Diagram = null,
    string? DiagramOnly = null,
    string? DiagramExclude = null);
