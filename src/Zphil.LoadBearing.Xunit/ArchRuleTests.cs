using System.Runtime.CompilerServices;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Xunit;

/// <summary>
///     The xUnit adapter: derive a sealed test class from
///     <c>ArchRuleTests&lt;YourSpec&gt;</c>, point <see cref="SolutionPath" /> at the solution to check
///     (<see cref="FindSolutionUp" /> resolves it by name when the solution is not copied to the test
///     output), and every post-desugar rule in the spec becomes its own named test — the rule ID
///     <em>is</em> the test's display name, so a failing architecture rule reads as a failing test in the
///     test explorer.
///     A failing rule's message is the exact CLI human block (<see cref="HumanReportRenderer.RuleBlock" />),
///     a Quarantine tripwire (no diff context in a test run) is reported as skipped, and everything else passes.
///     A workspace that fails to load completely fails one named test — <see cref="Workspace_LoadedCompletely" />,
///     carrying the load diagnostics — and every rule case skips rather than pass against a partial model.
///     Override <see cref="AllowWorkspaceDiagnostics" /> to opt into checking the partial model as it loaded.
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
    /// <summary>The solution (<c>.sln</c>/<c>.slnx</c>) to check the spec against.</summary>
    protected abstract string SolutionPath { get; }

    /// <summary>
    ///     The spec's own project, when the spec is a solution member — the seed of the checked universe's
    ///     exclusion (mirrors the CLI's spec-member exclusion). That project and the private plumbing only it
    ///     references are dropped; projects the solution file declares stay in, even when the spec references
    ///     them, because those are the code under law. Defaults to the spec assembly's name; override to
    ///     <see langword="null" /> when the spec lives outside the target solution.
    /// </summary>
    protected virtual string? ExcludeProjectName => typeof(TSpec).Assembly.GetName().Name;

    /// <summary>
    ///     Opts the rule tests into a partially-loaded workspace — the adapter's spelling of the CLI's
    ///     <c>--allow-workspace-diagnostics</c>. By default a load failure fails
    ///     <see cref="Workspace_LoadedCompletely" /> and skips every rule case, because a rule whose subject
    ///     lived in an unloaded project selects nothing and an empty subject passes — a green run against a
    ///     partial model signs a verdict that was never reached. With <see langword="true" />, rule verdicts
    ///     come from the partial model as it loaded, and <see cref="Workspace_LoadedCompletely" /> skips
    ///     rather than pass under a name that would then be false.
    /// </summary>
    protected virtual bool AllowWorkspaceDiagnostics => false;

    /// <summary>
    ///     Resolves a solution file's absolute path by name, for the usual case where the solution is not
    ///     copied to the test output directory. Climbs from <see cref="AppContext.BaseDirectory" /> through
    ///     its ancestors (the start directory included) and returns the full path of the first one holding a
    ///     file named <paramref name="fileName" />, so a consumer writes
    ///     <c>SolutionPath =&gt; FindSolutionUp("MyApp.slnx")</c> rather than hand-rolling the walk.
    /// </summary>
    /// <param name="fileName">The solution file name to locate (for example, <c>MyApp.slnx</c>).</param>
    /// <returns>The absolute path to the located file.</returns>
    /// <exception cref="FileNotFoundException">
    ///     No ancestor of <see cref="AppContext.BaseDirectory" /> holds a file named
    ///     <paramref name="fileName" />; the message names both the file and the start directory.
    /// </exception>
    protected static string FindSolutionUp(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{fileName}' walking up from '{AppContext.BaseDirectory}'.", fileName);
    }

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
    ///     One rule's verdict from the shared check run: a Quarantine tripwire (no diff context) is skipped, a
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

        if (run.Diagnostics.Gates(AllowWorkspaceDiagnostics))
            Assert.Skip(IncompleteModelGate.AdapterSkipReason);

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

    /// <summary>
    ///     The named answer to a partially-loaded workspace: fails with the load diagnostics inline when a
    ///     project failed to load (the rule cases then skip — no verdict is reached against a partial model),
    ///     skips with the diagnostics when <see cref="AllowWorkspaceDiagnostics" /> opted in, and passes
    ///     silently on a complete load.
    /// </summary>
    [Fact]
    public async Task Workspace_LoadedCompletely()
    {
        ArchCheckRun run = await GetRunAsync();
        if (!run.Diagnostics.IsIncomplete) return;
        if (AllowWorkspaceDiagnostics) Assert.Skip(IncompleteModelGate.AdapterOptedIn(run.Diagnostics));
        Assert.Fail(IncompleteModelGate.AdapterRefusal(run.Diagnostics));
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
    // before MSBuildLocator registration, so a consumer needs no ModuleInitializer. The check itself is the
    // shared ArchCheckSequence, so the adapter and the CLI cannot disagree about it; what the adapter brings
    // is its own extraction — a cold one-shot workspace, opened inside the delegate so the sequence's
    // baselines-before-extraction ordering covers the load too.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<ArchCheckRun> RunPipelineAsync(string solutionPath, string? excludeProjectName)
    {
        EnsureMsBuild();

        ArchitectureModel model = ArchModelBuilder.Build(new TSpec());
        string fullSolutionPath = Path.GetFullPath(solutionPath);
        string solutionDirectory = Path.GetDirectoryName(fullSolutionPath)!;

        var diagnostics = new List<string>();
        LoadedSolution? opened = null;
        try
        {
            CheckReport report = await ArchCheckSequence.ExecuteAsync(
                model, model.Rules, solutionDirectory,
                async ct =>
                {
                    LoadedSolution loaded = await WorkspaceLoader.LoadAsync(fullSolutionPath, diagnostics.Add, ct);
                    opened = loaded; // handed out so the finally below disposes it whatever the walk does

                    // The same closure the CLI applies: the spec project plus the plumbing only it references,
                    // with the solution's declared members subtracted so a spec that references the code it
                    // governs never excludes it.
                    var exclude = excludeProjectName is null
                        ? null
                        : SpecExclusion.Compute(loaded.Solution, fullSolutionPath, excludeProjectName);
                    return await CodebaseExtractor.ExtractFromSolutionAsync(loaded.Solution, exclude, ct);
                },
                null, CancellationToken.None);

            var byId = report.Results.ToDictionary(r => r.Rule.Id, r => r, StringComparer.Ordinal);
            // No merge notes: the adapter has no channel that renders them, so its diagnostics are the load
            // failures alone — which is also the only stream the gate below may ever see.
            return new ArchCheckRun(byId, solutionDirectory, new WorkspaceDiagnostics(diagnostics, []));
        }
        finally
        {
            opened?.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuild()
    {
        MsBuildBootstrap.EnsureInitialized();
    }

    private sealed record ArchCheckRun(
        IReadOnlyDictionary<string, RuleResult> ResultsById,
        string SolutionDirectory,
        WorkspaceDiagnostics Diagnostics);

    // Per-closed-generic statics are load-bearing: each ArchRuleTests<TSpec> caches ITS spec's single check
    // run (per-TSpec caching is the whole point of the design), so these must NOT be shared across TSpec.
    // ReSharper disable StaticMemberInGenericType
    private static readonly object Gate = new();

    private static Task<ArchCheckRun>? s_run;
    // ReSharper restore StaticMemberInGenericType
}
