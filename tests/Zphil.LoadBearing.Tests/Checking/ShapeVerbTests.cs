using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The shape/naming/escape verbs (GRAMMAR §5.3) and the adjective + noun vocabulary that feeds
///     them (§5.1–§5.2): every adjective (InNamespace, OfKind, WithSuffix, WithPrefix,
///     WithNameMatching, Named, Except, Where) and the Project noun get at least one pass/fail pin here,
///     plus the contrast that keeps a <c>*</c> inside an affix literal where the glob family expands it
///     (§4.2).
/// </summary>
public sealed class ShapeVerbTests
{
    private const string Naming = """
                                  namespace App.Naming
                                  {
                                      public interface IFoo {}
                                      public interface Bar {}
                                      public class OrderController {}
                                      public class OrderHandler {}
                                      public class UserRepository {}
                                      public class X {}
                                  }
                                  """;

    // The exact-name universe: one simple name carried by two namespaces, a nested type, and a generic —
    // the whole of what .Named's matching rule has to answer for.
    private const string SimpleNames = """
                                       namespace App.A
                                       {
                                           public class Widget {}
                                           public class Order
                                           {
                                               public class Line {}
                                           }
                                           public class Repository<T> {}
                                       }

                                       namespace App.B
                                       {
                                           public class Widget {}
                                       }
                                       """;

    private const string Shape = """
                                 namespace App.Shape
                                 {
                                     public sealed class SealedThing {}
                                     public class OpenThing {}
                                     public static class StaticThing {}
                                     public abstract class AbstractThing {}
                                     internal class InternalThing {}
                                     public class PublicThing {}
                                 }
                                 """;

    private static readonly CodebaseModel NamingModel = CompilationFactory.Extract(Naming);

    private static readonly CodebaseModel ShapeModel = CompilationFactory.Extract(Shape);

    private static readonly CodebaseModel SimpleNameModel = CompilationFactory.Extract(SimpleNames);

    [Fact]
    public void OfKind_And_MustHavePrefix_FlagInterfaceWithoutIPrefix()
    {
        RuleResult result = Checker.Run(NamingModel, arch =>
                arch.Rule("naming/interfaces")
                    .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("App.Naming.*").MustHavePrefix("I"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Naming.Bar"]);
    }

    [Fact]
    public void WithPrefix_And_MustHaveSuffix_FlagMismatchedSuffix()
    {
        RuleResult result = Checker.Run(NamingModel, arch =>
                arch.Rule("naming/handlers")
                    .Enforce(arch.Types.WithPrefix("Order").MustHaveSuffix("Handler"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Naming.OrderController"]);
    }

    [Fact]
    public void WithNameMatching_And_MustHaveNameMatching_Hold()
    {
        Checker.Run(NamingModel, arch =>
                arch.Rule("naming/repo")
                    .Enforce(arch.Types.WithNameMatching("*Repo*").MustHaveNameMatching("*Repository"))
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    // The next two pin the one thing that separates an affix from a glob: a `*` inside an affix is a
    // literal character, because only the *NameMatching family reaches the glob matcher. Every
    // `*`-free affix reads the same under either rule, so the contrast against the glob spelling is
    // the only honest way to state it — `*` is not a legal identifier character, so no codebase can
    // declare the name the literal reading looks for, and the literal side can only ever be empty.

    [Fact]
    public void WithSuffixAdjective_ReadsStarAsLiteral_WhereWithNameMatchingGlobs()
    {
        Checker.Run(NamingModel, arch =>
                arch.Rule("naming/literal-affix")
                    .Enforce(arch.Types.WithSuffix("*Controller").MustHavePrefix("Order"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);

        Checker.Run(NamingModel, arch =>
                arch.Rule("naming/glob-affix")
                    .Enforce(arch.Types.WithNameMatching("*Controller").MustHavePrefix("Order"))
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustHaveSuffixVerb_ReadsStarAsLiteral_WhereMustHaveNameMatchingGlobs()
    {
        Checker.Run(NamingModel, arch =>
                arch.Rule("naming/literal-verb")
                    .Enforce(arch.Types.Named("OrderController").MustHaveSuffix("*Controller"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Naming.OrderController"]);

        Checker.Run(NamingModel, arch =>
                arch.Rule("naming/glob-verb")
                    .Enforce(arch.Types.Named("OrderController").MustHaveNameMatching("*Controller"))
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustResideInNamespace_FlagsMisplacedType()
    {
        const string source = """
                              namespace App.Good { public class Widget {} }
                              namespace App.Bad { public class Widget2 {} }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("layout/x")
                    .Enforce(arch.Types.WithPrefix("Widget").MustResideInNamespace("App.Good.*"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Bad.Widget2"]);
    }

    [Fact]
    public void Must_EscapeHatch_HoldsAndFails()
    {
        Checker.Run(NamingModel, arch =>
                arch.Rule("style/short")
                    .Enforce(arch.Types.WithPrefix("X")
                        .Must(t => t.Name.Length <= 3, "keep names at or under 3 characters"))
                    .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(NamingModel, arch =>
                arch.Rule("style/short")
                    .Enforce(arch.Types.WithPrefix("OrderC")
                        .Must(t => t.Name.Length <= 3, "keep names at or under 3 characters"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Naming.OrderController"]);
    }

    [Fact]
    public void Except_SubtractsPayloadSelection()
    {
        // Subjects = Order* except *Handler = {OrderController}; it fails the Handler suffix.
        RuleResult result = Checker.Run(NamingModel, arch =>
                arch.Rule("naming/x")
                    .Enforce(arch.Types.WithPrefix("Order").Except(arch.Types.WithSuffix("Handler"))
                        .MustHaveSuffix("Handler"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Naming.OrderController"]);
    }

    [Fact]
    public void Except_SeveralPayloads_SubtractsEachOfThem()
    {
        // Several operands are one union payload, so every operand's types come out of the subject.
        IReadOnlyList<string> remaining = Checker.Selects(
            NamingModel,
            arch => arch.Types.InNamespace("App.Naming.*")
                .Except(arch.Types.Named("X"), arch.Types.Named("Bar")));

        remaining.ShouldBe(
            ["App.Naming.IFoo", "App.Naming.OrderController", "App.Naming.OrderHandler", "App.Naming.UserRepository"],
            ignoreOrder: true);
    }

    [Fact]
    public void Named_OneName_SelectsEveryTypeCarryingThatSimpleName()
    {
        // A simple name is not a location: it reaches every type carrying it, in every namespace.
        Checker.Selects(SimpleNameModel, arch => arch.Types.Named("Widget"))
            .ShouldBe(["App.A.Widget", "App.B.Widget"], ignoreOrder: true);
    }

    [Fact]
    public void Named_SeveralNames_SelectsEachOfThem()
    {
        Checker.Selects(SimpleNameModel, arch => arch.Types.Named("Order", "Widget"))
            .ShouldBe(["App.A.Order", "App.A.Widget", "App.B.Widget"], ignoreOrder: true);
    }

    [Fact]
    public void Named_IsCaseSensitive_SelectsNothingForTheWrongCase()
    {
        Checker.Selects(SimpleNameModel, arch => arch.Types.Named("widget"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Named_MatchesTheSimpleNameOnly()
    {
        // The simple name is the one a report prints without namespace, containing type or generic arity: a
        // nested type answers to its leaf, its dotted spelling names nothing, and a generic drops the arity.
        Checker.Selects(SimpleNameModel, arch => arch.Types.Named("Line"))
            .ShouldBe(["App.A.Order.Line"]);
        Checker.Selects(SimpleNameModel, arch => arch.Types.Named("Order.Line"))
            .ShouldBeEmpty();
        Checker.Selects(SimpleNameModel, arch => arch.Types.Named("Repository"))
            .ShouldBe(["App.A.Repository<T>"]);
    }

    [Fact]
    public void Where_EscapeHatch_NarrowsSubjectSelection()
    {
        // Where narrows Order* to just the Handler; it then passes the Handler suffix check.
        Checker.Run(NamingModel, arch =>
                arch.Rule("naming/x")
                    .Enforce(arch.Types.WithPrefix("Order")
                        .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal), "whose name ends with Handler")
                        .MustHaveSuffix("Handler"))
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void ProjectNoun_SelectsTypesInNamedProject()
    {
        // CompilationFactory compiles into project "TestProject"; a non-empty pass proves the noun resolved.
        Checker.Run(NamingModel, arch =>
                arch.Rule("proj/x")
                    .Enforce(arch.Project("TestProject").MustHaveNameMatching("*"))
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustBeSealed_HoldsAndFlagsUnsealed()
    {
        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/sealed")
                    .Enforce(arch.Types.WithPrefix("Sealed").MustBeSealed())
                    .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/sealed")
                    .Enforce(arch.Types.WithPrefix("Open").MustBeSealed())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Shape.OpenThing"]);
    }

    [Fact]
    public void MustBeStatic_HoldsAndFlagsNonStatic()
    {
        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/static")
                    .Enforce(arch.Types.WithPrefix("Static").MustBeStatic())
                    .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/static")
                    .Enforce(arch.Types.WithPrefix("Open").MustBeStatic())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Shape.OpenThing"]);
    }

    [Fact]
    public void MustBeAbstract_HoldsAndFlagsConcrete()
    {
        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/abstract")
                    .Enforce(arch.Types.WithPrefix("Abstract").MustBeAbstract())
                    .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/abstract")
                    .Enforce(arch.Types.WithPrefix("Sealed").MustBeAbstract())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Shape.SealedThing"]);
    }

    [Fact]
    public void MustBePublic_HoldsAndFlagsInternal()
    {
        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/public")
                    .Enforce(arch.Types.WithPrefix("Public").MustBePublic())
                    .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/public")
                    .Enforce(arch.Types.WithPrefix("Internal").MustBePublic())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Shape.InternalThing"]);
    }

    [Fact]
    public void MustBeInternal_HoldsAndFlagsPublic()
    {
        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/internal")
                    .Enforce(arch.Types.WithPrefix("Internal").MustBeInternal())
                    .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/internal")
                    .Enforce(arch.Types.WithPrefix("Public").MustBeInternal())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Shape.PublicThing"]);
    }

    [Fact]
    public void StaticClass_IsNeitherSealedNorAbstract_ThroughTheVerbs()
    {
        // Normalization visible at the verb layer: a static class fails both MustBeSealed and
        // MustBeAbstract (it is neither in C# declaration semantics).
        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/sealed")
                    .Enforce(arch.Types.WithPrefix("Static").MustBeSealed())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Shape.StaticThing"]);

        Checker.Run(ShapeModel, arch =>
                arch.Rule("shape/abstract")
                    .Enforce(arch.Types.WithPrefix("Static").MustBeAbstract())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Shape.StaticThing"]);
    }

    [Fact]
    public void Must_EscapeHatch_ReachesIsRecord()
    {
        const string source = """
                              namespace App.Events
                              {
                                  public record OrderCreated(int Id);
                                  public class OrderHandler {}
                              }
                              """;

        Checker.Run(source, arch =>
                arch.Rule("events/records")
                    .Enforce(arch.Types.InNamespace("App.Events.*").Must(t => t.IsRecord, "be a record"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Events.OrderHandler"]);
    }

    [Fact]
    public void Where_EscapeHatch_ReachesFilePaths()
    {
        // Two-file input; the Where narrows by FilePaths. Self-guarding: if FilePaths were empty the
        // Where matches nothing and the empty-subject default FAILS the rule — Passed proves both
        // population and reachability end-to-end.
        CodebaseModel codebase = CompilationFactory.Extract(
            "App.Files",
            ("Special.cs", "namespace App.Files { public class SpecialThing {} }"),
            ("Normal.cs", "namespace App.Files { public class NormalWidget {} }"));

        Checker.Run(codebase, arch =>
                arch.Rule("files/special")
                    .Enforce(arch.Types
                        .Where(t => t.FilePaths.Any(p => p.EndsWith("Special.cs", StringComparison.Ordinal)),
                            "declared in `Special.cs`")
                        .MustHaveSuffix("Thing"))
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void Where_ThrowingSubjectPredicate_SurfacesRuleErrorNamingTheHatch()
    {
        // A subject-selection Where predicate that throws becomes a contained RuleError, not an aborted run;
        // the detail names the `Where` hatch and echoes the thrown exception (SelectionEvaluator.InvokePredicate).
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("throwing/x")
                    .Enforce(arch.Types.Where(_ => throw new Exception("boom"), "d").MustHavePrefix("I"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetailContaining(
            ViolationKind.RuleError, "the `Where` predicate threw Exception", "boom");
    }
}
