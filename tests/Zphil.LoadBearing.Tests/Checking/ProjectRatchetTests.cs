using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The ratchet over a project subject (GRAMMAR §4.10, §7): a Migrate rule partitions its project
///     violations against the captured baseline on the <c>project:{Name}</c> identity, so blessed
///     packaging debt passes and a project nobody blessed stays red.
/// </summary>
/// <remarks>
///     The row that matters most is the per-package one. A <c>MustReferenceNoPackages</c> rule mints one
///     violation per declared package and they all key on the project, so a single entry grandfathers the
///     whole list — and adding a ninth package to an already-blessed project adds no red. That is the
///     deliberate cost of the collapse, and it is pinned here so a future change to the identity has to
///     move this row with it.
/// </remarks>
public sealed class ProjectRatchetTests
{
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Contract", isPackable: true, isPackableSite: ProjectFacts.Site("src/Contract/Contract.csproj", 4)),
        ProjectFacts.Project(
            "Zphil.Cli", isPackable: true, isPackableSite: ProjectFacts.Site("src/Cli/Cli.csproj", 4)));

    private static readonly CodebaseModel PackageScene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Domain",
            packageReferences:
            [
                ProjectFacts.Package("Newtonsoft.Json", "src/Domain/Domain.csproj", 9),
                ProjectFacts.Package("Serilog", "src/Domain/Domain.csproj", 10)
            ]),
        ProjectFacts.Project(
            "Zphil.Web", packageReferences: [ProjectFacts.Package("Serilog", "src/Web/Web.csproj", 7)]));

    [Fact]
    public void Migrate_BaselinedProject_Grandfathers_WhileTheOtherStaysRed()
    {
        BaselineIndex index = Checker.Baselines(
            "packaging/nothing-ships", BaselineEntry.ForSubject("project:Zphil.Contract"));

        RuleResult result = Checker.Run(Scene, index, arch => arch.Rule("packaging/nothing-ships")
                .Migrate(
                    "Two projects still publish packages.",
                    arch.Projects.Matching("Zphil.*").MustNotBePackable())
                .Because("A package nobody meant to publish is an API nobody meant to promise."))
            .Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Cli"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Migrate_ProjectIdentity_IsTheProjectTaggedName()
    {
        // The pinned identity form. A project carries no DocumentationCommentId, so the tag is the word
        // `project` — which is also what keeps SymbolIds.Display from stripping it.
        RuleResult result = Checker.Run(Scene, BaselineIndex.Empty, arch => arch.Rule("packaging/nothing-ships")
                .Migrate(
                    "Two projects still publish packages.",
                    arch.Projects.Matching("Zphil.*").MustNotBePackable())
                .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe(
            [
                BaselineEntry.ForSubject("project:Zphil.Cli"),
                BaselineEntry.ForSubject("project:Zphil.Contract")
            ]);
    }

    [Fact]
    public void Migrate_OneEntryGrandfathersEveryPackageOfThatProject()
    {
        // Per-package violations share one identity, so one baseline entry blesses the whole list — and the
        // project that was not blessed still reports each of its own.
        BaselineIndex index = Checker.Baselines(
            "packaging/domain-pure", BaselineEntry.ForSubject("project:Zphil.Domain"));

        RuleResult result = Checker.Run(PackageScene, index, arch => arch.Rule("packaging/domain-pure")
                .Migrate(
                    "Two projects still take packages directly.",
                    arch.Projects.Matching("Zphil.*").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Web -> Serilog"]);
        result.ShouldHaveGrandfathered(2);
    }

    [Fact]
    public void Migrate_ViolationOrder_IsProjectThenPackage()
    {
        // Deterministic report order across the new slots: the project name is the primary key and the
        // package name stands where a Target would, so one project's packages sort among themselves.
        RuleResult result = Checker.Run(PackageScene, BaselineIndex.Empty, arch => arch.Rule("packaging/domain-pure")
                .Migrate("Packages everywhere.", arch.Projects.Matching("Zphil.*").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.Violations.Select(violation => $"{violation.SubjectProject!.Name} -> {violation.Package!.Name}")
            .ShouldBe(["Zphil.Domain -> Newtonsoft.Json", "Zphil.Domain -> Serilog", "Zphil.Web -> Serilog"]);
    }
}
