using System.Runtime.CompilerServices;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Xunit;

/// <summary>
///     The xUnit adapter (DESIGN.md §10): derive a sealed test class from
///     <c>ArchRuleTests&lt;YourSpec&gt;</c>, point <see cref="SolutionPath" /> at the solution to check,
///     and every post-desugar rule in the spec becomes its own named test — the rule ID <em>is</em> the
///     test's display name, so a failing architecture rule reads as a failing test in the test explorer.
///     A failing rule's message is the exact CLI human block (<see cref="HumanReportRenderer.RuleBlock" />),
///     a Freeze tripwire (no diff context in a test run) is reported as skipped, and everything else passes.
/// </summary>
/// <remarks>
///     <para>
///         Rules enumerate at <em>discovery</em> time from the spec alone (no Roslyn, no workspace), so the
///         test explorer lists one case per rule ID. The workspace load + extraction + check runs once per
///         closed <typeparamref name="TSpec" /> (statics on a generic type are per-instantiation), lazily,
///         when the first case executes; every rule case reads its verdict from that shared run.
///     </para>
///     <para>
///         MSBuild is registered inside the run pipeline behind a <see cref="MethodImplOptions.NoInlining" />
///         wrapper, so a consuming test project needs no <c>[ModuleInitializer]</c> of its own.
///     </para>
/// </remarks>
/// <typeparam name="TSpec">The architecture spec to check — must be default-constructible.</typeparam>
public abstract class ArchRuleTests<TSpec> where TSpec : IArchitectureSpec, new()
{
    private static readonly object Gate = new();
    private static Task<ArchCheckRun>? s_run;

    /// <summary>The solution (<c>.sln</c>/<c>.slnx</c>) to check the spec against.</summary>
    protected abstract string SolutionPath { get; }

    /// <summary>
    ///     The project to exclude from the checked universe — the spec's own project when the spec is a
    ///     solution member (mirrors the CLI's spec-member exclusion). Defaults to the spec assembly's name;
    ///     override to <see langword="null" /> when the spec lives outside the target solution.
    /// </summary>
    protected virtual string? ExcludeProjectName => typeof(TSpec).Assembly.GetName().Name;

    /// <summary>
    ///     The discovery-time row source: one row per post-desugar rule ID, its ID doubling as the test
    ///     display name. Builds the model from the spec alone (no Roslyn). A spec-build failure collapses to
    ///     one sentinel row so it lands red at run time — where the pipeline rebuild rethrows the real
    ///     <c>SpecValidationException</c> — rather than as a silent discovery diagnostic.
    /// </summary>
    public static IEnumerable<ITheoryDataRow> RuleRows()
    {
        ArchitectureModel model;
        try
        {
            model = ArchModelBuilder.Build(new TSpec());
        }
        catch
        {
            return
            [
                new TheoryDataRow<string>(string.Empty) { TestDisplayName = $"{typeof(TSpec).Name}: spec build failed" }
            ];
        }

        return model.Rules.Select(ITheoryDataRow (rule) =>
            new TheoryDataRow<string>(rule.Id) { TestDisplayName = rule.Id });
    }

    /// <summary>
    ///     One rule's verdict from the shared check run: a Freeze tripwire (no diff context) is skipped, a
    ///     violated rule fails with the CLI human block, everything else passes.
    /// </summary>
    [Theory]
    [MemberData(nameof(RuleRows))]
    public async Task Rule_Holds(string ruleId)
    {
        ArchCheckRun run = await GetRunAsync();

        if (!run.ResultsById.TryGetValue(ruleId, out RuleResult? result))
            throw new InvalidOperationException(
                $"Rule '{ruleId}' was enumerated at discovery but is absent from the check run.");

        switch (result.Status)
        {
            case RuleStatus.Skipped:
                Assert.Skip(result.SkipReason ?? "rule skipped");
                break;
            case RuleStatus.Failed:
                Assert.Fail(HumanReportRenderer.RuleBlock(result, run.SolutionDirectory));
                break;
        }
    }

    // Lazily start (and then share) the one check run for this closed TSpec, seeded by the first case's
    // SolutionPath/ExcludeProjectName. A cached faulted task rethrows on every case (e.g. a spec-build or
    // workspace failure surfaces identically on every rule).
    private Task<ArchCheckRun> GetRunAsync()
    {
        if (s_run is not null) return s_run;
        lock (Gate)
        {
            return s_run ??= RunPipelineAsync(SolutionPath, ExcludeProjectName);
        }
    }

    // NoInlining + the non-Roslyn EnsureMsBuild() first: keeps the JIT from resolving MSBuildWorkspace
    // before MSBuildLocator registration, so a consumer needs no ModuleInitializer. Order mirrors the CLI's
    // CheckPipeline: baselines before extraction (a tampered baseline fails fast, before the Roslyn walk).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<ArchCheckRun> RunPipelineAsync(string solutionPath, string? excludeProjectName)
    {
        EnsureMsBuild();

        ArchitectureModel model = ArchModelBuilder.Build(new TSpec());
        string fullSolutionPath = Path.GetFullPath(solutionPath);
        string solutionDirectory = Path.GetDirectoryName(fullSolutionPath)!;

        BaselineIndex baselines = BaselineStore.LoadForModel(model, solutionDirectory);

        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(fullSolutionPath);
        IReadOnlyCollection<string>? exclude = excludeProjectName is null ? null : [excludeProjectName];
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(loaded.Solution, exclude);

        CheckReport report = ArchChecker.Check(model, codebase, baselines, null);
        var byId = report.Results.ToDictionary(r => r.Rule.Id, r => r, StringComparer.Ordinal);
        return new ArchCheckRun(byId, solutionDirectory);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuild()
    {
        MsBuildBootstrap.EnsureInitialized();
    }

    private sealed record ArchCheckRun(IReadOnlyDictionary<string, RuleResult> ResultsById, string SolutionDirectory);
}