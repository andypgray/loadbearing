using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The parsed inputs for <c>loadbearing baseline</c>. Exactly one of <see cref="Init" /> /
///     <see cref="AcceptReductions" /> / <see cref="Add" /> must be set. The <c>--add</c> companions
///     (<see cref="Rule" />, <see cref="Because" />, <see cref="Source" />, <see cref="Target" />,
///     <see cref="Subject" />) ride along and are validated together with the mode — all before any
///     workspace cost.
/// </summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="Init">
///     Whether <c>--init</c> was passed: capture each <em>uncaptured</em> ratcheted rule's current violations.
/// </param>
/// <param name="AcceptReductions">
///     Whether <c>--accept-reductions</c> was passed: drop captured entries whose violation no longer
///     occurs, and refuse new ones.
/// </param>
/// <param name="Add">
///     Whether <c>--add</c> was passed: grandfather exactly one currently observed violation, with attribution.
/// </param>
/// <param name="Rule">The ratcheted rule ID the <c>--add</c> entry joins, or null outside that mode.</param>
/// <param name="Because">
///     The mandatory single-line attribution recorded on an <c>--add</c> entry, or null outside that mode.
/// </param>
/// <param name="Source">
///     The edge's referencing type — a full type name or <c>T:</c> symbol ID — for the edge form of <c>--add</c>.
/// </param>
/// <param name="Target">
///     The edge's referenced type, or a banned member's full name or member symbol ID, for the edge form of
///     <c>--add</c>.
/// </param>
/// <param name="Subject">
///     The offending type or member — a full name or symbol ID — for the shape form of <c>--add</c>.
/// </param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to write baselines from the partial model when a project fails to load. Default
///     (<c>false</c>): any workspace-load failure refuses the whole command with exit 2 and writes nothing,
///     because a baseline is the team's signature on its debt and a partial model has no idea what that debt
///     is. Keys on exactly what <c>check</c> keys on (<see cref="IncompleteModelGate" />).
/// </param>
internal sealed record BaselineRequest(
    string? Solution,
    string? Spec,
    bool Init,
    bool AcceptReductions,
    bool Add,
    string? Rule,
    string? Because,
    string? Source,
    string? Target,
    string? Subject,
    string WorkingDirectory,
    bool AllowWorkspaceDiagnostics);
