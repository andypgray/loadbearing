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
internal sealed class ExplainRunner(TextWriter output)
{
    public async Task<int> RunAsync(ExplainRequest request, CancellationToken ct)
    {
        ArchitectureModel model = await LoadModelAsync(request, ct);

        ArchRule? rule = model.Rules.FirstOrDefault(candidate => candidate.Id == request.RuleId);
        if (rule is null) throw new UserErrorException(UnknownRuleMessage(request.RuleId, model));

        foreach (string line in ExplainFormatter.Lines(rule)) output.WriteLine(line);
        return 0;
    }

    private static async Task<ArchitectureModel> LoadModelAsync(ExplainRequest request, CancellationToken ct)
    {
        // Fast path: a built-DLL --spec resolves without ever opening the solution (R4).
        SpecResolution? withoutSolution = SpecResolver.TryResolveWithoutSolution(request.Spec);
        if (withoutSolution is not null) return ModelPipeline.LoadModel(withoutSolution.DllPath);

        // Convention or csproj --spec: load the workspace for resolution only; never extract.
        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            request.Solution, request.Spec, request.WorkingDirectory, ct);
        return workspace.Model;
    }

    private static string UnknownRuleMessage(string ruleId, ArchitectureModel model)
    {
        var ids = model.Rules.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal);
        return $"Unknown rule ID '{ruleId}'. Available rule IDs:\n  " + string.Join("\n  ", ids);
    }
}