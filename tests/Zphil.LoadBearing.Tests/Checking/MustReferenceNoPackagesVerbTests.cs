using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The package-ban verb <c>MustReferenceNoPackages</c> over hand-built project facts (GRAMMAR §4.10):
///     one violation per <c>PackageReference</c> a subject project declares, each sited at that
///     reference's own declaration.
/// </summary>
/// <remarks>
///     Two decisions are pinned here. <b>Per package, one identity</b>: eight packages report eight lines
///     to delete, but they share the project's baseline identity, because the law is about the project and
///     an entry blessing it must not have to be rewritten every time the package list moves.
///     <b>Empty passes</b>: a project that declares none and a project nothing evaluated are
///     indistinguishable by design — both carry an empty list — so both pass.
/// </remarks>
public sealed class MustReferenceNoPackagesVerbTests
{
    // One project declaring two packages from two different files — its own and a central props file — and
    // one declaring none, which is also what an unevaluated project looks like.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Domain",
            packageReferences:
            [
                ProjectFacts.Package("Newtonsoft.Json", "src/Domain/Domain.csproj", 9),
                ProjectFacts.Package("Serilog", "Directory.Packages.props", 4)
            ]),
        ProjectFacts.Project("Contract"));

    [Fact]
    public void MustReferenceNoPackages_DeclaredPackages_FailOncePerReferenceAtItsOwnSite()
    {
        RuleResult result = Checker.Run(Scene, arch => arch.Rule("packaging/domain-pure")
                .Enforce(arch.Projects.Named("Domain").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.ShouldHaveFailedWithProjectSubjects(["Domain -> Newtonsoft.Json", "Domain -> Serilog"]);
        result.Violations.Select(violation => violation.Sites.Single()
                .ToString())
            .ShouldBe(["src/Domain/Domain.csproj:9", "Directory.Packages.props:4"], ignoreOrder: true);
    }

    [Fact]
    public void MustReferenceNoPackages_EveryViolation_SharesTheProjectsBaselineIdentity()
    {
        // The NEW identity shape on this stratum: per-package violations collapse to one project-keyed
        // entry, so grandfathering a project's package debt is one line rather than one line per package.
        RuleResult result = Checker.Run(Scene, arch => arch.Rule("packaging/domain-pure")
                .Enforce(arch.Projects.Named("Domain").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.BaselineIdentity())
            .Distinct()
            .ShouldBe([BaselineEntry.ForSubject("project:Domain")]);
    }

    [Fact]
    public void MustReferenceNoPackages_NoDeclaredPackages_PassesClean()
    {
        // Declared-none and never-evaluated are one state here, and both are green: the model holds what a
        // project declares, so an empty list is the only honest reading of either.
        Checker.Run(Scene, arch => arch.Rule("packaging/contract-pure")
                .Enforce(arch.Projects.Named("Contract").MustReferenceNoPackages())
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustReferenceNoPackages_SweepsEveryProjectInTheSubject()
    {
        // The subject is a set, so the walk is per project and then per package inside it — a rule over
        // both projects reports the two packages of the one that declares any, and nothing for the other.
        Checker.Run(Scene, arch => arch.Rule("packaging/all-pure")
                .Enforce(arch.Projects.Matching("*").MustReferenceNoPackages())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(["Domain -> Newtonsoft.Json", "Domain -> Serilog"]);
    }
}
