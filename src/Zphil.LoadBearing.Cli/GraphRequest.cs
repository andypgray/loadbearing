using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The parsed inputs for <c>loadbearing graph</c> — the pre-spec codebase survey (human or
///     <c>--json</c>). Deliberately no <c>Spec</c>: the survey is a property of the codebase, and derive
///     runs before any spec exists.
/// </summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Json">Whether to emit the machine-readable JSON survey document instead of human output.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">The explicit <c>--binlog</c> to replay instead of a design-time build, or null to auto-select.</param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to survey the partial model when a project fails to load. Default (<c>false</c>): the survey
///     refuses before extraction with exit 2 (MCP: an error result), because a survey missing whole projects
///     is a wrong map, not a smaller one. Keys on exactly what <c>check</c> keys on
///     (<see cref="IncompleteModelGate" />).
/// </param>
/// <param name="Grain">
///     The floor on how much detail to render: the survey is never finer than this, and a caller whose
///     transport has a response budget may take it coarser still (<see cref="IResponseFitter" />). Coarser,
///     never narrower — grain and scope are separate knobs. No budget rides this record: what a channel can
///     carry is a property of the caller, not of the question asked.
/// </param>
/// <param name="Projects">
///     The <c>--projects</c> allow-list — project-name globs, semicolon-separated — or null for every
///     project. A filter that matches nothing refuses the run rather than surveying an empty codebase.
/// </param>
internal sealed record GraphRequest(
    string? Solution,
    bool Json,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog,
    bool AllowWorkspaceDiagnostics,
    DocumentGrain Grain,
    string? Projects);
