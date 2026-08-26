using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     How a project violation reads on the three report surfaces (GRAMMAR §4.10): the human block, the
///     <c>--json</c> document, and SARIF. Two texts to pin per surface — a packaging violation, which
///     names the project alone, and a per-package one, which names the package it declares.
/// </summary>
/// <remarks>
///     The renderers format independently by house convention, so the same two sentences are asserted
///     three times rather than shared through a helper. What is asserted about JSON is also the slot
///     discipline: <c>subjectProject</c> and <c>package</c> are additive and null-omitted, so a report
///     from a spec with no project rule is byte-identical to the one it was before them.
/// </remarks>
public sealed class ProjectViolationRenderTests
{
    private static readonly CodebaseModel Packable = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Internal", isPackable: true, isPackableSite: ProjectFacts.Site("Internal.csproj", 4)));

    private static readonly CodebaseModel Packaged = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Domain", packageReferences: [ProjectFacts.Package("Serilog", "Domain.csproj", 9)]));

    private static readonly string SolutionDir = Directory.GetCurrentDirectory();

    [Fact]
    public void HumanBlock_PackagingViolation_NamesTheProjectAtItsDeclaringSite()
    {
        PackableReport()
            .Single()
            .HumanBlock()
            .ShouldContain("Internal.csproj:4 — Zphil.Internal");
    }

    [Fact]
    public void HumanBlock_PerPackageViolation_NamesTheProjectAndThePackage()
    {
        PackagedReport()
            .Single()
            .HumanBlock()
            .ShouldContain("Domain.csproj:9 — Zphil.Domain references package Serilog");
    }

    [Fact]
    public void HumanBlock_UnlocatedPackagingViolation_ListsTheProjectWithNoSite()
    {
        // A fact nothing declared has nowhere to point, so the project is listed rather than dropped — the
        // same unlocated arm an empty subject and a rule error take.
        CodebaseModel siteless = ProjectFacts.Solution(ProjectFacts.Project("Zphil.Loose", isPackable: true));

        string block = Checker.Run(siteless, arch => arch.Rule("packaging/internal-not-shipped")
                .Enforce(arch.Projects.Matching("*").MustNotBePackable())
                .Because("b"))
            .Single()
            .HumanBlock();

        block.ShouldContain("  Zphil.Loose");
        block.ShouldNotContain(" — Zphil.Loose");
    }

    [Fact]
    public void Json_PackagingViolation_CarriesSubjectProjectAndOmitsThePackageSlot()
    {
        PackableReport()
            .ShouldRenderProjectShapeViolation("projectShape", "Zphil.Internal");
    }

    [Fact]
    public void Json_PerPackageViolation_CarriesBothSlots()
    {
        PackagedReport()
            .ShouldRenderProjectShapeViolation("projectShape", "Zphil.Domain", "Serilog");
    }

    [Fact]
    public void Json_ReportWithNoProjectRule_CarriesNeitherSlot()
    {
        // The additive-key claim: the two slots exist for project violations and are absent everywhere else,
        // which is what holds the schema at version 3 for every spec that has no project rule.
        CheckReport report = Checker.Run(
            "namespace App { public class Thing {} }",
            arch => arch.Rule("naming/things")
                .Enforce(arch.Namespace("App.*").MustHaveSuffix("Service"))
                .Because("b"));

        using JsonDocument document = JsonDocument.Parse(report.JsonReport());
        JsonElement violation = document.RootElement.GetProperty("rules")[0]
            .GetProperty("violations")[0];

        violation.TryGetProperty("subjectProject", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("package", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Sarif_PackagingViolation_MessageIsTheProjectName()
    {
        MessagesOf(PackableReport())
            .ShouldBe(["Zphil.Internal"]);
    }

    [Fact]
    public void Sarif_PerPackageViolation_MessageNamesTheProjectAndThePackage()
    {
        MessagesOf(PackagedReport())
            .ShouldBe(["Zphil.Domain references package Serilog"]);
    }

    private static CheckReport PackableReport()
    {
        return Checker.Run(Packable, arch => arch.Rule("packaging/internal-not-shipped")
            .Enforce(arch.Projects.Matching("*").MustNotBePackable())
            .Because("An internal project on the feed is an API nobody meant to promise."));
    }

    private static CheckReport PackagedReport()
    {
        return Checker.Run(Packaged, arch => arch.Rule("packaging/domain-pure")
            .Enforce(arch.Projects.Matching("*").MustReferenceNoPackages())
            .Because("The domain must not take a dependency the rest of the estate has to carry."));
    }

    private static IReadOnlyList<string> MessagesOf(CheckReport report)
    {
        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, [], WorkspaceDiagnostics.None);
        using JsonDocument document = JsonDocument.Parse(json);

        return document.RootElement.GetProperty("runs")[0]
            .GetProperty("results")
            .EnumerateArray()
            .Select(result => result.GetProperty("message")
                .GetProperty("text")
                .GetString()!)
            .ToList();
    }
}
