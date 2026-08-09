using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Cli.Mcp;

/// <summary>
///     The solution + spec this MCP server is bound to for its lifetime, captured once at
///     <c>loadbearing mcp</c> startup, and the run policy every tool call applies on top of them.
///     Resolution (solution discovery, spec load, workspace open) happens per tool call, not here, so any
///     resolution error text matches the CLI exactly.
/// </summary>
/// <remarks>
///     <para>
///         Startup runs discovery once ahead of all that, and what it does with a failure depends on whether
///         <see cref="Solution" /> was given. An argument that does not resolve exits 2 — the operator named
///         something wrong, and a server that answers every call with that error is worse than not starting.
///         With no argument the walk-up is optional by documentation, so its failure only records a reason: the
///         server starts unbound and announces it (<c>McpServerCommand.ResolveBoundSolution</c>). Per-call
///         resolution is what makes that coherent — the tools repeat the same discovery and the same message.
///     </para>
///     <para>
///         <b>How an MCP tool call differs from a CLI run is a four-part policy, and it lives here.</b> Every
///         tool asks for the JSON document (the tools return documents, never human text); bypasses the
///         persisted extraction cache (the warm workspace and <c>cache.json</c> keep independent lifetimes
///         and must never race on the file); never replays a build capture (latency-critical callers ride the
///         session); and reports an incomplete model inside its answer rather than refusing to produce one,
///         because this surface has no exit code to carry the verdict — <c>workspaceDiagnostics</c> and
///         <c>modelIncomplete</c> do. The factories below name each leg once, as a named constant, so no
///         tool body can spell one wrong and no leg can change in one place only.
///     </para>
/// </remarks>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from (the server's launch cwd).</param>
internal sealed record McpServerBinding(string? Solution, string? Spec, string WorkingDirectory)
{
    /// <summary>The tools answer with documents, never the human rendering: always <c>--json</c>.</summary>
    private const bool AsJsonDocument = true;

    /// <summary>
    ///     A tool call never reads or writes <c>cache.json</c>: the warm session and the persisted cache keep
    ///     independent lifetimes, so they can never race on the file.
    /// </summary>
    private const bool BypassPersistedCache = true;

    /// <summary>The warm path never uses the build capture — latency-critical callers ride the session.</summary>
    private const string? NoBinlogReplay = null;

    /// <summary>
    ///     The incompleteness rides the document (<c>workspaceDiagnostics</c> + <c>modelIncomplete</c>)
    ///     rather than an exit code this surface does not have, so the run answers instead of refusing.
    /// </summary>
    private const bool ReportPartialModels = true;

    /// <summary>No SARIF file: the tools return their document, and write nothing to disk.</summary>
    private const string? NoSarifFile = null;

    /// <summary>
    ///     The <c>arch_check</c> run. The rule globs go in raw, so the same parse and the same
    ///     unmatched-filter refusal serve both surfaces.
    /// </summary>
    /// <param name="diffBase">The git ref the Quarantine tripwire compares against, or null to skip it.</param>
    /// <param name="rules">Rule-ID globs narrowing what runs, or null for every rule.</param>
    internal CheckRequest CheckRequest(string? diffBase, string? rules)
    {
        return new CheckRequest(
            Solution, Spec, AsJsonDocument, diffBase, WorkingDirectory, BypassPersistedCache, NoBinlogReplay,
            ReportPartialModels, NoSarifFile, rules);
    }

    /// <summary>The <c>arch_status</c> run: the whole burndown, which carries no narrowing knob.</summary>
    internal StatusRequest StatusRequest()
    {
        return new StatusRequest(
            Solution, Spec, AsJsonDocument, WorkingDirectory, BypassPersistedCache, NoBinlogReplay,
            ReportPartialModels);
    }

    /// <summary>The <c>arch_explain</c> run — a spec lookup, so none of the four policy legs applies.</summary>
    /// <param name="ruleId">The post-desugar rule ID to explain.</param>
    internal ExplainRequest ExplainRequest(string ruleId)
    {
        return new ExplainRequest(ruleId, Solution, Spec, WorkingDirectory);
    }

    /// <summary>The <c>arch_context</c> run — a card lookup, so none of the four policy legs applies.</summary>
    /// <param name="path">The file or directory to find covering scope cards for.</param>
    internal ContextRequest ContextRequest(string path)
    {
        return new ContextRequest(path, Solution, Spec, WorkingDirectory);
    }

    /// <summary>
    ///     The <c>arch_graph</c> run. Two departures from the policy above, both deliberate:
    ///     <see cref="Spec" /> goes unused, because the survey is a property of the codebase and derive runs
    ///     before any spec exists; and <paramref name="allowWorkspaceDiagnostics" /> is the caller's, because
    ///     graph refuses before it has a document to stamp, so opting into a partial model is a choice the
    ///     tool exposes rather than one the surface makes.
    /// </summary>
    /// <param name="allowWorkspaceDiagnostics">Whether to survey the partial model instead of refusing.</param>
    /// <param name="grain">The floor on detail — coarser, never narrower.</param>
    /// <param name="projects">Project-name globs narrowing the subject, or null for every project.</param>
    /// <param name="responseBudgetChars">The transport's response budget, which the runner degrades against.</param>
    internal GraphRequest GraphRequest(
        bool allowWorkspaceDiagnostics, GraphGrain grain, string? projects, int responseBudgetChars)
    {
        return new GraphRequest(
            Solution, AsJsonDocument, WorkingDirectory, BypassPersistedCache, NoBinlogReplay,
            allowWorkspaceDiagnostics, grain, projects, responseBudgetChars);
    }
}
