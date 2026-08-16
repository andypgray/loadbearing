using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
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

/// <summary>The dogfood gates: LoadBearing checked against itself, and its own generated files.</summary>
/// <remarks>One class, so the workspace-heavy runs serialize. Each gate states its own contract below.</remarks>
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

    /// <summary>
    ///     This repository's own codebase, extracted once for every gate below that reads all of it: the
    ///     workspace comes from the warm pool (so the ~17-second load is shared with the rest of the suite)
    ///     and nothing is excluded, which is the call <c>graph</c> makes.
    /// </summary>
    /// <remarks>
    ///     The model is immutable and already shared assembly-wide by <see cref="WorkspaceFixture" />, so
    ///     four gates reading one instance is the established shape rather than a new risk. The one gate that
    ///     needs a <em>different</em> model — <see cref="ScopedCards_AreCurrent" />, which must exclude what
    ///     <c>render</c> excludes — extracts its own.
    /// </remarks>
    private static readonly Lazy<Task<CodebaseModel>> WholeCodebase = new(async () =>
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(
            RepoRoot.Solution, CancellationToken.None);

        return await CodebaseExtractor.ExtractFromSolutionAsync(snapshot.Solution);
    });

    /// <summary>
    ///     The CI-equivalent self-spec gate, and — on the same run — the gate on the advisory channel
    ///     beside it. <c>workspaceDiagnostics</c> is what every MCP consumer of this repo's own check
    ///     reads, the arch hook included, so it is asserted <em>empty</em>: a channel that is never empty
    ///     teaches its readers to skim, and the next real diagnostic would arrive inside noise nobody
    ///     looks at. Getting here took removing causes rather than filtering reports — the spec fixtures
    ///     reference the MyApp projects they govern, and the two anchors that only ever needed to
    ///     <em>name</em> a MyApp type now name it by fully-qualified string instead of declaring a stub
    ///     that impersonates it. Pinned as emptiness rather than a count, because the failure worth
    ///     catching is a line nobody meant to add.
    /// </summary>
    [Fact]
    public async Task SelfSpec_Check_ExitsZero()
    {
        // Loads the whole solution through MSBuildWorkspace (several seconds — an accepted cost). --json so
        // the same run answers both halves; the report goes to stdout, the human warnings still to stderr.
        CliResult result = await CliRunner.InvokeAsync(
            "check", RepoRoot.Solution, "--spec", RepoRoot.ArchSpecCsproj, "--json");

        // The assertion surfaces the CLI's own output on failure — otherwise a red self-check (e.g. the
        // Release-only spec-resolution regression) shows only "2 != 0" with no clue why, as the release run did.
        result.ShouldSucceed();

        using JsonDocument report = result.ShouldHaveJsonStdout();
        report.RootElement.GetProperty("workspaceDiagnostics")
            .EnumerateArray()
            .Select(note => note.GetString())
            .ShouldBeEmpty(
                "this repo's own check must carry no advisory note at all — a duplicate declaration here is " +
                "a layout mistake, not background noise, and this channel is the product's own front door.");
    }

    [Fact]
    public void AgentsMd_IsCurrent()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        string composed = AgentContextRenderer.RootBlock(model, SpecName);
        string committed = File.ReadAllText(RepoRoot.AgentsMd);

        // Exactly one marker pair (ExtractBody throws on any other count), and its body is current.
        MarkerPairCount(committed)
            .ShouldBe(1);
        ManagedBlock.ExtractBody(committed)
            .ShouldBe(composed);
    }

    [Fact]
    public async Task ArchitectureMd_IsCurrent()
    {
        // The shared no-exclusions model: the diagram and the survey are two renderings of one codebase and
        // must never disagree about what is in it.
        CodebaseModel codebase = await WholeCodebase.Value;

        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        string composed = DiagramComposer.Compose(
            summary, Path.GetFileName(RepoRoot.Solution), model, SpecName, new DiagramScope(ShippingProjects, []));
        string committed = File.ReadAllText(RepoRoot.ArchitectureMd);

        MarkerPairCount(committed)
            .ShouldBe(1);
        ManagedBlock.ExtractBody(committed)
            .ShouldBe(composed);
    }

    /// <summary>
    ///     The shape guard behind <see cref="ArchitectureMd_IsCurrent" />, and the cheaper half: no
    ///     workspace, just the committed file. The block is supposed to carry two drawings of the same
    ///     system — the codebase survey drawn from what exists, and the architecture law drawn from the
    ///     spec — and the composer is the only thing that puts them side by side. Rewire the render or the
    ///     drift gate back to <see cref="GraphDiagramRenderer" /> alone and the committed artifact loses a
    ///     fence, which the equality gate would catch only after someone re-rendered. This one names the
    ///     missing half directly.
    /// </summary>
    [Fact]
    public void ArchitectureMd_CarriesBothFences()
    {
        string? body = ManagedBlock.ExtractBody(File.ReadAllText(RepoRoot.ArchitectureMd));
        body.ShouldNotBeNull("ARCHITECTURE.md carries no managed block.");

        Occurrences(body, "```mermaid")
            .ShouldBe(2, "the managed block must carry both drawings.");
        body.ShouldContain("accTitle: Codebase survey:");
        body.ShouldContain("accTitle: Architecture law:");
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
        SpecResolution resolution = SpecResolver.Resolve(
            snapshot.Solution, RepoRoot.Solution, RepoRoot.ArchSpecCsproj, WorkspaceDiagnostics.None);
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, resolution.ExcludeProjectNames, snapshot.TargetFrameworks,
            TestContext.Current.CancellationToken);

        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        ContextComposition composition = ContextFileComposer.Compose(model, codebase, RepoRoot.Directory, SpecName);

        // A skip warning means a declared layer or scope matched no type — a spec that no longer describes
        // this codebase, and a card silently not written.
        composition.Warnings.ShouldBeEmpty();

        foreach (ContextFile file in composition.Files)
        {
            string relative = PathFormat.Relative(RepoRoot.Directory, file.Path);
            File.Exists(file.Path)
                .ShouldBeTrue($"{relative} is not committed; run `loadbearing render`.");

            string committed = File.ReadAllText(file.Path);
            MarkerPairCount(committed)
                .ShouldBe(1, relative);
            ManagedBlock.ExtractBody(committed)
                .ShouldBe(file.Body, relative);
        }

        CommittedContextFiles()
            .ShouldBe(
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

    /// <summary>
    ///     The sanctioned-broad-catcher pin: the discipline that holds an advisory channel empty by test,
    ///     applied to an exemption list. <c>exceptions/no-swallowed-broad-catches</c> carves seven type names
    ///     out of its own subject, and an exemption list is exactly the kind of thing that grows by one name at
    ///     a time until it means nothing. Pinning it to the exact seven makes every addition a deliberate act:
    ///     the list can only grow by moving this assertion in the same commit, where a reviewer sees the name
    ///     and the reason together.
    /// </summary>
    [Fact]
    public void SanctionedBroadCatchers_AreExactlyTheSevenHoldAndContinueBoundaries()
    {
        FieldInfo? field = typeof(LoadBearingArchSpec).GetField(
            "SanctionedBroadCatchers", BindingFlags.NonPublic | BindingFlags.Static);
        field.ShouldNotBeNull("the self-spec no longer carries a SanctionedBroadCatchers set; move this pin with it.");

        var sanctioned = (HashSet<string>)field.GetValue(null)!;
        var ordered = sanctioned.OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        ordered.ShouldBe(
            [
                "ArchChecker",
                "ArchRuleTests",
                "CommandEntryPoint",
                "IdleTimeoutWatchdog",
                "ParentProcessWatcher",
                "ServerShutdown",
                "VsWhereLocator"
            ], customMessage: "a name added here leaves the broad-catch law; add it with its reason in the set's " +
                              "xmldoc, or rewrite the handler to filter or rethrow.");
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
        CodebaseModel codebase = await WholeCodebase.Value;

        // The layer selection is taken from the built model rather than re-declared, so this pins the globs
        // the spec actually ships. layering/core-no-roslyn's subject is the bare Core layer.
        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        Selection coreLayer = model.Rules.Single(rule => rule.Id == "layering/core-no-roslyn")
            .Constraint!.Subject;
        Selection coreProject = coreLayer.Owner.Project("Zphil.LoadBearing");

        var evaluator = new SelectionEvaluator(codebase);
        var inLayer = Names(evaluator.Evaluate(coreLayer, SelectionPosition.Subject));
        var inProject = Names(evaluator.Evaluate(coreProject, SelectionPosition.Subject));

        // Asserted as two set differences rather than one list equality, because the whole Core type list
        // is ~190 names and a positional diff of it says nothing. Each direction names only the strays.
        inProject.Except(inLayer)
            .ShouldBeEmpty(
                "these Core types are in no Core-layer glob, so every rule anchored on `core` silently skips " +
                "them — add their namespace to the Core layer in LoadBearingArchSpec.");
        inLayer.Except(inProject)
            .ShouldBeEmpty(
                "these types match a Core-layer glob but are not in the Core project, so the layer now claims " +
                "code it does not own — narrow the glob in LoadBearingArchSpec.");
    }

    /// <summary>
    ///     The live oracle for <c>.Authored()</c>'s detection contract (GRAMMAR §5.2), so the difference
    ///     between the CLI project noun and the same selection narrowed to authored types is knowable
    ///     exactly. The JSON generator carries its attribute on the generated class, and partial
    ///     declarations merge onto one symbol, so <c>LoadBearingJsonContext</c> lands on the generated side
    ///     even though its declaration is hand-written — the attribute-on-the-generated-class arm of the
    ///     partial-type rule, which this project was the first to work and
    ///     <see cref="RoslynProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes" /> now works too. That
    ///     test carries the other arm as well, <c>[GeneratedRegex]</c>'s attribute-on-the-generated-method
    ///     shape, which left the CLI with <c>NuGetAuditDiagnostics</c> when the incomplete-model gate moved
    ///     down to the Roslyn project. Asserted as an equality rather than a containment, because a contract
    ///     that quietly took one authored type with it would be a worse failure than one that missed a
    ///     generated one.
    /// </summary>
    [Fact]
    public async Task CliProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes()
    {
        CodebaseModel codebase = await WholeCodebase.Value;

        var arch = new Arch();
        Selection cliProject = arch.Project("Zphil.LoadBearing.Cli");

        var evaluator = new SelectionEvaluator(codebase);
        var declared = Names(evaluator.Evaluate(cliProject, SelectionPosition.Subject));
        var authored = Names(evaluator.Evaluate(cliProject.Authored(), SelectionPosition.Subject));

        declared.Except(authored)
            .ShouldBe(["Zphil.LoadBearing.Cli.Rendering.LoadBearingJsonContext"]);

        // The synthesized top-level-statements entry point is the nearest thing this project has to a type
        // nobody typed, and no generator emitted it — so it must survive the narrowing.
        authored.ShouldContain("Program");
    }

    /// <summary>
    ///     The same contract against the same real solution, on a project that carries <em>both</em> arms of
    ///     the rule at once. <c>[GeneratedRegex]</c> carries its attribute on the generated <em>method</em>,
    ///     so <c>NuGetAuditDiagnostics</c> — the author's own partial class, which the generator completes —
    ///     stays authored while the three types each regex emits, plus the single <c>Utilities</c> class they
    ///     share, do not. Two of every three are nested and carry no attribute of their own, which is what
    ///     makes the containing-type walk load-bearing rather than defensive; the <c>_N</c> suffix is the
    ///     generator's declaration index within the file, so reordering the partial methods renumbers them and
    ///     moves this pin with them. Beside them sits the attribute-on-the-generated-<em>class</em> shape:
    ///     <c>ManifestJsonContext</c>, whose declaration is hand-written and whose partial halves merge onto
    ///     one symbol, lands on the generated side exactly as the CLI's <c>LoadBearingJsonContext</c> does
    ///     (<see cref="CliProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes" />). Both live here rather
    ///     than only in <c>GeneratedTypeExtractionTests</c>' synthetic generator because a hand-written
    ///     generator can only prove the rule as its author understood it, and the shapes these arms exist for
    ///     are ones the real toolchain emits.
    /// </summary>
    [Fact]
    public async Task RoslynProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes()
    {
        CodebaseModel codebase = await WholeCodebase.Value;

        var arch = new Arch();
        Selection roslynProject = arch.Project("Zphil.LoadBearing.Roslyn");

        var evaluator = new SelectionEvaluator(codebase);
        var declared = Names(evaluator.Evaluate(roslynProject, SelectionPosition.Subject));
        var authored = Names(evaluator.Evaluate(roslynProject.Authored(), SelectionPosition.Subject));

        declared.Except(authored)
            .ShouldBe(
            [
                "System.Text.RegularExpressions.Generated.AuditCode_1",
                "System.Text.RegularExpressions.Generated.AuditCode_1.RunnerFactory",
                "System.Text.RegularExpressions.Generated.AuditCode_1.RunnerFactory.Runner",
                "System.Text.RegularExpressions.Generated.AuditText_0",
                "System.Text.RegularExpressions.Generated.AuditText_0.RunnerFactory",
                "System.Text.RegularExpressions.Generated.AuditText_0.RunnerFactory.Runner",
                "System.Text.RegularExpressions.Generated.Utilities",
                "Zphil.LoadBearing.Roslyn.Caching.ManifestJsonContext"
            ], ignoreOrder: true);

        // The type the generator completed rather than emitted. Its own declaration is hand-written, the
        // attribute landed on the method, and the two parts merge onto one symbol — so it must survive the
        // narrowing, and an equality above is what proves it was not quietly taken along.
        authored.ShouldContain("Zphil.LoadBearing.Roslyn.NuGetAuditDiagnostics");
    }

    /// <summary>
    ///     The live oracle for the signal the built-output search refuses an intermediate assembly with.
    ///     Every probe test <em>fabricates</em> <c>CompilationOutputInfo.AssemblyPath</c>; nothing else in the
    ///     suite proves Roslyn populates it. If it ever stops — a Workspaces change, a BuildHost that no
    ///     longer reads <c>@(IntermediateAssembly)</c> — the refusal degrades to a silent no-op with every
    ///     other test still green, and this is the only gate that can catch it. Asserted as three facts per
    ///     project (present, rooted, and not the output path) because a path that is merely non-empty could
    ///     be the bin-side one, which would exclude the real output instead of the intermediate.
    /// </summary>
    [Fact]
    public async Task CompilationOutputInfo_CarriesARootedIntermediateAssemblyForEveryCSharpProject()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(
            RepoRoot.Solution, TestContext.Current.CancellationToken);

        var cSharpProjects = snapshot.Solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .ToList();

        // The guard against a vacuous pass: an empty project set satisfies every claim below.
        cSharpProjects.ShouldNotBeEmpty();

        var unusable = cSharpProjects
            .Where(project => !IsUsableIntermediateAssemblyPath(project))
            .Select(project => $"{project.Name}: '{project.CompilationOutputInfo.AssemblyPath}'")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        unusable.ShouldBeEmpty(
            "these projects carry no usable intermediate assembly path, so the built-output search has " +
            "nothing to refuse an obj-side result with — it would silently answer with one.");
    }

    private static bool IsUsableIntermediateAssemblyPath(Project project)
    {
        string? assemblyPath = project.CompilationOutputInfo.AssemblyPath;
        return !string.IsNullOrWhiteSpace(assemblyPath)
               && Path.IsPathRooted(assemblyPath)
               && !string.Equals(assemblyPath, project.OutputFilePath, PathComparison.Comparison);
    }

    private static IReadOnlyList<string> Names(IEnumerable<TypeNode> types)
    {
        return types.Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
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
