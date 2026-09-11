using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>
///     The <c>explain</c> pipeline: load the model (the DLL fast path needs no workspace; convention or
///     a csproj loads one for resolution only, never extraction) → find the rule by ID → dump its
///     fields (<see cref="ExplainFormatter" />) → exit 0. An unknown ID is a <see cref="UserErrorException" />
///     listing every available (post-desugar) ID, ordinal-sorted → exit 2 — unless the ID is a JSON array
///     written as text, which is named as that instead of rostered (<see cref="Refusals" />). A missing ID
///     argument never reaches here — System.CommandLine rejects it as a parse error, remapped to exit 2.
/// </summary>
/// <remarks>
///     <para>
///         <b>Never a gate.</b> A workspace opened for resolution can fail partially; explain renders the
///         composed diagnostics to stderr and still answers, because the model it dumps comes from the spec,
///         not the codebase — a load failure cannot make the answer wrong. The DLL fast path never opens a
///         workspace, so it stays silent by construction.
///     </para>
///     <para>
///         <b>It fronts the persisted extraction cache</b> (<c>--no-cache</c> opts out), and this is the
///         verb the cache does the most for: it never extracts at all, so on a hit the recorded resolution
///         replays and the run opens <em>no workspace</em> — the whole of what a convention or csproj
///         <c>--spec</c> costs. Nothing here reads absence as evidence: an unknown rule ID is a refusal
///         against the spec's own rule list, which no cache state can shorten.
///     </para>
/// </remarks>
internal sealed class ExplainRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
    : CacheWiredRunner(source, environment)
{
    public async Task<int> RunAsync(ExplainRequest request, CancellationToken ct)
    {
        ArchitectureModel model = await LoadModelAsync(request, ct);

        ArchRule? rule = model.Rules.FirstOrDefault(candidate => candidate.Id == request.RuleId);
        if (rule is null) throw new UserErrorException(UnknownRuleMessage(request.RuleId, model));

        foreach (string line in ExplainFormatter.Lines(rule)) await output.WriteLineAsync(line);
        return 0;
    }

    private async Task<ArchitectureModel> LoadModelAsync(ExplainRequest request, CancellationToken ct)
    {
        // Fast path: a built-DLL --spec resolves without ever opening the solution. The model still comes
        // through the source, so a warm host serves its cached one rather than reloading the DLL per call.
        SpecResolution? withoutSolution = SpecResolver.TryResolveWithoutSolution(request.Spec);
        if (withoutSolution is not null) return SolutionSource.LoadSpecModel(withoutSolution.DllPath);

        // Convention or csproj --spec: load the workspace for resolution only; never extract. The acquisition
        // seam is the shared one, so explain's model is the model every other verb would have loaded — its
        // lazy ExtractAsync is simply never called. On a cache hit the recorded resolution replays and no
        // workspace opens either, which is this verb's entire cost.
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory,
            request.NoCache, ct);
        RecordCacheOutcome(source);

        // Composed like every other verb's, so the MSBuild-selection note accompanies the load failures.
        // Nothing gates on them here: the rule being dumped is the spec's, so a project that failed to load
        // cannot change the answer — only the visibility of the failure.
        WorkspaceDiagnosticsRenderer.Render(error, source.Diagnostics.Rendered);

        return source.Model;
    }

    // A JSON array a client wrote into the string parameter is answered on its shape rather than with the
    // roster: every ID the reader could want is inside their own brackets already.
    private static string UnknownRuleMessage(string ruleId, ArchitectureModel model)
    {
        var lead = $"Unknown rule ID '{ruleId}'";

        return Refusals.StringifiedArrayRefusal(
                   ruleId, lead, elements => Refusals.SingleValueAdvice("pass one rule ID", elements))
               ?? Refusals.RuleNotFound(lead, model);
    }
}
