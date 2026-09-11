using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The correspondence verb <c>MustHaveExactlyOneCounterpart</c> over the fast path (GRAMMAR §5.3, §4.1,
///     §4.3): per subject the <c>named:</c> template derives a name — every <c>{Name}</c> replaced by the
///     subject's simple name — and exactly one type in the <c>among:</c> selection must carry it. Zero
///     counterparts and several are both red, so the rule states a one-to-one law rather than a
///     one-way existence check.
/// </summary>
/// <remarks>
///     The verb reads names off the model the selections already resolve, so it adds no extracted fact.
///     Both arms mint <see cref="ViolationKind.Shape" /> keyed on the SUBJECT symbol ID
///     (<see cref="BaselineEntry.ForSubject" />) and differ only in evidence: the missing arm points at the
///     subject's own declaration, the ambiguous arm at the counterparts that collide. Counterparts are
///     evidence and never identity, which is what keeps a grandfathered subject grandfathered when the arm
///     flips. An <c>among:</c> selection matching nothing reds every subject and raises no warning — the
///     shape family has no inert-target warning, and operand emptiness is not subject emptiness.
/// </remarks>
public sealed class MustHaveExactlyOneCounterpartVerbTests
{
    // One class with its interface and one without: the green case and the missing arm in one scene.
    private const string Scene = """
                                 namespace App
                                 {
                                     public class Order {}
                                     public interface IOrder {}
                                     public class Invoice {}
                                 }
                                 """;

    // The same scene with `IInvoice` declared twice in different namespaces — the ambiguous arm, which no
    // committed fixture solution can stage because two same-named interfaces are what a real tree avoids.
    private const string AmbiguousScene = """
                                          namespace App
                                          {
                                              public class Order {}
                                              public interface IOrder {}
                                              public class Invoice {}
                                          }
                                          namespace A
                                          {
                                              public interface IInvoice {}
                                          }
                                          namespace B
                                          {
                                              public interface IInvoice {}
                                          }
                                          """;

    // Two uncovered classes beside one unrelated interface, so the ratchet has a blessed subject and a new
    // one to tell apart.
    private const string StraysScene = """
                                       namespace App
                                       {
                                           public interface IOrder {}
                                           public class Invoice {}
                                           public class Receipt {}
                                       }
                                       """;

    // `OrderContractOrder` carries the template's BOTH substitutions; `InvoiceContract` carries only the
    // first, so it is the decoy a single-occurrence replace would settle for.
    private const string PlaceholderScene = """
                                            namespace App
                                            {
                                                public class Order {}
                                                public class Invoice {}
                                                public interface OrderContractOrder {}
                                                public interface InvoiceContract {}
                                            }
                                            """;

    // One generic class, and one name declared at two arities — the shapes that decide whether matching is
    // arity-free and whether "exactly one" is read per subject or as a bijection.
    private const string ArityScene = """
                                      namespace App
                                      {
                                          public class Repository<T> {}
                                          public interface IRepository {}
                                          public class Handler {}
                                          public class Handler<T> {}
                                          public interface IHandler {}
                                      }
                                      """;

    // A nested subject and a nested counterpart, so the leaf-name reading is exercised on both sides at once.
    private const string NestedScene = """
                                       namespace App
                                       {
                                           public class Order
                                           {
                                               public class Line {}
                                           }
                                           public class Contracts
                                           {
                                               public interface ILine {}
                                           }
                                       }
                                       """;

    // The counterpart differs from the derived name by case alone.
    private const string CaseScene = """
                                     namespace App
                                     {
                                         public class Order {}
                                         public interface iorder {}
                                     }
                                     """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustHaveExactlyOneCounterpart_OneMatchingName_PassesClean()
    {
        // The satisfied law: one subject, one type in the among selection carrying the derived name.
        Checker.Run(SceneModel, arch =>
                arch.Rule("naming/one-interface-per-service")
                    .Enforce(arch.Types.WithNameMatching("Order")
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_NoCounterpart_FailsAtTheSubjectsOwnDeclarationSite()
    {
        // The missing arm has no counterpart to point at, so the file:line an agent jumps to is where the
        // uncovered subject is declared — its own declaration sites, exactly as the other shape verbs cite.
        Checker.Run(SceneModel, arch =>
                arch.Rule("naming/one-interface-per-service")
                    .Enforce(arch.Types.OfKind(TypeKind.Class)
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjectAtSites("App.Invoice", ["Test.cs:5"]);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_SeveralCounterparts_FailAtTheCounterpartDeclarationSites()
    {
        // The ambiguous arm inverts the evidence: the subject is lawful where it stands and the edit that
        // resolves the collision happens at one of the counterparts, so those are the sites. Ordered by
        // (file, line), which is the order a report prints them in.
        Checker.Run(CompilationFactory.Extract(AmbiguousScene), arch =>
                arch.Rule("naming/one-interface-per-service")
                    .Enforce(arch.Types.OfKind(TypeKind.Class)
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjectAtSites("App.Invoice", ["Test.cs:9", "Test.cs:13"]);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_EveryNamePlaceholder_IsSubstituted()
    {
        // Both claims in one row: `Order` is green only because BOTH occurrences were replaced
        // (`OrderContractOrder` exists), and `Invoice` is red although `InvoiceContract` exists — which is
        // what a replace that stopped after the first occurrence would have settled for.
        Checker.Run(CompilationFactory.Extract(PlaceholderScene), arch =>
                arch.Rule("naming/contract-per-type")
                    .Enforce(arch.Types.OfKind(TypeKind.Class)
                        .MustHaveExactlyOneCounterpart(
                            among: arch.Types.OfKind(TypeKind.Interface), named: "{Name}Contract{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["App.Invoice"]);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_GenericSubject_MatchesArityFree()
    {
        // The subject's simple name is Roslyn's — arity-free — so `Repository<T>` derives `IRepository` and
        // an author never spells the backtick arity a metadata name would carry.
        Checker.Run(CompilationFactory.Extract(ArityScene), arch =>
                arch.Rule("naming/interface-per-repository")
                    .Enforce(arch.Types.WithNameMatching("Repository")
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_TwoAritiesOfOneName_BothMatchTheSingleCounterpart()
    {
        // "Exactly one" is counted PER SUBJECT, not as a bijection over the two sets: `Handler` and
        // `Handler<T>` are two subjects that derive one name, and the single `IHandler` satisfies both. A
        // pairing reading would have to red one of them, and the subject count is asserted so the row
        // cannot pass by sweeping only one.
        RuleResult result = Checker.Run(CompilationFactory.Extract(ArityScene), arch =>
                arch.Rule("naming/interface-per-handler")
                    .Enforce(arch.Types.WithNameMatching("Handler")
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
        result.SubjectTypes.ShouldBe(2);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_NestedTypes_MatchOnLeafNames()
    {
        // Both sides read the leaf: the subject `App.Order.Line` derives `ILine`, and the counterpart
        // `App.Contracts.ILine` carries it — neither name is qualified by its declaring type.
        Checker.Run(CompilationFactory.Extract(NestedScene), arch =>
                arch.Rule("naming/interface-per-line")
                    .Enforce(arch.Types.WithNameMatching("Line")
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_NameMatching_IsCaseSensitive()
    {
        // Ordinal on both halves — the substitution and the name comparison — so `iorder` is a different
        // name from `IOrder` and not a counterpart. Case-insensitivity here would silently bless the very
        // naming drift the verb exists to find.
        Checker.Run(CompilationFactory.Extract(CaseScene), arch =>
                arch.Rule("naming/one-interface-per-service")
                    .Enforce(arch.Types.OfKind(TypeKind.Class)
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjectAtSites("App.Order", ["Test.cs:3"]);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_AmongMatchingNothing_RedsEverySubjectAndWarnsAboutNone()
    {
        // An among selection naming nothing can hold no counterpart, so every subject reds — a false red,
        // and deliberately a loud one. The per-operand emptiness failure guards a union SUBJECT (§9) and the
        // inert-target warning belongs to the forbidden-set verbs (§4.1); neither reaches this position, so
        // the report carries the reds and nothing else.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("naming/absent-among")
                    .Enforce(arch.Types.OfKind(TypeKind.Class)
                        .MustHaveExactlyOneCounterpart(among: arch.Namespace("Nowhere.*"), named: "I{Name}"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Order", "App.Invoice"]);

        result.ShouldSatisfyAllConditions(
            () => result.Warnings.ShouldBeEmpty(),
            () => result.Violators(ViolationKind.EmptySubject, violation => violation.Detail!)
                .ShouldBeEmpty());
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), which is what
        // keeps an empty among and an empty subject distinguishable outcomes rather than one blur.
        Checker.Run(SceneModel, arch =>
                arch.Rule("naming/empty")
                    .Enforce(arch.Namespace("Nowhere.*")
                        .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_BlessedSubject_StaysGrandfatheredWhenTheArmFlips()
    {
        // Identity is the subject symbol ID on BOTH arms, so one entry blesses `App.Invoice` whether it has
        // no counterpart or two — the counterparts are evidence, and evidence must not move a key. Were the
        // ambiguous arm keyed on what it points at, adding a second `IInvoice` would silently un-bless the
        // debt and read as a new violation.
        BaselineEntry blessed = BaselineEntry.ForSubject("T:App.Invoice");
        BaselineIndex index = Checker.Baselines("naming/one-interface-per-service", blessed);

        RuleResult missing = Run(SceneModel, index);
        RuleResult ambiguous = Run(CompilationFactory.Extract(AmbiguousScene), index);

        missing.ShouldHaveGrandfathered(1);
        ambiguous.ShouldHaveGrandfathered(1);

        // The pinned identity form on each arm: the type-tagged DocId, with the counterparts nowhere in it.
        Run(SceneModel, BaselineIndex.Empty)
            .Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe([blessed]);
        Run(CompilationFactory.Extract(AmbiguousScene), BaselineIndex.Empty)
            .Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe([blessed]);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_GrandfatheredStrayPasses_NewStrayStaysRed()
    {
        // One entry per uncovered subject (GRAMMAR §4.3): `App.Invoice` is blessed, and `App.Receipt` — a
        // distinct identity with the same complaint — stays red.
        BaselineIndex index = Checker.Baselines(
            "naming/one-interface-per-service", BaselineEntry.ForSubject("T:App.Invoice"));

        RuleResult result = Checker.Run(CompilationFactory.Extract(StraysScene), index, arch =>
                arch.Rule("naming/one-interface-per-service")
                    .Migrate(
                        "Some types predate the one-interface-per-service convention.",
                        arch.Types.OfKind(TypeKind.Class)
                            .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("A service with no interface cannot be substituted in a test."))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Receipt"]);
        result.ShouldHaveGrandfathered(1);
    }

    // The one rule both ratchet rows run, over whichever scene and baseline they hand it — so the flip is
    // the only thing that varies between the arms.
    private static RuleResult Run(CodebaseModel codebase, BaselineIndex baselines)
    {
        return Checker.Run(codebase, baselines, arch =>
                arch.Rule("naming/one-interface-per-service")
                    .Migrate(
                        "Some types predate the one-interface-per-service convention.",
                        arch.Types.OfKind(TypeKind.Class)
                            .MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
                    .Because("A service with no interface cannot be substituted in a test."))
            .Single();
    }
}
