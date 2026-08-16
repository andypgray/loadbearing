using System.Runtime.CompilerServices;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
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
/// </summary>
/// <remarks>
///     <para>
///         A failing rule's message is the exact CLI human block (<see cref="HumanReportRenderer.RuleBlock" />),
///         a Quarantine tripwire (no diff context in a test run) is reported as skipped, and everything else
///         passes. A workspace that fails to load completely fails one named test —
///         <see cref="Workspace_LoadedCompletely" />, carrying the load diagnostics — and every rule case skips
///         rather than pass against a partial model. Override <see cref="AllowWorkspaceDiagnostics" /> to opt
///         into checking the partial model as it loaded. A solution filter is the opposite case — a smaller
///         model rather than a wrong one — so the rule cases keep their verdicts and only
///         <see cref="Workspace_LoadedCompletely" /> skips.
///     </para>
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
    /// <summary>
    ///     The solution (<c>.sln</c>/<c>.slnx</c>) to check the spec against, or a <c>.slnf</c> filter over
    ///     one — which checks the projects it selects plus their transitive references, and so answers over
    ///     part of the solution: every rule case still reports its verdict, and
    ///     <see cref="Workspace_LoadedCompletely" /> skips naming the declared projects the run never checked.
    /// </summary>
    protected abstract string SolutionPath { get; }

    /// <summary>
    ///     The spec's own project, when the spec is a solution member — the seed of the checked universe's
    ///     exclusion (mirrors the CLI's spec-member exclusion).
    /// </summary>
    /// <remarks>
    ///     That project and the private plumbing only it references are dropped; projects the solution file
    ///     declares stay in, even when the spec references them, because those are the code under law.
    ///     Defaults to the spec assembly's name; override to <see langword="null" /> when the spec lives
    ///     outside the target solution.
    /// </remarks>
    protected virtual string? ExcludeProjectName => typeof(TSpec).Assembly.GetName().Name;

    /// <summary>
    ///     Opts the rule tests into a partially-loaded workspace — the adapter's spelling of the CLI's
    ///     <c>--allow-workspace-diagnostics</c>.
    /// </summary>
    /// <remarks>
    ///     By default a load failure fails <see cref="Workspace_LoadedCompletely" /> and skips every rule
    ///     case, because a rule whose subject lived in an unloaded project selects nothing and every other
    ///     rule was measured over a codebase missing whole projects — a run against a partial model reports
    ///     verdicts it never reached. With
    ///     <see langword="true" />, rule verdicts come from the partial model as it loaded, and
    ///     <see cref="Workspace_LoadedCompletely" /> skips rather than pass under a name that would then be
    ///     false.
    /// </remarks>
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
    ///     display name. Builds the model from the spec alone (no Roslyn).
    /// </summary>
    /// <remarks>
    ///     A spec-build failure collapses to one sentinel row so it lands red at run time — where the
    ///     pipeline rebuild rethrows the real <c>SpecValidationException</c> — rather than as a silent
    ///     discovery diagnostic.
    /// </remarks>
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
    ///     The named answer to a partially-loaded workspace: fails naming the projects that failed to load
    ///     (the rule cases then skip — no verdict is reached against a partial model), skips naming them when
    ///     <see cref="AllowWorkspaceDiagnostics" /> opted in, and passes silently on a complete load.
    /// </summary>
    /// <remarks>
    ///     A <c>.slnf</c> <see cref="SolutionPath" /> that left declared projects unchecked also skips it,
    ///     naming them (<see cref="NarrowedUniverseNotice.AdapterSkip" />). The rule cases keep reporting
    ///     there — a narrowed universe is a smaller true answer, unlike a partial model — but this test is
    ///     the completeness claim itself, and a filtered run cannot make it.
    /// </remarks>
    [Fact]
    public async Task Workspace_LoadedCompletely()
    {
        ArchCheckRun run = await GetRunAsync();
        if (run.Diagnostics.IsIncomplete)
        {
            if (AllowWorkspaceDiagnostics) Assert.Skip(IncompleteModelGate.AdapterOptedIn(run.Diagnostics));
            Assert.Fail(IncompleteModelGate.AdapterRefusal(run.Diagnostics));
        }

        // Only ever a clean load by here: a broken model outranks a small one, and it has already answered.
        if (run.Diagnostics.UncheckedProjects.Count == 0) return;

        var uncheckedProjects = NarrowedUniverseNotice.Relative(
            run.Diagnostics.UncheckedProjects, run.SolutionDirectory);
        Assert.Skip(NarrowedUniverseNotice.AdapterSkip(Path.GetFileName(run.SolutionPath), uncheckedProjects));
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
        string solutionDirectory = SolutionProjectFileParser.AnchorDirectory(fullSolutionPath);

        var diagnostics = new List<string>();
        LoadedSolution? opened = null;
        try
        {
            CheckReport report = await ArchCheckSequence.ExecuteAsync(
                model, model.Rules, fullSolutionPath, solutionDirectory,
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
                    CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(
                        loaded.Solution, exclude, loaded.TargetFrameworks, ct);
                    // The unchecked projects come out of this load, which is why they ride the extraction
                    // rather than the call: nothing above this line has opened a workspace to measure them.
                    return new ExtractedCodebase(codebase, loaded.UncheckedProjects);
                },
                null, CancellationToken.None);

            var byId = report.Results.ToDictionary(r => r.Rule.Id, r => r, StringComparer.Ordinal);
            // No merge notes: the adapter has no channel that renders them, so its diagnostics are the load
            // failures alone. The failed and unchecked projects come off the load itself — null only where no
            // load happened, which is also the case where there is nothing to have failed or skipped.
            return new ArchCheckRun(
                byId, solutionDirectory, fullSolutionPath,
                new WorkspaceDiagnostics(
                    diagnostics, [], opened?.FailedProjects ?? [], opened?.UncheckedProjects ?? [],
                    opened?.RestoreFailedProjects ?? []));
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
        string SolutionPath,
        WorkspaceDiagnostics Diagnostics);

    // Per-closed-generic statics are load-bearing: each ArchRuleTests<TSpec> caches ITS spec's single check
    // run (per-TSpec caching is the whole point of the design), so these must NOT be shared across TSpec.
    // ReSharper disable StaticMemberInGenericType
    private static readonly object Gate = new();

    private static Task<ArchCheckRun>? s_run;
    // ReSharper restore StaticMemberInGenericType
}
