using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The project subject itself (GRAMMAR §4.10): which projects each adjective reaches, the
///     constraint-position escape hatch over <see cref="IProjectInfo" />, and the two loud failures — an
///     empty project subject and a predicate that throws.
/// </summary>
/// <remarks>
///     The adjective rows read the subject back through a probe rule nothing can satisfy, so what they
///     assert is the selection and never a verb's verdict. The escape-hatch rows are the other way round:
///     the selection is a single named project and the predicate is the whole claim.
/// </remarks>
public sealed class ProjectSubjectVerbTests
{
    // Three projects whose names separate every adjective under test, plus the facts the escape-hatch rows
    // read: a packable contract with a framework, an internal project opted out, and a tool project.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project("Zphil.Contract", ["netstandard2.0"], isPackable: true),
        ProjectFacts.Project("Zphil.Internal", ["net8.0"], isPackable: false),
        ProjectFacts.Project("Other.Tool", ["net8.0"]));

    [Fact]
    public void Named_OneName_SelectsThatProjectAlone()
    {
        Checker.SelectsProjects(Scene, arch => arch.Projects.Named("Zphil.Contract"))
            .ShouldBe(["Zphil.Contract"]);
    }

    [Fact]
    public void Named_SeveralNames_SelectsEachOfThem()
    {
        // Ordinal and exact: `.Named` takes identifiers the solution declares, which is what parts it from
        // the glob form beside it.
        Checker.SelectsProjects(Scene, arch => arch.Projects.Named("Zphil.Contract", "Other.Tool"))
            .ShouldBe(["Zphil.Contract", "Other.Tool"], ignoreOrder: true);
    }

    [Fact]
    public void Named_NameNoProjectDeclares_SelectsNothing()
    {
        Checker.SelectsProjects(Scene, arch => arch.Projects.Named("zphil.contract"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Matching_Glob_SelectsEveryProjectWhoseNameMatches()
    {
        // `*` spans any run of characters, dots included — a project name is one token with no segment
        // structure, so there is no subtree operator here and nothing to anchor.
        Checker.SelectsProjects(Scene, arch => arch.Projects.Matching("Zphil.*"))
            .ShouldBe(["Zphil.Contract", "Zphil.Internal"], ignoreOrder: true);
    }

    [Fact]
    public void Matching_SeveralGlobs_SelectsTheUnion()
    {
        Checker.SelectsProjects(Scene, arch => arch.Projects.Matching("*.Contract", "*.Tool"))
            .ShouldBe(["Zphil.Contract", "Other.Tool"], ignoreOrder: true);
    }

    [Fact]
    public void Except_SubtractsThePayloadSelection()
    {
        Checker.SelectsProjects(
                Scene,
                arch => arch.Projects.Matching("Zphil.*")
                    .Except(arch.Projects.Named("Zphil.Internal")))
            .ShouldBe(["Zphil.Contract"]);
    }

    [Fact]
    public void Where_NarrowsByThePredicate()
    {
        // The selector-position hatch sees the same IProjectInfo the constraint-position one does, so a
        // rule can narrow on a fact the closed vocabulary has no adjective for.
        Checker.SelectsProjects(
                Scene,
                arch => arch.Projects.Where(
                    project => project.TargetFrameworks.Contains("net8.0"),
                    description: "that target net8.0"))
            .ShouldBe(["Zphil.Internal", "Other.Tool"], ignoreOrder: true);
    }

    [Fact]
    public void Must_PredicateHolds_PassesClean()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/described")
                .Enforce(arch.Projects.Named("Zphil.Contract")
                    .Must(project => project.IsPackable == true, description: "produce a package"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void Must_PredicateFails_RedsUnlocated()
    {
        // A predicate is a claim about the whole project, and no single declaration in it is the one that
        // failed — so the violation carries no site rather than an invented one.
        Checker.Run(Scene, arch => arch.Rule("packaging/described")
                .Enforce(arch.Projects.Named("Zphil.Internal")
                    .Must(project => project.IsPackable == true, description: "produce a package"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Zphil.Internal", []);
    }

    [Fact]
    public void Must_ThrowingPredicate_BecomesARuleErrorNamingTheProject()
    {
        // The guarded invoke: a throwing hatch is a rule error naming the project it threw on, never an
        // aborted run.
        Checker.Run(Scene, arch => arch.Rule("packaging/described")
                .Enforce(arch.Projects.Named("Zphil.Contract")
                    .Must(_ => throw new InvalidOperationException("boom"), description: "explode"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithDetailContaining(
                ViolationKind.RuleError, "`Must` predicate threw InvalidOperationException on `Zphil.Contract`", "boom");
    }

    [Fact]
    public void Where_ThrowingPredicate_BecomesARuleErrorToo()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/described")
                .Enforce(arch.Projects.Where(_ => throw new InvalidOperationException("boom"), description: "that explode")
                    .MustNotBePackable())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithDetailContaining(ViolationKind.RuleError, "`Where` predicate threw InvalidOperationException");
    }

    [Fact]
    public void EmptyProjectSubject_FailsWithTheProjectFlavouredMessage()
    {
        // The project analog of the empty type and member subjects (GRAMMAR §4.1, §4.6): a subject that
        // matched nothing fails the rule by default, and says so in project terms.
        Checker.Run(Scene, arch => arch.Rule("packaging/nowhere")
                .Enforce(arch.Projects.Named("Nope").MustNotBePackable())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptyProjectSubjectMessage);
    }

    [Fact]
    public void EmptyProjectSubjectMessage_IsPinned()
    {
        ConstraintEvaluator.EmptyProjectSubjectMessage
            .ShouldBe("The subject selection matched no solution-declared projects.");
    }
}
