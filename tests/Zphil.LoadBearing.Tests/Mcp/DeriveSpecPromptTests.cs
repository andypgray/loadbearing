using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp;
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
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A prompt call never resolves the binding (prompts read no solution/spec), so any working directory
    // serves; a real one keeps StartAsync's host build honest.
    private static McpServerBinding Binding => new(null, null, Directory.GetCurrentDirectory());

    [Fact]
    public async Task ListPrompts_AdvertisesDeriveSpec()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        var prompts = await harness.Client.ListPromptsAsync(cancellationToken: Ct);

        // Assert — registering the prompt advertises the capability and lists it by name.
        prompts.Select(prompt => prompt.Name).ShouldContain(ArchPrompts.DeriveSpecName);
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
        string text = message.Content.ShouldBeOfType<TextContentBlock>().Text;
        text.ShouldContain("does not infer"); // the server infers no architecture
        text.ShouldContain("do not guess"); // the curation gate belongs to the human
        text.ShouldContain("data, not failures"); // mid-derive reds are the evidence pass working
        text.ShouldContain("by the human"); // the ratchet steps are human-owned...
        text.ShouldContain("baseline --init"); // ...specifically this one
        text.ShouldContain("implementation type"); // BoundaryOnlyVia must list the facade impl
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
        string text = ((TextContentBlock)result.Messages[0].Content).Text;
        text.ShouldContain("emptySubject");
        text.ShouldContain("This rule is inert: its target selection matched no types.");
        text.ShouldContain("trailing `.*`");
        text.ShouldContain("^[a-z0-9-]+(/[a-z0-9-]+)*$");
    }

    [Fact]
    public void DeriveSpec_LoadsEmbeddedRecipe_NonTrivial()
    {
        // A rename of the .md or its manifest id would otherwise surface only when a client calls
        // prompts/get; this load-time assertion turns manifest-id drift into a test failure instead.
        ArchPrompts.DeriveSpec().Length.ShouldBeGreaterThan(500);
    }
}