using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Mcp.Prompts;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     Pins the <c>derive_spec</c> MCP prompt end to end over the in-memory client/server harness: it is
///     advertised in <c>prompts/list</c> and <c>prompts/get</c> returns a single user message whose body
///     carries the recipe's load-bearing commitments — the honesty contract, the human-owned ratchet steps,
///     and the checker's authoring signals. Assertions target a few stable anchor phrases, not the whole
///     blob, so wording can evolve while those commitments cannot silently drift. A load-time guard turns a
///     renamed resource or drifted manifest id into a test failure instead of a runtime surprise on the
///     first <c>prompts/get</c>.
/// </summary>
public sealed class DeriveSpecPromptTests
{
    /// <summary>
    ///     Ceiling on the body every <c>prompts/get</c> serves, in the same UTF-16 code units
    ///     <see cref="string.Length" /> counts. Measured at 69,418 characters the day it was set, and
    ///     rounded up to the next 500.
    /// </summary>
    /// <remarks>
    ///     The recipe arrives whole at every kickoff — some 14,000 tokens before the reader has done
    ///     anything — and it has only ever grown: 39,348 characters at 0.4.0, 56,555 at 0.7.0, 69,418
    ///     today. Each of those figures was measured at a release and none of them gated anything, so
    ///     every addition so far has been free. That is what this constant changes, and the reason it is
    ///     set close rather than generously.
    ///     <see cref="DeriveSpecVocabularyTests" /> holds the other side as a floor on content — every
    ///     shipped constraint verb must be named somewhere in the body — so vocabulary can never be the
    ///     thing that gets cut here. Prose is what displaces prose.
    ///     <para>
    ///         When this reds, cut or move content before reaching for the number. Raising it is a
    ///         decision in its own right: move the constant in the same commit as the text that needed
    ///         the room, and say what it bought.
    ///     </para>
    ///     <para>
    ///         What it is not: it is not a claim that a shorter recipe derives better. Nothing here
    ///         measures the quality of a derivation, and that question is answered by running
    ///         derivations against real codebases rather than by a character count.
    ///     </para>
    /// </remarks>
    private const int ServedBodyCeiling = 69_500;

    // A prompt call never resolves the binding (prompts read no solution/spec), so any working directory
    // serves; a real one keeps StartAsync's host build honest.
    private static McpServerBinding Binding => new(null, null, Directory.GetCurrentDirectory());

    [Fact]
    public async Task ListPrompts_AdvertisesDeriveSpec()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        IList<McpClientPrompt> prompts = await harness.Client.ListPromptsAsync(cancellationToken: Ct);

        // Assert — registering the prompt advertises the capability and lists it by name.
        prompts.Select(prompt => prompt.Name)
            .ShouldContain(ArchPrompts.DeriveSpecName);
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_ReturnsSingleUserMessageCarryingLoadBearingCommitments()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — a string-returning prompt method maps to one Role.User text message.
        PromptMessage message = result.Messages.ShouldHaveSingleItem();
        message.Role.ShouldBe(Role.User);
        string text = message.Content.ShouldBeOfType<TextContentBlock>()
            .Text;
        text.ShouldContain("does not infer"); // the server infers no architecture
        text.ShouldContain("do not guess"); // the curation gate belongs to the human
        text.ShouldContain("data, not failures"); // mid-derive reds are the evidence pass working
        text.ShouldContain("by the human"); // the ratchet steps are human-owned...
        text.ShouldContain("baseline --init"); // ...specifically this one
        text.ShouldContain("implementation type"); // BoundaryOnlyVia must list the facade impl
        // The no-load boundary: a facade the spec cannot compile against is still law, and the prompt
        // must reach for that before telling an adopter to open the product project up.
        text.ShouldContain("arch.Types.Named(\"IBillingFacade\", \"BillingFacade\")");
        text.ShouldContain("rather than grandfathering it");
        // The layer recipe shows the purpose trailer and says where its sentence comes from: the
        // stated-intent prose the adopter was already sent to read in step 0.
        text.ShouldContain(".Purpose(\"Domain holds the order and customer model.\")");
        text.ShouldContain("one sentence on what the layer is for");
        // The scope recipe teaches the second scope posture and the rule that picks between the two.
        text.ShouldContain(".Caution(");
        text.ShouldContain("new callers welcome");
        // The family recipe: one rule over a partition of plugins or modules, the three family verbs, and
        // what "itself" means on a family — the cell, as declared — so the recipe never proposes one
        // rule per cell where one sentence says it. The cycle gate is taught as the law for peers whose
        // order nobody has stated, so an adopter reaches for it before writing an order they do not know.
        text.ShouldContain("arch.Each(arch.Projects.Matching(\"MyApp.Plugin.*\"))");
        text.ShouldContain("MustNotReferenceEachOther()");
        text.ShouldContain("MustOnlyBeReferencedByItself()");
        text.ShouldContain("MustNotHaveCircularReferences()");
        text.ShouldContain("but never in a circle");
        text.ShouldContain("over the family, not one per unit");
        text.ShouldContain("arch_graph"); // the survey tool
        text.ShouldContain("arch_check"); // the evidence tool
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_EmbedsAuthoringSignalAnchors()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — the checker's authoring signals must survive prose edits: they are what stop an agent
        // misreading an empty subject or an inert target as evidence about the code. The cure itself now
        // rides each signal as its `hint`, so what the recipe pins is the diagnosis, the route to the hint,
        // and the rule that ends the loop — never the glob semantics the hint carries.
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("emptySubject");
        text.ShouldContain("This rule is inert: its target selection matched no types.");
        text.ShouldContain("apply the hint rather than guessing");
        text.ShouldContain("two consecutive re-checks");
        text.ShouldContain("carry it to step 6 as `undecided`");
        text.ShouldContain("subject could not be made to match; human to resolve");
        text.ShouldContain("^[a-z0-9-]+(/[a-z0-9-]+)*$");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_ClosesWithTheFixedKeyReceipt()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — the outcome report was a prose list, so every derivation closed in a different shape and
        // nothing downstream could read one. The keys are the contract a reader or a script finds each claim
        // under, and the sentence keeping the three claims apart is what stops a spec that builds from being
        // read as a spec somebody curated, or a pass on a partial model from being read as obedience.
        string text = result.ShouldHaveTextContent();
        string[] receiptKeys =
        [
            "specBuild: ok",
            "evidencePass: complete",
            "check: exit 1, rules 31, passed 2, failed 27, skipped 2, violations 51",
            "postures: enforce",
            "leftRed:",
            "dropped:",
            "undecided:",
            "curation: pending",
            "baseline: not run (human)",
            "render: not run (human)"
        ];
        foreach (string key in receiptKeys) text.ShouldContain(key);
        text.ShouldContain("none implies another");
        text.ShouldContain("has *not observed* the rule");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_SubstitutesRunningVersionIntoScaffold()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert
        string text = result.ShouldHaveTextContent();
        text.ShouldContain($"<PackageReference Include=\"Zphil.LoadBearing\" Version=\"{ServerVersion.SemVer}\" />");
        text.ShouldNotContain(ArchPrompts.ScaffoldVersionPlaceholder);
        text.ShouldContain("`loadbearing --version` prints");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_NamesTheDnxPrefixForASessionWithNoInstalledCommand()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — the recipe names `loadbearing` eleven times and is served to a session that may have no
        // such command: the registry manifest's dnx launch runs the package without installing the tool.
        // The eleven spellings stay right for every installed reader, so what the recipe owes the other
        // population is the prefix, pinned to the running build for the same reason the scaffold's version
        // is. Bare `dnx` would not resolve in the shell an agent drives — see ServerInstructionsTests.
        string text = result.ShouldHaveTextContent();
        text.ShouldContain($"`dotnet dnx Zphil.LoadBearing.Cli@{ServerVersion.SemVer} --yes --`");
        text.ShouldNotContain(ArchPrompts.CliPackagePlaceholder);

        // The substitution is a plain replace over the whole recipe, and the product's own repository URL
        // carries the package's name too. This is what keeps it from being rewritten into a dead link.
        text.ShouldContain("https://github.com/andypgray/loadbearing/blob/main/GRAMMAR.md");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_ScaffoldOptsOutOfCentralPackageManagement()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — the scaffold pins its own version inline, which is NU1008 on a repository that manages
        // package versions centrally. The opt-out is what makes one csproj valid either way, so it cannot
        // drop out of the recipe quietly; CI executes the same commitment against a real central file.
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_WarnsThatAnArchNamespaceSegmentShadowsTheContractType()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — the recipe's own arch/ folder convention makes an Arch-suffixed project name the
        // natural reach, and neither resulting compiler error mentions this library: the recipe must
        // carry the cause (a namespace segment named Arch shadows the contract type) and the way out.
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("CS0118");
        text.ShouldContain("'Arch' is a namespace but is used like a type");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_WarnsAgainstHandTrimmingTheSolutionAddRewrite()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — on a multi-configuration solution the CLI's own rewrite dwarfs the spec project it
        // adds, which reads as damage against the recipe's one-diff close; the tidy-looking repair is a
        // hand-written Debug/Release entry that leaves the spec project unbuilt everywhere else. Both
        // halves must survive prose edits: that the rewrite is correct, and that trimming it is not.
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("rewrite more of a `.sln` than the one project it adds");
        text.ShouldContain("skips a project with no mapping for it");
        text.ShouldContain("not to be trimmed by hand");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_RoutesToTheGrammar()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        GetPromptResult result = await harness.Client.GetPromptAsync(
            ArchPrompts.DeriveSpecName, cancellationToken: Ct);

        // Assert — the authoring reference is a condensed subset of the published grammar; the recipe
        // must say where the full vocabulary lives, or an agent mid-derive has nowhere to go past the
        // subset but improvisation.
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("https://github.com/andypgray/loadbearing/blob/main/GRAMMAR.md");
    }

    [Fact]
    public void DeriveSpec_LoadsEmbeddedRecipe_NonTrivial()
    {
        // A rename of the .md or its manifest id would otherwise surface only when a client calls
        // prompts/get; this load-time assertion turns manifest-id drift into a test failure instead.
        ArchPrompts.DeriveSpec()
            .Length.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void DeriveSpec_StaysUnderTheServedBodyCeiling()
    {
        // The failure this catches is the free append: a paragraph added because it seemed worth saying,
        // to a document nothing was measuring, that every session then pays for at kickoff whether or not
        // it ever reads the paragraph.
        ArchPrompts.DeriveSpec()
            .Length.ShouldBeLessThanOrEqualTo(ServedBodyCeiling);
    }
}
