namespace Zphil.LoadBearing.Cli;

/// <summary>The parsed inputs for <c>loadbearing status</c> — the burndown report (human or <c>--json</c>).</summary>
internal sealed record StatusRequest(string? Solution, string? Spec, bool Json, string WorkingDirectory);