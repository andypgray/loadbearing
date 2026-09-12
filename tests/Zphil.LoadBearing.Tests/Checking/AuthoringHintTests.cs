using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The cure that rides an empty subject and an inert target: which advice each selection shape earns,
///     that the two signals ask different things of the same shape, and that a partial model replaces every
///     one of those cures rather than trailing them.
/// </summary>
public sealed class AuthoringHintTests
{
    private const string NamespaceFragment = "`Legacy*` never crosses a dot";

    private const string ProjectFragment = "named by its csproj file name";

    private const string TypeFragment = "typeof anchor reaches only a type this solution declares";

    private const string NarrowingFragment = "term narrows, so the set is no wider than the last one applied";

    private const string SubjectAction = "check the selection against what the solution declares";

    private const string InertAction = "passes forever, so decide before keeping it";

    // A stand-in for the sentence a partial model composes; its wording is pinned where it is minted, and
    // these rows are about the replacement rather than about the words.
    private const string PartialModelCure = "THE-PARTIAL-MODEL-CURE";

    [Fact]
    public void NamespaceNoun_EarnsTheGlobSemantics()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Namespace("App.Nowhere.*"), null)
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void TypesNarrowedByOneInNamespace_EarnsTheGlobSemanticsToo()
    {
        // The same namespace region spelled the other way round: RegionOf reads it off arch.Types plus a
        // single InNamespace exactly as it reads it off the noun.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Types.InNamespace("App.Nowhere.*"), null)
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void GlobLayer_EarnsTheGlobSemantics()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Layer("Domain", "App.Nowhere.*"), null)
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void LayerDefinedByASelection_IsCuredAsWhateverDefinesIt()
    {
        // A layer is transparent to its definition, so a layer that IS a project earns the project's cure
        // rather than glob semantics it has no glob to apply to.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Layer("Core", arch.Project("App.Core")), null)
            .ShouldContain(ProjectFragment);
    }

    [Fact]
    public void ProjectNoun_EarnsTheProjectNamingRule()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Project("App.Nowhere"), null)
            .ShouldContain(ProjectFragment);
    }

    [Fact]
    public void TypeofNoun_EarnsTheDeclaredOnlyRule()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Type(typeof(AuthoringHintTests)), null)
            .ShouldContain(TypeFragment);
    }

    [Fact]
    public void ANarrowedTypesSubject_FallsBackToTheNarrowingAdvice()
    {
        // Nothing here globs a namespace, so glob semantics would be a false sentence: the honest cure is
        // the one true of every selection.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Types.WithSuffix("Service"), null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void ARegisteredSubject_FallsBackToTheNarrowingAdvice()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Registered(Lifetime.Singleton), null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void AUnionSubject_FallsBackToTheNarrowingAdvice()
    {
        // A union answers no noun, and its parts may disagree about shape — but each empty part is cured on
        // its own terms anyway, so the union's own sentence never has to choose between them.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.AnyOf(arch.Namespace("App.A.*"), arch.Project("App.B")), null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void TheSameShape_AsksDifferentThingsOfASubjectAndOfATarget()
    {
        // One vocabulary, two signals: the semantics are shared and what to do about them is not. A subject
        // that matched nothing has already failed; an inert target has not, which is the whole difference.
        var arch = new Arch();
        Selection pattern = arch.Namespace("App.Nowhere.*");

        string subject = AuthoringHints.ForSubject(pattern, null);
        string inert = AuthoringHints.ForInertTarget([pattern], null);

        subject.ShouldSatisfyAllConditions(
            () => subject.ShouldContain(NamespaceFragment),
            () => subject.ShouldContain(SubjectAction),
            () => subject.ShouldNotContain(InertAction));

        inert.ShouldSatisfyAllConditions(
            () => inert.ShouldContain(NamespaceFragment),
            () => inert.ShouldContain(InertAction),
            () => inert.ShouldNotContain(SubjectAction));
    }

    [Fact]
    public void AnInertTarget_IsCuredByThePatternOperandThatRaisedTheWarning()
    {
        // The gate fires on the first PATTERN operand, so the sentence has to describe that one rather than
        // a bare typeof sibling, whose absence is the win condition and never warns at all.
        var arch = new Arch();

        AuthoringHints.ForInertTarget([arch.Type(typeof(AuthoringHintTests)), arch.Namespace("App.Ghost.*")], null)
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void AnInertTargetWithNoPatternOperand_FallsBackToTheNarrowingAdvice()
    {
        // Unreachable through the checker, whose gate warns only when some operand is a pattern — but the
        // floor has to hold anyway, because it is what makes the chooser total. Advice true of every
        // selection is the only honest answer when there is no shape to read.
        AuthoringHints.ForInertTarget([], null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void EveryHint_ReadsAsOneSentenceInTheHouseVoice()
    {
        // The registers these share: one line, a semicolon parting diagnosis from cure, a full stop, and
        // never the second person. Cheap insurance on text only ever read at a moment of failure.
        var arch = new Arch();
        string[] hints =
        [
            AuthoringHints.ForSubject(arch.Namespace("App.Nowhere.*"), null),
            AuthoringHints.ForSubject(arch.Project("App.Nowhere"), null),
            AuthoringHints.ForSubject(arch.Type(typeof(AuthoringHintTests)), null),
            AuthoringHints.ForSubject(arch.Types.WithSuffix("Service"), null),
            AuthoringHints.ForInertTarget([arch.Namespace("App.Ghost.*")], null),
            AuthoringHints.ForMemberSubject(null),
            AuthoringHints.ForProjectSubject(null)
        ];

        hints.ShouldAllBe(hint => !hint.Contains('\n'));
        hints.ShouldAllBe(hint => hint.EndsWith('.'));
        hints.ShouldAllBe(hint => char.IsUpper(hint[0]));
        hints.ShouldAllBe(hint => hint.Contains(';'));
        hints.ShouldAllBe(hint => !hint.Contains(" you ") && !hint.Contains(" your "));
    }

    [Fact]
    public void AnEmptySubject_CarriesItsCureThroughTheChecker()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("empty/x")
                    .Enforce(arch.Namespace("Nope.Nowhere.*").MustHaveSuffix("X"))
                    .Because("b"))
            .Single();

        string? hint = result.Violations.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void AnEmptySubjectOfAnotherShape_CarriesThatShapesCure()
    {
        // The wiring proves the point of the feature: two rules failing on the same kind get different
        // cures, because the cure is read off the selection rather than off the kind.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("empty/y")
                    .Enforce(arch.Types.WithSuffix("NoSuchSuffix").MustBeSealed())
                    .Because("b"))
            .Single();

        string? hint = result.Violations.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void AnInertTarget_CarriesItsCureThroughTheChecker()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        string? hint = result.Warnings.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldContain(InertAction);
    }

    [Fact]
    public void APartialModel_ReplacesEveryShapeCureRatherThanTrailingIt()
    {
        // Both signals and both causes: advice to check a selection against what the solution declares is
        // about the wrong thing when a project never loaded, so it is replaced rather than appended — and a
        // sentence carrying both would be two cures for one diagnosis, past the single semicolon these
        // clauses are built to keep.
        //
        // Every shape the chooser reads, rather than one of them twice: the replacement is unconditional,
        // and a cure narrowed to namespace nouns would go on reading green against a glob row while a
        // project- or typeof-shaped empty subject advised a spec fix for a load failure. That is the
        // half-fix the member and project row below exists to prevent, one stratum over.
        var arch = new Arch();
        var partial = new IncompleteModel(PartialModelCure);
        (string Shape, Selection Pattern, string ShapeFragment)[] shapes =
        [
            ("a namespace glob", arch.Namespace("App.Nowhere.*"), NamespaceFragment),
            ("a project name", arch.Project("App.Nowhere"), ProjectFragment),
            ("a typeof anchor", arch.Type(typeof(AuthoringHintTests)), TypeFragment),
            ("a narrowed types selection", arch.Types.WithSuffix("Service"), NarrowingFragment)
        ];

        foreach ((string shape, Selection pattern, string shapeFragment) in shapes)
        {
            string subject = AuthoringHints.ForSubject(pattern, partial);
            string inert = AuthoringHints.ForInertTarget([pattern], partial);

            subject.ShouldSatisfyAllConditions(
                () => subject.ShouldBe(PartialModelCure, shape),
                () => subject.ShouldNotContain(shapeFragment, shape),
                () => subject.ShouldNotContain(SubjectAction, shape));
            inert.ShouldSatisfyAllConditions(
                () => inert.ShouldBe(PartialModelCure, shape),
                () => inert.ShouldNotContain(InertAction, shape));
        }
    }

    [Fact]
    public void APartialModel_ReachesTheMemberAndProjectCuresToo()
    {
        // The two fixed clauses read off no selection shape, so nothing about them would have made anyone
        // route them through the fact — and a failed project empties a member or a project subject exactly
        // as it empties a type one. Leaving these two alone was the half-fix this row exists to prevent.
        var partial = new IncompleteModel(PartialModelCure);

        AuthoringHints.ForMemberSubject(partial)
            .ShouldBe(PartialModelCure);
        AuthoringHints.ForProjectSubject(partial)
            .ShouldBe(PartialModelCure);
    }

    [Fact]
    public void APartialModel_ReachesBothSignalsThroughTheChecker()
    {
        // The wiring, end to end on one model: a subject that matched nothing fails carrying the fact, and a
        // target that matched nothing warns carrying the same fact. Before this, a rule reported itself inert
        // because the edge it rests on was never extracted and the cure it printed was about globs.
        var partial = new IncompleteModel(PartialModelCure);

        RuleResult empty = Checker.Run(Sources.LayeredModel, partial, arch =>
                arch.Rule("empty/x")
                    .Enforce(arch.Namespace("Nope.Nowhere.*").MustHaveSuffix("X"))
                    .Because("b"))
            .Single();
        RuleResult inert = Checker.Run(Sources.LayeredModel, partial, arch =>
                arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        empty.Violations.Single()
            .Hint.ShouldBe(PartialModelCure);
        inert.Warnings.Single()
            .Hint.ShouldBe(PartialModelCure);
    }

    [Fact]
    public void AWholeModel_ReachesTheShapeCureItAlwaysDid()
    {
        // The control on the whole feature: null is every run whose projects loaded, and those reach the
        // advice they always did, byte for byte. Declared rather than passed inline because a bare null is
        // ambiguous between this overload and the baselines one, and a cast would read as noise.
        IncompleteModel? whole = null;

        RuleResult result = Checker.Run(Sources.LayeredModel, whole, arch =>
                arch.Rule("empty/x")
                    .Enforce(arch.Namespace("Nope.Nowhere.*").MustHaveSuffix("X"))
                    .Because("b"))
            .Single();

        string? hint = result.Violations.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldContain(NamespaceFragment);
    }
}
