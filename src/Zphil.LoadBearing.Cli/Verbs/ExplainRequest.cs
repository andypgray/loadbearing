namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>The parsed inputs to an <c>explain</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="RuleId">The rule ID to explain (a post-desugar ID, e.g. <c>legacy/billing/containment</c>).</param>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from (unused on the DLL fast path).</param>
/// <param name="NoCache">
///     Whether to bypass the persisted extraction cache entirely (no read, no write). Explain fronts it
///     because it reads presence — the rule it found — and a hit spares it the workspace altogether, this
///     verb's whole cost. Unused on the DLL fast path, which opens no solution to cache.
/// </param>
internal sealed record ExplainRequest(
    string RuleId,
    string? Solution,
    string? Spec,
    string WorkingDirectory,
    bool NoCache);
