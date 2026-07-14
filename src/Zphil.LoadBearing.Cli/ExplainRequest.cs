namespace Zphil.LoadBearing.Cli;

/// <summary>The parsed inputs to an <c>explain</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="RuleId">The rule ID to explain (a post-desugar ID, e.g. <c>legacy/billing/containment</c>).</param>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from (unused on the DLL fast path).</param>
internal sealed record ExplainRequest(string RuleId, string? Solution, string? Spec, string WorkingDirectory);