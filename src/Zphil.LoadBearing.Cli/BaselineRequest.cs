namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The parsed inputs for <c>loadbearing baseline</c>. Exactly one of <see cref="Init" /> /
///     <see cref="AcceptReductions" /> / <see cref="Add" /> must be set. The <c>--add</c> companions
///     (<see cref="Rule" />, <see cref="Because" />, <see cref="Source" />, <see cref="Target" />,
///     <see cref="Subject" />) ride along and are validated together with the mode — all before any
///     workspace cost.
/// </summary>
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
    string WorkingDirectory);