namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The parsed inputs for <c>loadbearing baseline</c>. Exactly one of <see cref="Init" /> /
///     <see cref="AcceptReductions" /> / <see cref="Add" /> must be set. The <c>--add</c> companions
///     (<see cref="Rule" />, <see cref="Because" />, <see cref="Source" />, <see cref="Target" />,
///     <see cref="Subject" />) ride along and are validated together with the mode — all before any
///     workspace cost.
/// </summary>
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