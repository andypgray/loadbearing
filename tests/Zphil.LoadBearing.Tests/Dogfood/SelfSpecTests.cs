using Shouldly;
using Xunit;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Dogfood;

/// <summary>
///     The dogfood gates. One class so the workspace-heavy runs serialize.
///     <see cref="SelfSpec_Check_ExitsZero" /> is the CI-equivalent self-spec gate: LoadBearing checks
///     itself and passes. <see cref="AgentsMd_IsCurrent" /> is the provably-current gate, no workspace
///     needed: it composes the root block in-process and asserts the committed <c>AGENTS.md</c>'s single
///     managed block equals it exactly — the product thesis in one test.
///     <see cref="ArchitectureMd_IsCurrent" /> is the same gate over the rendered diagram, and unlike its
///     pure-spec sibling it does need a workspace: the diagram is drawn from the codebase rather than from
///     the spec, so the solution has to be loaded and extracted to compose it.
///     <see cref="ScopedCards_AreCurrent" /> closes the class: every per-directory card this repo commits,
///     gated as a class rather than one file at a time.
/// </summary>
[Collection("Serial")]
public sealed class SelfSpecTests
{
    /// <summary>
    ///     The spec name the provenance line carries — the spec assembly's file name without extension,
    ///     which is what <c>render</c> derives from the resolved DLL path.
    /// </summary>
    private const string SpecName = "Zphil.LoadBearing.ArchSpec";

    /// <summary>
    ///     Where this solution's own render can place a context file. <c>examples/</c> is deliberately
    ///     absent: those <c>AGENTS.md</c>s belong to the example solutions' own specs and are gated by the
    ///     examples job's own zero-diff. Everything else under the repo is fair game for the orphan check.
    /// </summary>
    private static readonly string[] ContextFileRoots = ["src", "arch", "tests"];

    /// <summary>
    ///     The projects the committed diagram draws: the four shipping packages, the rule pack, and the
    ///     spec project. An allow-list rather than a deny-list, because the fixture projects outnumber
    ///     these two to one and an allow-list keeps the committed artifact stable while they churn. The
    ///     cost is that a genuinely new shipping project stays off the diagram until someone adds it here,
    ///     which no gate can catch.
    /// </summary>
    private static readonly string[] ShippingProjects =
    [
        "Zphil.LoadBearing",
        "Zphil.LoadBearing.Cli",
        "Zphil.LoadBearing.Roslyn",
        "Zphil.LoadBearing.Xunit",
        "Zphil.LoadBearing.Packs.DotNet",
        "Zphil.LoadBearing.ArchSpec"
    ];

    [Fact]
    public async Task SelfSpec_Check_ExitsZero()
    {
        // Loads the whole solution through MSBuildWorkspace (several seconds — an accepted cost).
        CliResult result = await CliRunner.InvokeAsync("check", RepoRoot.Solution, "--spec", RepoRoot.ArchSpecCsproj);

        // Surface the CLI's own output on failure — otherwise a red self-check (e.g. the Release-only
        // spec-resolution regression) shows only "2 != 0" with no clue why, as the release run did.
        result.Exit.ShouldBe(0, $"check exited {result.Exit}.\nstderr:\n{result.Err}\nstdout:\n{result.Out}");
    }

    [Fact]
    public void AgentsMd_IsCurrent()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        string composed = AgentContextRenderer.RootBlock(model, SpecName);
        string committed = File.ReadAllText(RepoRoot.AgentsMd);

        // Exactly one marker pair (ExtractBody throws on any other count), and its body is current.
        MarkerPairCount(committed).ShouldBe(1);
        ManagedBlock.ExtractBody(committed).ShouldBe(composed);
    }

    [Fact]
    public async Task ArchitectureMd_IsCurrent()
    {
        // Loads the whole solution through MSBuildWorkspace (several seconds — an accepted cost). The
        // extraction excludes nothing, which is the call `graph` makes: the diagram and the survey are two
        // renderings of one codebase and must never disagree about what is in it.
        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(RepoRoot.Solution);
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(loaded.Solution);

        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string composed = GraphDiagramRenderer.Block(
            summary, Path.GetFileName(RepoRoot.Solution), new DiagramScope(ShippingProjects, []));
        string committed = File.ReadAllText(RepoRoot.ArchitectureMd);

        MarkerPairCount(committed).ShouldBe(1);
        ManagedBlock.ExtractBody(committed).ShouldBe(composed);
    }

    /// <summary>
    ///     The card class, gated as a class. Composes every context file a <c>render</c> of this repo would
    ///     write — through the very composer the command uses, so the gate cannot drift from it — and
    ///     asserts two things: each composed body is byte-equal to what is committed, and no committed card
    ///     exists that the composer did not produce. The second half is the one a per-file assertion can
    ///     never make: a card orphaned by a spec change stays committed, stays read by agents, and nothing
    ///     would ever look at it again.
    /// </summary>
    [Fact]
    public async Task ScopedCards_AreCurrent()
    {
        // Through the warm pool, sharing ArchitectureMd_IsCurrent's load. Unlike that test, the extraction
        // excludes what `render` excludes — the spec project and the plumbing only it references — because
        // here the extraction decides card placement, and a card must land where the command puts it.
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(
            RepoRoot.Solution, TestContext.Current.CancellationToken);
        SpecResolution resolution = SpecResolver.Resolve(snapshot.Solution, RepoRoot.Solution, RepoRoot.ArchSpecCsproj);
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, resolution.ExcludeProjectNames, TestContext.Current.CancellationToken);

        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        ContextComposition composition = ContextFileComposer.Compose(model, codebase, RepoRoot.Directory, SpecName);

        // A skip warning means a declared layer or scope matched no type — a spec that no longer describes
        // this codebase, and a card silently not written.
        composition.Warnings.ShouldBeEmpty();

        foreach (ContextFile file in composition.Files)
        {
            string relative = PathFormat.Relative(RepoRoot.Directory, file.Path);
            File.Exists(file.Path).ShouldBeTrue($"{relative} is not committed; run `loadbearing render`.");

            string committed = File.ReadAllText(file.Path);
            MarkerPairCount(committed).ShouldBe(1, relative);
            ManagedBlock.ExtractBody(committed).ShouldBe(file.Body, relative);
        }

        CommittedContextFiles().ShouldBe(
            composition.Files.Select(file => Path.GetFullPath(file.Path)),
            ignoreOrder: true,
            "a committed AGENTS.md that no placement produces is an orphan; delete it or restore the rule that placed it.");
    }

    /// <summary>
    ///     The verb ledger's completeness pin, and the one gate here that needs neither a workspace nor
    ///     the codebase. The self-spec's xmldoc claims to exercise the verb families this codebase can
    ///     honestly exercise, and to name every remaining one with a reason — a claim that was false when
    ///     it was written and would rot again the moment a verb shipped. So it is enforced instead of
    ///     asserted: reflect the public <c>Must*</c> surface, subtract what the built model actually uses,
    ///     and require every remainder to be named in the spec's own source. A new verb that lands with
    ///     neither a self-use nor a ledger line turns this red.
    /// </summary>
    [Fact]
    public void VerbLedger_AccountsForEveryUnusedVerb()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        string ledger = File.ReadAllText(RepoRoot.ArchSpecSource);

        var used = model.Rules
            .Where(rule => rule.Constraint is not null)
            .Select(rule => VerbName(rule.Constraint!.GetType()))
            .ToHashSet(StringComparer.Ordinal);

        // The `<c>Verb</c>` needle rather than a bare substring: `Must` is a prefix of every other verb,
        // so an unbounded search would let one mention account for all of them.
        var unaccounted = PublicVerbs()
            .Where(verb => !used.Contains(verb))
            .Where(verb => !ledger.Contains($"<c>{verb}</c>", StringComparison.Ordinal))
            .ToList();

        unaccounted.ShouldBeEmpty(
            "these verbs are neither used by the self-spec nor named in its ledger — use them on this " +
            "repo's real code, or add a line to the ledger saying plainly why not.");
    }

    /// <summary>Every <c>Must*</c> verb on Core's public surface, by name.</summary>
    private static IReadOnlyList<string> PublicVerbs()
    {
        return typeof(Arch).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods())
            .Select(method => method.Name)
            .Where(name => name.StartsWith("Must", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    // A constraint node's type name is its verb plus the `Constraint` suffix, with the member-level twins
    // carrying a `Member` prefix (MemberMustHaveSuffixConstraint is MustHaveSuffix on a member subject).
    private static string VerbName(Type constraintType)
    {
        string name = constraintType.Name;
        if (name.EndsWith("Constraint", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - "Constraint".Length);
        if (name.StartsWith("Member", StringComparison.Ordinal))
            name = name.Substring("Member".Length);

        return name;
    }

    /// <summary>
    ///     The Core layer's completeness pin. Core is declared as an explicit list of namespace globs,
    ///     because its root namespace is this repository's root namespace and a <c>Zphil.LoadBearing.*</c>
    ///     subtree would swallow every other project. That list is a hand-maintained duplicate of a fact
    ///     the build already knows — project membership — and a new Core namespace nobody adds to it would
    ///     escape every rule anchored on <c>core</c>, silently and with every rule still green. So the two
    ///     type sets are asserted equal: a <c>Project</c> subject means membership, a <c>Layer</c> subject
    ///     means namespace match, and here they must name the same types.
    /// </summary>
    [Fact]
    public async Task CoreLayer_MatchesTheCoreProject()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(
            RepoRoot.Solution, TestContext.Current.CancellationToken);
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, ct: TestContext.Current.CancellationToken);

        // The layer selection is taken from the built model rather than re-declared, so this pins the globs
        // the spec actually ships. layering/core-no-roslyn's subject is the bare Core layer.
        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        Selection coreLayer = model.Rules.Single(rule => rule.Id == "layering/core-no-roslyn").Constraint!.Subject;
        Selection coreProject = coreLayer.Owner.Project("Zphil.LoadBearing");

        var evaluator = new SelectionEvaluator(codebase);
        var inLayer = Names(evaluator.Evaluate(coreLayer, SelectionPosition.Subject));
        var inProject = Names(evaluator.Evaluate(coreProject, SelectionPosition.Subject));

        // Asserted as two set differences rather than one list equality, because the whole Core type list
        // is ~190 names and a positional diff of it says nothing. Each direction names only the strays.
        inProject.Except(inLayer).ShouldBeEmpty(
            "these Core types are in no Core-layer glob, so every rule anchored on `core` silently skips " +
            "them — add their namespace to the Core layer in LoadBearingArchSpec.");
        inLayer.Except(inProject).ShouldBeEmpty(
            "these types match a Core-layer glob but are not in the Core project, so the layer now claims " +
            "code it does not own — narrow the glob in LoadBearingArchSpec.");
    }

    private static IReadOnlyList<string> Names(IEnumerable<TypeNode> types)
    {
        return types.Select(type => type.FullName).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    // Every AGENTS.md this solution's render could own: the root one plus whatever sits under the source
    // roots. Build output is skipped — a copied artifact under bin/ or obj/ is not a committed card.
    private static IReadOnlyList<string> CommittedContextFiles()
    {
        var files = new List<string> { Path.GetFullPath(RepoRoot.AgentsMd) };
        foreach (string root in ContextFileRoots)
        {
            string directory = Path.Combine(RepoRoot.Directory, root);
            if (!Directory.Exists(directory)) continue;

            files.AddRange(Directory
                .EnumerateFiles(directory, ContextFileComposer.FileName, SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Select(Path.GetFullPath));
        }

        return files;
    }

    private static bool IsBuildOutput(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");
    }

    private static int MarkerPairCount(string text)
    {
        int begins = Occurrences(text, ManagedBlock.BeginMarker);
        int ends = Occurrences(text, ManagedBlock.EndMarker);
        return begins == ends ? begins : -1;
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (int index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}