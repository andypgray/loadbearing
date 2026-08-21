using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The check report's <c>multiTargetedProjects</c> stamp end to end, over the <c>MultiTfm</c> fixture:
///     the verdict below is clean and stays clean, and the stamp is what says the verdict covers one
///     framework's view of a project that compiles twice. Without it a green report over a multi-targeting
///     estate reads as a green estate, and the branch another framework's <c>#if</c> guards was never in
///     the model to violate anything.
/// </summary>
/// <remarks>
///     The stamp is a trust statement rather than a finding, so both halves of the claim are asserted here:
///     the multi-targeted project is named with its frameworks and its winner, and a single-framework
///     solution's report carries no such key at all — which is what keeps every existing report byte-identical.
/// </remarks>
public sealed class MultiTfmCheckE2ETests
{
    private const string Core = "MultiTfm.Core";
    private const string RuleId = "layering/core-independent";

    // Pattern-only, so the spec assembly needs no reference to the fixture at all — and passing, because a
    // clean verdict is exactly the reading the stamp qualifies.
    private const string CoreIndependentSpec = """
                                               using Zphil.LoadBearing;

                                               namespace MultiTfmSpecs
                                               {
                                                   public sealed class CoreIndependentSpec : IArchitectureSpec
                                                   {
                                                       public void Define(Arch arch)
                                                       {
                                                           arch.Rule("layering/core-independent")
                                                               .Enforce(arch.Project("MultiTfm.Core").MustNotReference(arch.Namespace("MultiTfm.Web.*")))
                                                               .Because("The core must not know about the web tier.");
                                                       }
                                                   }
                                               }
                                               """;

    [Fact]
    public async Task Check_MultiTargetedProject_IsStampedWithItsFrameworksAndTheWinningOne()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        string specDll = EmitSpec("CoreIndependent");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — the rule passed, and the stamp says what that pass covers. Both are read off the one
        // document, because the point of the stamp is precisely that it qualifies this verdict.
        result.ShouldSucceed();
        result.Out.ShouldHavePassed(RuleId);

        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement stamped = Stamps(document)
            .Single();

        stamped.GetProperty("project")
            .GetString()
            .ShouldBe(Core);
        stamped.GetProperty("targetFrameworks")
            .EnumerateArray()
            .Select(framework => framework.GetString())
            .ShouldBe(["net10.0", "netstandard2.0"]);
        stamped.GetProperty("factsFollow")
            .GetString()
            .ShouldBe("net10.0");
    }

    [Fact]
    public async Task Check_MultiTargetedProject_LeavesTheVerdictWhole()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        string specDll = EmitSpec("CoreIndependentWhole");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — the stamp scopes the verdict, it never invalidates it: the model is complete, every rule
        // ran and answered, and nothing here is a load failure. Stamping modelIncomplete would tell a client
        // to distrust an answer that is entirely trustworthy about the framework it was measured against.
        using JsonDocument document = result.ShouldHaveJsonStdout();

        document.RootElement.TryGetProperty("modelIncomplete", out _)
            .ShouldBeFalse();
        document.RootElement.GetProperty("summary")
            .GetProperty("rulesChecked")
            .GetInt32()
            .ShouldBe(1);
    }

    [Fact]
    public async Task Check_SingleTargetedSolution_OmitsTheStampEntirely()
    {
        // Act — the MyApp bed, every project of which targets one framework, checked with the spec that
        // holds. This is the shape of every report the suite's goldens carry.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll, "--json");

        // Assert — absent, not an empty array: a solution with nothing to say about frameworks renders the
        // document it rendered before the key existed.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();

        document.RootElement.TryGetProperty("multiTargetedProjects", out _)
            .ShouldBeFalse();
    }

    private static IEnumerable<JsonElement> Stamps(JsonDocument document)
    {
        return document.RootElement.GetProperty("multiTargetedProjects")
            .EnumerateArray();
    }

    // A spec DLL per test, under that test's own temp root. The delete is best-effort, which is what an
    // emitted spec needs: a collectible spec ALC may not have released the file's handle by teardown
    // (unload is GC-timed), and the run root's sweep reclaims whatever a failed delete leaves.
    private static string EmitSpec(string name)
    {
        TempDirectory specTemp = TestTempRoot.Fresh($"multi-tfm-{name}");
        string specDll = specTemp.PathOf($"Zphil.LoadBearing.{name}Spec.dll");
        SpecAssemblyCompiler.EmitSpecDll(CoreIndependentSpec, specDll, $"Zphil.LoadBearing.{name}Spec");
        return specDll;
    }
}
