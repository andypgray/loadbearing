using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The check report's subject-coverage pair end to end, over the <c>RazorApp</c> fixture: a rule written
///     the way a rule author reaches for a web tier — <c>arch.Project("RazorApp.Web")</c> — states how much
///     of what it swept nobody wrote, and the same rule narrowed with <c>.Authored()</c> states nothing at
///     all.
/// </summary>
/// <remarks>
///     The pair is measured on the materialized subject set rather than on the selection that produced it,
///     which is what makes it self-extinguishing by construction: there is no rule anywhere about when to
///     suppress the statement, because a subject with no generated types has nothing to report. The two
///     specs below differ by exactly one call, so what changes between the two documents is attributable to
///     that call alone.
/// </remarks>
[Collection("Serial")]
public sealed class GeneratedSubjectCheckE2ETests
{
    private const string RuleId = "naming/web-types";

    private const string ProjectSubjectSpec = """
                                              using Zphil.LoadBearing;

                                              namespace RazorAppSpecs
                                              {
                                                  public sealed class ProjectSubjectSpec : IArchitectureSpec
                                                  {
                                                      public void Define(Arch arch)
                                                      {
                                                          arch.Rule("naming/web-types")
                                                              .Enforce(arch.Project("RazorApp.Web").MustHaveSuffix("Controller"))
                                                              .Because("Web types are named for what they are.");
                                                      }
                                                  }
                                              }
                                              """;

    private const string AuthoredSubjectSpec = """
                                               using Zphil.LoadBearing;

                                               namespace RazorAppSpecs
                                               {
                                                   public sealed class AuthoredSubjectSpec : IArchitectureSpec
                                                   {
                                                       public void Define(Arch arch)
                                                       {
                                                           arch.Rule("naming/web-types")
                                                               .Enforce(arch.Project("RazorApp.Web").Authored().MustHaveSuffix("Controller"))
                                                               .Because("Web types are named for what they are.");
                                                       }
                                                   }
                                               }
                                               """;

    [Fact]
    public async Task Check_ProjectSubjectSweepingGeneratedTypes_StatesBothNumbers()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");
        string specDll = EmitSpec(ProjectSubjectSpec, "ProjectSubject");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — both numbers, because either alone is unreadable: "2 generated" says nothing without the
        // 4 it is 2 of, and the denominator appears nowhere else in the document. Here the pair says the
        // rule is aimed at a subject half of which nobody can act on.
        result.Out.ShouldHaveSubjectCoverage(RuleId, 4, 2);
    }

    [Fact]
    public async Task Check_SubjectNarrowedToAuthored_ReportsNeitherKey()
    {
        // Arrange — the same rule, one call different.
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");
        string specDll = EmitSpec(AuthoredSubjectSpec, "AuthoredSubject");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — the statement extinguishes itself. Both keys go, never one.
        result.Out.ShouldReportNoSubjectCoverage(RuleId);
    }

    [Fact]
    public async Task Check_HumanReport_CarriesTheSubjectLineForAFailingRule()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");
        string specDll = EmitSpec(ProjectSubjectSpec, "ProjectSubjectHuman");

        // Act
        CliResult result = await CliRunner.InvokeAsync("check", fixture.SolutionPath, "--spec", specDll);

        // Assert — the terminal reader gets the same fact, sited between the rule's own framing and its list
        // of violations, so it reads as scope rather than as one more finding.
        result.Out.NormalizedLines()
            .ShouldContain("  subject: 4 types, 2 generated");
    }

    [Fact]
    public async Task Check_HumanReport_NarrowedToAuthored_SaysNothingAboutTheSubject()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/RazorApp", "RazorApp.slnx");
        string specDll = EmitSpec(AuthoredSubjectSpec, "AuthoredSubjectHuman");

        // Act
        CliResult result = await CliRunner.InvokeAsync("check", fixture.SolutionPath, "--spec", specDll);

        // Assert — no line at all rather than a line reading zero. A rule aimed squarely at code someone
        // wrote has nothing to say here, and its silence is what makes the line's presence a finding.
        result.Out.NormalizedLines()
            .ShouldNotContain("subject:");
    }

    // A spec DLL per test, under that test's own temp root. The delete is best-effort, which is what an
    // emitted spec needs: a collectible spec ALC may not have released the file's handle by teardown
    // (unload is GC-timed), and the run root's sweep reclaims whatever a failed delete leaves.
    private static string EmitSpec(string source, string name)
    {
        TempDirectory specTemp = TestTempRoot.Fresh($"generated-subject-{name}");
        string specDll = specTemp.PathOf($"Zphil.LoadBearing.{name}Spec.dll");
        SpecAssemblyCompiler.EmitSpecDll(source, specDll, $"Zphil.LoadBearing.{name}Spec");
        return specDll;
    }
}
