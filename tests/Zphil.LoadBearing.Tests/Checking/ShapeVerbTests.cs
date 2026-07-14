using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The shape/naming/escape verbs (GRAMMAR §5.3) and the adjective + noun vocabulary that feeds
///     them (§5.1–§5.2): every adjective (InNamespace, OfKind, WithSuffix, WithPrefix,
///     WithNameMatching, Except, Where) and the Project noun get at least one pass/fail pin here.
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

    [Fact]
    public void OfKind_And_MustHavePrefix_FlagInterfaceWithoutIPrefix()
    {
        RuleResult result = Checker.Run(Naming, arch =>
                arch.Rule("naming/interfaces")
                    .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("App.Naming.*").MustHavePrefix("I"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects().ShouldBe(["App.Naming.Bar"]);
    }

    [Fact]
    public void WithPrefix_And_MustHaveSuffix_FlagMismatchedSuffix()
    {
        RuleResult result = Checker.Run(Naming, arch =>
                arch.Rule("naming/handlers")
                    .Enforce(arch.Types.WithPrefix("Order").MustHaveSuffix("Handler"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects().ShouldBe(["App.Naming.OrderController"]);
    }

    [Fact]
    public void WithNameMatching_And_MustHaveNameMatching_Hold()
    {
        Checker.Run(Naming, arch =>
                arch.Rule("naming/repo")
                    .Enforce(arch.Types.WithNameMatching("*Repo*").MustHaveNameMatching("*Repository"))
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
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

        result.ShapeSubjects().ShouldBe(["App.Bad.Widget2"]);
    }

    [Fact]
    public void Must_EscapeHatch_HoldsAndFails()
    {
        Checker.Run(Naming, arch =>
                arch.Rule("style/short")
                    .Enforce(arch.Types.WithPrefix("X").Must(t => t.Name.Length <= 3, "keep names at or under 3 characters"))
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Naming, arch =>
                arch.Rule("style/short")
                    .Enforce(arch.Types.WithPrefix("OrderC").Must(t => t.Name.Length <= 3, "keep names at or under 3 characters"))
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Naming.OrderController"]);
    }

    [Fact]
    public void Except_SubtractsPayloadSelection()
    {
        // Subjects = Order* except *Handler = {OrderController}; it fails the Handler suffix.
        RuleResult result = Checker.Run(Naming, arch =>
                arch.Rule("naming/x")
                    .Enforce(arch.Types.WithPrefix("Order").Except(arch.Types.WithSuffix("Handler")).MustHaveSuffix("Handler"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects().ShouldBe(["App.Naming.OrderController"]);
    }

    [Fact]
    public void Where_EscapeHatch_NarrowsSubjectSelection()
    {
        // Where narrows Order* to just the Handler; it then passes the Handler suffix check.
        Checker.Run(Naming, arch =>
                arch.Rule("naming/x")
                    .Enforce(arch.Types.WithPrefix("Order")
                        .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal), "whose name ends with Handler")
                        .MustHaveSuffix("Handler"))
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public void ProjectNoun_SelectsTypesInNamedProject()
    {
        // CompilationFactory compiles into project "TestProject"; a non-empty pass proves the noun resolved.
        Checker.Run(Naming, arch =>
                arch.Rule("proj/x")
                    .Enforce(arch.Project("TestProject").MustHaveNameMatching("*"))
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public void MustBeSealed_HoldsAndFlagsUnsealed()
    {
        Checker.Run(Shape, arch =>
                arch.Rule("shape/sealed")
                    .Enforce(arch.Types.WithPrefix("Sealed").MustBeSealed())
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Shape, arch =>
                arch.Rule("shape/sealed")
                    .Enforce(arch.Types.WithPrefix("Open").MustBeSealed())
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Shape.OpenThing"]);
    }

    [Fact]
    public void MustBeStatic_HoldsAndFlagsNonStatic()
    {
        Checker.Run(Shape, arch =>
                arch.Rule("shape/static")
                    .Enforce(arch.Types.WithPrefix("Static").MustBeStatic())
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Shape, arch =>
                arch.Rule("shape/static")
                    .Enforce(arch.Types.WithPrefix("Open").MustBeStatic())
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Shape.OpenThing"]);
    }

    [Fact]
    public void MustBeAbstract_HoldsAndFlagsConcrete()
    {
        Checker.Run(Shape, arch =>
                arch.Rule("shape/abstract")
                    .Enforce(arch.Types.WithPrefix("Abstract").MustBeAbstract())
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Shape, arch =>
                arch.Rule("shape/abstract")
                    .Enforce(arch.Types.WithPrefix("Sealed").MustBeAbstract())
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Shape.SealedThing"]);
    }

    [Fact]
    public void MustBePublic_HoldsAndFlagsInternal()
    {
        Checker.Run(Shape, arch =>
                arch.Rule("shape/public")
                    .Enforce(arch.Types.WithPrefix("Public").MustBePublic())
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Shape, arch =>
                arch.Rule("shape/public")
                    .Enforce(arch.Types.WithPrefix("Internal").MustBePublic())
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Shape.InternalThing"]);
    }

    [Fact]
    public void MustBeInternal_HoldsAndFlagsPublic()
    {
        Checker.Run(Shape, arch =>
                arch.Rule("shape/internal")
                    .Enforce(arch.Types.WithPrefix("Internal").MustBeInternal())
                    .Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Shape, arch =>
                arch.Rule("shape/internal")
                    .Enforce(arch.Types.WithPrefix("Public").MustBeInternal())
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Shape.PublicThing"]);
    }

    [Fact]
    public void StaticClass_IsNeitherSealedNorAbstract_ThroughTheVerbs()
    {
        // Normalization visible at the verb layer: a static class fails both MustBeSealed and
        // MustBeAbstract (it is neither in C# declaration semantics).
        Checker.Run(Shape, arch =>
                arch.Rule("shape/sealed")
                    .Enforce(arch.Types.WithPrefix("Static").MustBeSealed())
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Shape.StaticThing"]);

        Checker.Run(Shape, arch =>
                arch.Rule("shape/abstract")
                    .Enforce(arch.Types.WithPrefix("Static").MustBeAbstract())
                    .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["App.Shape.StaticThing"]);
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
            .Single().ShapeSubjects().ShouldBe(["App.Events.OrderHandler"]);
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
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }
}