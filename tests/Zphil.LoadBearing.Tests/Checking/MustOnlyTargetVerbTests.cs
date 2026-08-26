using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The framework allow-list verb <c>MustOnlyTarget</c> over hand-built project facts (GRAMMAR §4.10):
///     a project reds when any framework it declares sits outside the permitted list, at whichever
///     declaration set them.
/// </summary>
/// <remarks>
///     Two decisions are pinned here rather than left to be discovered. <b>Strict</b>: there is no
///     exemption for anything, because a project's frameworks are a closed set and every moniker it
///     declares is one this rule judges. <b>Absent-fact pass</b>: a project nothing evaluated declares no
///     framework, so it has nothing to judge and passes — a rule that redded there would be reporting the
///     load's own gaps as architecture violations.
/// </remarks>
public sealed class MustOnlyTargetVerbTests
{
    // One project per fact shape: a single declared framework sited in its own file, two frameworks sited
    // in a props file above it (the shared-policy case), and a project nothing evaluated at all.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Contract", ["netstandard2.0"], ProjectFacts.Site("src/Contract/Contract.csproj", 5)),
        ProjectFacts.Project(
            "Host", ["net8.0", "net48"], ProjectFacts.Site("Directory.Build.props", 3)),
        ProjectFacts.Project("Unevaluated"));

    [Fact]
    public void MustOnlyTarget_FrameworkOutsideTheList_FailsAtTheDeclaringPropsFile()
    {
        // The site is the props file, not the project: the property a rule is about is very often set
        // somewhere else, and the file:line an agent jumps to has to be where the winning declaration sits.
        Checker.Run(Scene, arch => arch.Rule("packaging/contract-tfm")
                .Enforce(arch.Projects.Named("Host").MustOnlyTarget("netstandard2.0"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Host", ["Directory.Build.props:3"]);
    }

    [Fact]
    public void MustOnlyTarget_EveryDeclaredFrameworkPermitted_PassesClean()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/contract-tfm")
                .Enforce(arch.Projects.Named("Contract").MustOnlyTarget("netstandard2.0"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyTarget_MultiTargetedProjectWithBothPermitted_PassesClean()
    {
        // The allow-list is over the whole declared set, so a multi-targeted project passes only when every
        // one of its frameworks is listed — never on the strength of the first.
        Checker.Run(Scene, arch => arch.Rule("packaging/host-tfm")
                .Enforce(arch.Projects.Named("Host").MustOnlyTarget("net8.0", "net48"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyTarget_ProjectNothingEvaluated_PassesClean()
    {
        // The absent-fact row: unknown never reds. A project with no declared framework has nothing outside
        // the list, which is the honest reading of a fact the load never measured.
        Checker.Run(Scene, arch => arch.Rule("packaging/contract-tfm")
                .Enforce(arch.Projects.Named("Unevaluated").MustOnlyTarget("netstandard2.0"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyTarget_DeclaredFrameworkWithNoSite_FailsUnlocated()
    {
        // A framework read from what the load discriminated rather than from an evaluation carries no site.
        // The finding is still real, so it reds — with nothing to point at, which is more honest than a
        // made-up location.
        CodebaseModel siteless = ProjectFacts.Solution(ProjectFacts.Project("Legacy", ["net48"]));

        Checker.Run(siteless, arch => arch.Rule("packaging/modern-only")
                .Enforce(arch.Projects.Named("Legacy").MustOnlyTarget("net8.0"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Legacy", []);
    }

    [Fact]
    public void MustOnlyTarget_OneViolationPerOffendingProject_NotPerFramework()
    {
        // A project declaring two forbidden frameworks is one finding, not two: the law is about the
        // project's target set, and the sites are evidence rather than identity (GRAMMAR §4.3).
        Checker.Run(Scene, arch => arch.Rule("packaging/netstandard-only")
                .Enforce(arch.Projects.Matching("*").MustOnlyTarget("netstandard2.0"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(["Host"]);
    }
}
