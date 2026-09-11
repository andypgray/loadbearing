using System.Runtime.CompilerServices;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Checking;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Roslyn.MsBuild;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Xunit;

/// <summary>
///     The xUnit adapter: derive a sealed class from <c>ArchRuleTests&lt;YourSpec&gt;</c> in your test
///     project, override <see cref="SolutionPath" /> to name the solution to check, and every rule in
///     the spec runs as its own named test. The rule ID is the test's display name — a scope
///     contributing <c>{scope-id}/containment</c> and <c>{scope-id}/tripwire</c> — so a broken
///     architecture rule reads as a failing test in the test explorer.
///     <see cref="SolutionPath" /> is the only required override.
/// </summary>
/// <remarks>
///     The adapter never builds and never restores: build the target solution first, or the verdicts
///     are stale. It needs a .NET SDK on the test host, since it loads the solution through MSBuild,
///     and it registers MSBuild itself, so your test project needs no <c>[ModuleInitializer]</c> of
///     its own.
/// </remarks>
/// <typeparam name="TSpec">The architecture spec to check — must be default-constructible.</typeparam>
public abstract class ArchRuleTests<TSpec> where TSpec : IArchitectureSpec, new()
{
    /// <summary>
    ///     Gets the solution to check the spec against: a <c>.sln</c> or <c>.slnx</c> file, or a
    ///     <c>.slnf</c> filter over one, which checks the projects the filter selects plus everything they
    ///     reference; <see cref="Workspace_LoadedCompletely" /> then skips, and so does any rule whose whole
    ///     subject the filter left out. The only required override. A relative path resolves against the
    ///     test process's current directory, which is not where a solution normally sits, so
    ///     <see cref="FindSolutionUp" /> covers the usual case where the solution is not copied to the test
    ///     output.
    /// </summary>
    protected abstract string SolutionPath { get; }

    /// <summary>
    ///     Gets the name of the spec's own project, which is left out of the code being checked along with
    ///     any project only it pulls in — a rule pack, say, or a helper library the solution does not
    ///     declare. Projects the solution file declares stay in even when the spec references them, so a
    ///     spec may govern the very code it compiles against. Defaults to the spec assembly's name;
    ///     override it to <see langword="null" /> when the spec lives outside the checked solution.
    /// </summary>
    protected virtual string? ExcludeProjectName => typeof(TSpec).Assembly.GetName().Name;

    /// <summary>
    ///     Gets whether the rule tests may report verdicts from a solution that did not load completely —
    ///     the adapter's form of the CLI's <c>--allow-workspace-diagnostics</c>. False by default: a load
    ///     failure fails <see cref="Workspace_LoadedCompletely" /> and every rule case skips, because a
    ///     rule measured over a codebase missing whole projects reports a verdict it never reached.
    ///     Override it to <see langword="true" /> to take the verdicts the partial model does support;
    ///     <see cref="Workspace_LoadedCompletely" /> then skips, still naming the projects that failed,
    ///     rather than pass under a name the run cannot vouch for.
    /// </summary>
    protected virtual bool AllowWorkspaceDiagnostics => false;

    /// <summary>
    ///     Resolves a solution file's absolute path by name, for the usual case where the solution is not
    ///     copied to the test output directory. Climbs from <see cref="AppContext.BaseDirectory" /> through
    ///     its ancestors, the start directory included, and returns the full path of the first one holding
    ///     a file named <paramref name="fileName" />, so a consumer writes
    ///     <c>SolutionPath =&gt; FindSolutionUp("MyApp.slnx")</c> rather than hand-rolling the climb.
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
    ///     The row source behind <see cref="Rule_Holds" />: one row per rule in the spec, each carrying
    ///     the rule ID that becomes the test's display name. Runs at test discovery and builds the model
    ///     from the spec alone, so the test explorer can list the rules without loading a solution. The
    ///     load, the extraction and the check then run once for each spec type, when its first case
    ///     executes, and every rule case of that type reads its verdict from that one run. A spec that
    ///     fails to build collapses to a single row, which fails when the run reaches it and carries the
    ///     spec's own errors. The test framework calls this; there is nothing to override.
    /// </summary>
    // The sentinel row lands a spec-build failure red at run time — where the pipeline's rebuild
    // rethrows the real SpecValidationException — rather than as a discovery diagnostic no runner
    // surfaces.
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
    ///     One rule's verdict, the test named by its rule ID: it passes when the rule holds, fails with
    ///     the block <c>loadbearing check</c> prints for it — the reason, the fix, and every violation
    ///     with its file and line — when it does not, and skips when the run reached no verdict, carrying
    ///     the reason. A scope's tripwire is the usual skip: it has no diff base to compare changed files
    ///     against, so a <c>Caution</c> scope, whose tripwire is its only rule, is a case that is always
    ///     skipped and never fires. Rows come from <see cref="RuleRows" />.
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
    ///     The named answer to a solution that did not load completely: it fails, naming the projects that
    ///     failed to load, and every rule case then skips rather than report a verdict reached over a
    ///     partial model; it skips, naming the same projects, when
    ///     <see cref="AllowWorkspaceDiagnostics" /> opted into checking the model as it loaded; and it
    ///     passes silently on a clean load. It also skips on the two runs that are smaller rather than
    ///     wrong — one through a <c>.slnf</c> solution filter, one over a solution declaring projects the
    ///     checker cannot read — naming what went unchecked, since this test is the completeness claim
    ///     itself and neither run can make it. A solution can be both at once, and then it reports one
    ///     block for each; a solution that failed to load outranks both and is reported alone.
    /// </summary>
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
        var blocks = new List<string>();

        if (run.Diagnostics.UncheckedProjects.Count > 0)
            blocks.Add(
                NarrowedUniverseNotice.AdapterSkip(
                    Path.GetFileName(run.SolutionPath),
                    NarrowedUniverseNotice.Relative(run.Diagnostics.UncheckedProjects, run.SolutionDirectory)));

        if (run.Diagnostics.UnsupportedProjects.Count > 0)
            blocks.Add(
                UnsupportedProjectsNotice.AdapterSkip(
                    UnsupportedProjectsNotice.Relative(run.Diagnostics.UnsupportedProjects, run.SolutionDirectory)));

        if (blocks.Count > 0) Assert.Skip(string.Join("\n", blocks));
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
        // Escapes the extraction delegate the way `opened` does, and for the same reason: every fact in it
        // exists only once a model has been merged, and this is the one place the adapter holds one. It is
        // composed inside the delegate rather than after the sequence returns, because the check now reads
        // the load's diagnostics too — one composition serving the checker and the completeness test is what
        // keeps them from ever describing the same load differently. Seeded with the no-load value rather
        // than null: the sequence invokes the delegate exactly once and the report below is its return value,
        // so the seed is never the value read, and a nullable here would only buy a dereference to justify.
        WorkspaceDiagnostics loadDiagnostics = WorkspaceDiagnostics.None;
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
                    // governs never excludes it. One read of that membership serves both consumers — the
                    // subtraction here, and the per-project label the extraction stamps on the model.
                    IReadOnlySet<string>? declaredMembers = SpecExclusion.TryReadDeclaredMembers(fullSolutionPath);
                    IReadOnlyCollection<string>? exclude = excludeProjectName is null
                        ? null
                        : SpecExclusion.Compute(loaded.Solution, declaredMembers, excludeProjectName);
                    CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(
                        loaded.Solution, exclude, loaded.TargetFrameworks, declaredMembers, ct);
                    // Both of the merge's two facts ride this one value, and neither is rendered: the adapter
                    // has no channel that shows them, but they are documented as one pair filled from one
                    // read. Every list comes off this load, which is why the whole value rides the extraction
                    // rather than the call: nothing above this line has opened a workspace to measure any of
                    // them.
                    var composed = new WorkspaceDiagnostics(
                        diagnostics, codebase.MergeNotes, loaded.FailedProjects, loaded.UncheckedProjects,
                        loaded.RestoreFailedProjects, loaded.UnsupportedProjects,
                        MultiTargetedProjects.Of(codebase));
                    loadDiagnostics = composed;
                    return new ExtractedCodebase(codebase, composed);
                },
                null, CancellationToken.None);

            Dictionary<string, RuleResult> byId = report.Results.ToDictionary(r => r.Rule.Id, r => r, StringComparer.Ordinal);
            return new ArchCheckRun(byId, solutionDirectory, fullSolutionPath, loadDiagnostics);
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
    private static readonly Lock Gate = new();

    private static Task<ArchCheckRun>? s_run;
    // ReSharper restore StaticMemberInGenericType
}
