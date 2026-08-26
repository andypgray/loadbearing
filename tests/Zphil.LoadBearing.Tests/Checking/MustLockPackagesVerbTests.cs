using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The supply-chain verb <c>MustLockPackages</c> over hand-built project facts (GRAMMAR §4.10): a
///     project whose restore does not write a lock file reds, at whichever declaration turned it off —
///     regularly a solution-wide props file the project itself never mentions.
/// </summary>
/// <remarks>
///     The tri-state is the whole of the verb. <see langword="false" /> reds,
///     <see langword="true" /> passes, and <see langword="null" /> — nothing evaluated this project —
///     passes too, because unknown is not a claim either way.
/// </remarks>
public sealed class MustLockPackagesVerbTests
{
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Locked", locksPackages: true, locksPackagesSite: ProjectFacts.Site("src/Directory.Build.props", 12)),
        ProjectFacts.Project(
            "Unlocked", locksPackages: false, locksPackagesSite: ProjectFacts.Site("Directory.Build.props", 7)),
        ProjectFacts.Project("Unevaluated"));

    [Fact]
    public void MustLockPackages_RestoreWritesNoLockFile_FailsAtTheDeclaringPropsFile()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/locked-restore")
                .Enforce(arch.Projects.Named("Unlocked").MustLockPackages())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Unlocked", ["Directory.Build.props:7"]);
    }

    [Fact]
    public void MustLockPackages_RestoreWritesALockFile_PassesClean()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/locked-restore")
                .Enforce(arch.Projects.Named("Locked").MustLockPackages())
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustLockPackages_ProjectNothingEvaluated_PassesClean()
    {
        // The absent-fact row: unknown never reds, even though NuGet's own default is off — the model says
        // nothing about this project, and a verdict against silence would be the load's gap wearing a law.
        Checker.Run(Scene, arch => arch.Rule("packaging/locked-restore")
                .Enforce(arch.Projects.Named("Unevaluated").MustLockPackages())
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustLockPackages_UnlockedWithNoSite_FailsUnlocated()
    {
        CodebaseModel siteless = ProjectFacts.Solution(ProjectFacts.Project("Loose", locksPackages: false));

        Checker.Run(siteless, arch => arch.Rule("packaging/locked-restore")
                .Enforce(arch.Projects.Named("Loose").MustLockPackages())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Loose", []);
    }

    [Fact]
    public void MustLockPackages_SweepingSubject_RedsOnlyTheKnownFalse()
    {
        // Over the whole solution the verb separates the three states in one pass: only the declared-false
        // project is a finding.
        Checker.Run(Scene, arch => arch.Rule("packaging/locked-restore")
                .Enforce(arch.Projects.Matching("*").MustLockPackages())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(["Unlocked"]);
    }
}
