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
        // misreading an empty subject or an inert target as evidence about the code.
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("emptySubject");
        text.ShouldContain("This rule is inert: its target selection matched no types.");
        text.ShouldContain("trailing `.*`");
        text.ShouldContain("^[a-z0-9-]+(/[a-z0-9-]+)*$");
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
}
