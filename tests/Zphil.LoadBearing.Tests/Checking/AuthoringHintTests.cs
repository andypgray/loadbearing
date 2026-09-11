using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The cure that rides an empty subject and an inert target: which advice each selection shape earns,
///     and that the two signals ask different things of the same shape.
/// </summary>
public sealed class AuthoringHintTests
{
    private const string NamespaceFragment = "`Legacy*` never crosses a dot";

    private const string ProjectFragment = "named by its csproj file name";

    private const string TypeFragment = "typeof anchor reaches only a type this solution declares";

    private const string NarrowingFragment = "term narrows, so the set is no wider than the last one applied";

    private const string SubjectAction = "check the selection against what the solution declares";

    private const string InertAction = "passes forever, so decide before keeping it";

    [Fact]
    public void NamespaceNoun_EarnsTheGlobSemantics()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Namespace("App.Nowhere.*"))
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void TypesNarrowedByOneInNamespace_EarnsTheGlobSemanticsToo()
    {
        // The same namespace region spelled the other way round: RegionOf reads it off arch.Types plus a
        // single InNamespace exactly as it reads it off the noun.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Types.InNamespace("App.Nowhere.*"))
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void GlobLayer_EarnsTheGlobSemantics()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Layer("Domain", "App.Nowhere.*"))
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void LayerDefinedByASelection_IsCuredAsWhateverDefinesIt()
    {
        // A layer is transparent to its definition, so a layer that IS a project earns the project's cure
        // rather than glob semantics it has no glob to apply to.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Layer("Core", arch.Project("App.Core")))
            .ShouldContain(ProjectFragment);
    }

    [Fact]
    public void ProjectNoun_EarnsTheProjectNamingRule()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Project("App.Nowhere"))
            .ShouldContain(ProjectFragment);
    }

    [Fact]
    public void TypeofNoun_EarnsTheDeclaredOnlyRule()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Type(typeof(AuthoringHintTests)))
            .ShouldContain(TypeFragment);
    }

    [Fact]
    public void ANarrowedTypesSubject_FallsBackToTheNarrowingAdvice()
    {
        // Nothing here globs a namespace, so glob semantics would be a false sentence: the honest cure is
        // the one true of every selection.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Types.WithSuffix("Service"))
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void ARegisteredSubject_FallsBackToTheNarrowingAdvice()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Registered(Lifetime.Singleton))
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void AUnionSubject_FallsBackToTheNarrowingAdvice()
    {
        // A union answers no noun, and its parts may disagree about shape — but each empty part is cured on
        // its own terms anyway, so the union's own sentence never has to choose between them.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.AnyOf(arch.Namespace("App.A.*"), arch.Project("App.B")))
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void TheSameShape_AsksDifferentThingsOfASubjectAndOfATarget()
    {
        // One vocabulary, two signals: the semantics are shared and what to do about them is not. A subject
        // that matched nothing has already failed; an inert target has not, which is the whole difference.
        var arch = new Arch();
        Selection pattern = arch.Namespace("App.Nowhere.*");

        string subject = AuthoringHints.ForSubject(pattern);
        string inert = AuthoringHints.ForInertTarget([pattern]);

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

        AuthoringHints.ForInertTarget([arch.Type(typeof(AuthoringHintTests)), arch.Namespace("App.Ghost.*")])
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void AnInertTargetWithNoPatternOperand_FallsBackToTheNarrowingAdvice()
    {
        // Unreachable through the checker, whose gate warns only when some operand is a pattern — but the
        // floor has to hold anyway, because it is what makes the chooser total. Advice true of every
        // selection is the only honest answer when there is no shape to read.
        AuthoringHints.ForInertTarget([])
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
            AuthoringHints.ForSubject(arch.Namespace("App.Nowhere.*")),
            AuthoringHints.ForSubject(arch.Project("App.Nowhere")),
            AuthoringHints.ForSubject(arch.Type(typeof(AuthoringHintTests))),
            AuthoringHints.ForSubject(arch.Types.WithSuffix("Service")),
            AuthoringHints.ForInertTarget([arch.Namespace("App.Ghost.*")]),
            AuthoringHints.EmptyMemberSubject,
            AuthoringHints.EmptyProjectSubject
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
}
