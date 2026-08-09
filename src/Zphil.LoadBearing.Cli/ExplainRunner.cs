using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>explain</c> pipeline: load the model (the DLL fast path needs no workspace; convention or
///     a csproj loads one for resolution only, never extraction) → find the rule by ID → dump its
///     fields (<see cref="ExplainFormatter" />) → exit 0. An unknown ID is a <see cref="UserErrorException" />
///     listing every available (post-desugar) ID, ordinal-sorted → exit 2. A missing ID argument never
///     reaches here — System.CommandLine rejects it as a parse error, remapped to exit 2.
/// </summary>
/// <remarks>
///     <b>Never a gate.</b> A workspace opened for resolution can fail partially; explain renders the
///     composed diagnostics to stderr and still answers, because the model it dumps comes from the spec, not
///     the codebase — a load failure cannot make the answer wrong. The DLL fast path never opens a
///     workspace, so it stays silent by construction.
/// </remarks>
internal sealed class ExplainRunner(TextWriter output, TextWriter error, ISolutionSource? source = null)
    : WorkspaceRunner(source)
{
    public async Task<int> RunAsync(ExplainRequest request, CancellationToken ct)
    {
        ArchitectureModel model = await LoadModelAsync(request, ct);

        ArchRule? rule = model.Rules.FirstOrDefault(candidate => candidate.Id == request.RuleId);
        if (rule is null) throw new UserErrorException(UnknownRuleMessage(request.RuleId, model));

        foreach (string line in ExplainFormatter.Lines(rule)) output.WriteLine(line);
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
        // lazy ExtractAsync is simply never called.
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, request.Solution, request.Spec, request.WorkingDirectory, ct);

        // Composed like every other verb's, so the MSBuild-selection note accompanies the load failures.
        // Nothing gates on them here: the rule being dumped is the spec's, so a project that failed to load
        // cannot change the answer — only the visibility of the failure.
        WorkspaceDiagnosticsRenderer.Render(error, source.Diagnostics.Rendered);

        return source.Model;
    }

    private static string UnknownRuleMessage(string ruleId, ArchitectureModel model)
    {
        return Refusals.NotFoundMessage(
            $"Unknown rule ID '{ruleId}'", "Available rule IDs", Refusals.AvailableRuleIds(model));
    }
}
