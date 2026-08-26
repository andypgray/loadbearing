using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The publish-ban verb <c>MustNotBePackable</c> over hand-built project facts (GRAMMAR §4.10): a
///     project an evaluation reported as producing a package reds, sited where <c>IsPackable</c> was set.
/// </summary>
/// <remarks>
///     The polarity is the point. The SDK defaults <c>IsPackable</c> on, so the projects that ship and the
///     projects that merely compile look alike until one opts out — this verb is what makes the opt-out
///     checkable rather than assumed. The tri-state holds as everywhere else: only known-true reds. The
///     <c>.Packable()</c> adjective is the same fact read from the other side and is pinned here beside
///     it, because a subject that admitted unknown projects would silently widen every rule written on it.
/// </remarks>
public sealed class MustNotBePackableVerbTests
{
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Contract", isPackable: true, isPackableSite: ProjectFacts.Site("src/Contract/Contract.csproj", 4)),
        ProjectFacts.Project(
            "Internal", isPackable: false, isPackableSite: ProjectFacts.Site("src/Internal/Internal.csproj", 6)),
        ProjectFacts.Project("Unevaluated"));

    [Fact]
    public void MustNotBePackable_PackableProject_FailsAtTheDeclaringSite()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/internal-not-shipped")
                .Enforce(arch.Projects.Named("Contract").MustNotBePackable())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Contract", ["src/Contract/Contract.csproj:4"]);
    }

    [Fact]
    public void MustNotBePackable_OptedOutProject_PassesClean()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/internal-not-shipped")
                .Enforce(arch.Projects.Named("Internal").MustNotBePackable())
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotBePackable_ProjectNothingEvaluated_PassesClean()
    {
        // The absent-fact row: unknown never reds, even though the SDK's own default is on.
        Checker.Run(Scene, arch => arch.Rule("packaging/internal-not-shipped")
                .Enforce(arch.Projects.Named("Unevaluated").MustNotBePackable())
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void PackableAdjective_AdmitsOnlyKnownTrue()
    {
        // Read from the other side: the adjective narrows to the projects an evaluation reported as
        // packable, so neither the opted-out project nor the unevaluated one enters the subject. A subject
        // that admitted unknown would make every rule written on it wider than its sentence says.
        Checker.SelectsProjects(Scene, arch => arch.Projects.Packable())
            .ShouldBe(["Contract"]);
    }

    [Fact]
    public void MustNotBePackable_UnderThePackableAdjective_RedsExactlyTheKnownPackable()
    {
        // The composed shape a real spec writes — "packable projects must not be packable" is deliberately
        // circular here, and that is what makes it a pure test of the adjective's reach.
        Checker.Run(Scene, arch => arch.Rule("packaging/nothing-ships")
                .Enforce(arch.Projects.Packable().MustNotBePackable())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(["Contract"]);
    }
}
