namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The parsed inputs for <c>loadbearing graph</c> — the pre-spec codebase survey (human or
///     <c>--json</c>). Deliberately no <c>Spec</c>: the survey is a property of the codebase, and derive
///     runs before any spec exists.
/// </summary>
internal sealed record GraphRequest(string? Solution, bool Json, string WorkingDirectory);