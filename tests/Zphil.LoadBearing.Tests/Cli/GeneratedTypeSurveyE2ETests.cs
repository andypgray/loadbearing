using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The survey's generated-type qualifier end to end, over the <c>RazorApp</c> fixture:
///     <c>Roslyn.RazorGeneratedTypeTests</c> pins the model and <c>Extraction.GraphSummarizerTests</c> the
///     counting; these pin the two documents a reader actually holds, because a count that is computed
///     correctly and rendered nowhere tells nobody what a project-anchored subject would sweep.
/// </summary>
/// <remarks>
///     Targeted assertions and no golden, deliberately. The SDK band decides what the Razor generator names
///     its output and CI runs four images, so a document pinned byte-for-byte here would red on an SDK bump
///     that changed nothing this test is about. What is asserted is the namespace, the flag and the counts
///     — every part of the claim, none of the spelling.
/// </remarks>
[Collection("Serial")]
public sealed class GeneratedTypeSurveyE2ETests
{
    private const string Web = "RazorApp.Web";
    private const string Core = "RazorApp.Core";
    private const string GeneratedNamespace = "AspNetCoreGeneratedDocument";

    [Fact]
    public async Task Graph_GeneratedTypes_AreCountedBesideTheProjectsTotal()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the pair a reader needs before writing arch.Project("RazorApp.Web"): how big the noun is,
        // and how much of it nobody wrote. The generated count is a SUBSET of types, never a population
        // beside it, which is why it qualifies that number rather than sitting in a list of its own.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement web = Project(document, Web);

        web.GetProperty("types")
            .GetInt32()
            .ShouldBe(4);
        web.GetProperty("generated")
            .GetInt32()
            .ShouldBe(2);
    }

    [Fact]
    public async Task Graph_ProjectWithNoGeneratedTypes_OmitsTheKeyEntirely()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the omit-at-zero rule, which is what keeps every survey of a generator-free solution the
        // document it was before this key existed. Asserted on the contrast project inside the same document
        // as the one above, so the two shapes cannot drift apart.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement core = Project(document, Core);

        core.GetProperty("types")
            .GetInt32()
            .ShouldBe(1);
        core.TryGetProperty("generated", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Graph_WhollyGeneratedNamespace_IsMarkedInTheNamespaceInventory()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the per-namespace half, which is the one that stops a wholly generated namespace becoming
        // a layer glob. A reader scanning the inventory for a name to anchor on sees this one is entirely
        // generator output before they reach for it.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement views = Namespace(Project(document, Web), GeneratedNamespace);

        views.GetProperty("types")
            .GetInt32()
            .ShouldBe(2);
        views.GetProperty("generated")
            .GetInt32()
            .ShouldBe(2);

        // The authored namespace beside it in the same project, so this is a distinction the document draws
        // rather than a blanket mark on everything the Web SDK compiles.
        Namespace(Project(document, Web), Web)
            .TryGetProperty("generated", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Graph_HumanSurvey_QualifiesBothTheProjectLineAndTheNamespaceEntry()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath);

        // Assert — the same two facts a terminal reader gets, so nobody is sent to --json for them. The
        // wholly generated namespace reads "all generated" rather than repeating the count it just gave.
        result.ShouldSucceed();
        string output = result.Out.NormalizedLines();

        output.ShouldContain($"  {Web} — 4 types (2 generated); targets net10.0; references: {Core}; packages: (none)");
        output.ShouldContain($"{GeneratedNamespace} (2, all generated)");
        output.ShouldContain($"  {Core} — 1 type; targets net10.0; references: (none); packages: (none)");
    }

    private static JsonElement Project(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Single(project => project.GetProperty("name")
                .GetString() == name);
    }

    private static JsonElement Namespace(JsonElement project, string name)
    {
        return project.GetProperty("namespaces")
            .EnumerateArray()
            .Single(@namespace => @namespace.GetProperty("namespace")
                .GetString() == name);
    }
}
