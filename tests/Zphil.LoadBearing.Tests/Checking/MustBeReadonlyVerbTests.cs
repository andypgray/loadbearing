using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The mutability verb <c>MustBeReadonly</c> over the fast path (GRAMMAR §5.7, §4.6): a field is red
///     unless it is declared <c>readonly</c> or <c>const</c>. Fields-only by receiver type — it lives on
///     <see cref="Fluent.FieldSelection" />, so the other projections cannot spell it.
/// </summary>
/// <remarks>
///     <b>A const field satisfies the verb</b>, and that decision is pinned here rather than left to each
///     spec: const is readonly's superset — readonly, static and compile-time at once — so redding one
///     would demand something weaker than what is already there. It lives in the checker because
///     <c>.Fields.ThatAreStatic()</c> sweeps every constant a type declares, and a rule author who had to
///     remember the exception would eventually not. Violations are
///     <see cref="ViolationKind.MemberShape" />, identity the member's DocId riding
///     <see cref="BaselineEntry.ForSubject" />.
/// </remarks>
public sealed class MustBeReadonlyVerbTests
{
    // Every field shape the verb distinguishes, in one scene: a const and a static readonly (both green),
    // a writable static (the red), a readonly instance field (green) and a writable instance field, which
    // is what makes the static-adjective row below self-guarding.
    private const string Scene = """
                                 namespace App.State
                                 {
                                     public class Ledger
                                     {
                                         public const int MaxRows = 500;
                                         public static readonly string DefaultFormat = "csv";
                                         public static int RenderCount;
                                         public readonly int Opening;
                                         public int Running;
                                     }
                                 }
                                 """;

    // Two writable fields beside a readonly one, so the ratchet has an identity to bless and another to
    // keep red.
    private const string RatchetScene = """
                                        namespace App.State
                                        {
                                            public class Ledger
                                            {
                                                public static int RenderCount;
                                                public static int PurgeCount;
                                                public static readonly string DefaultFormat = "csv";
                                            }
                                        }
                                        """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel RatchetModel = CompilationFactory.Extract(RatchetScene);

    [Fact]
    public void MustBeReadonly_WritableField_FailsWithMemberIdAndDeclarationSite()
    {
        // A member-shape violation has no edge to cite, so the file:line an agent jumps to is where the
        // writable field is declared — its own declaration sites, carried as evidence.
        Checker.Run(SceneModel, arch =>
                arch.Rule("state/no-static-mutable")
                    .Enforce(arch.Namespace("App.State.*").Fields.WithPrefix("RenderCount").MustBeReadonly())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithMemberAtSites("F:App.State.Ledger.RenderCount", ["Test.cs:7"]);
    }

    [Fact]
    public void MustBeReadonly_ReadonlyField_PassesClean()
    {
        // The two readonly declarations green independently of staticness — one static, one instance.
        Checker.Run(SceneModel, arch =>
                arch.Rule("state/no-static-mutable")
                    .Enforce(arch.Namespace("App.State.*").Fields.WithPrefix("DefaultFormat").MustBeReadonly())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(SceneModel, arch =>
                arch.Rule("state/no-static-mutable")
                    .Enforce(arch.Namespace("App.State.*").Fields.WithPrefix("Opening").MustBeReadonly())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBeReadonly_EmptySubject_FailsWithMemberMessage()
    {
        // An empty member subject fails the rule with the member-flavored message (GRAMMAR §4.6), exactly as
        // every other member verb — the mutability verbs take the same subject gate.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("state/empty")
                    .Enforce(arch.Namespace("Nowhere.*").Fields.MustBeReadonly())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptyMemberSubjectMessage);
    }

    [Fact]
    public void MustBeReadonly_GrandfatheredWritableField_NewOneStaysRed()
    {
        // Identity is the member's own DocId (GRAMMAR §4.3, §4.6), one entry per writable field.
        // Ledger.RenderCount is blessed; Ledger.PurgeCount is a distinct identity the same rule keeps red.
        BaselineIndex index = Checker.Baselines(
            "state/no-static-mutable", BaselineEntry.ForSubject("F:App.State.Ledger.RenderCount"));

        RuleResult result = Checker.Run(RatchetModel, index, arch =>
                arch.Rule("state/no-static-mutable")
                    .Migrate(
                        "Some counters are still written in place.",
                        arch.Namespace("App.State.*").Fields.ThatAreStatic().MustBeReadonly())
                    .Because("A writable static is process-wide state nothing in the type system declares."))
            .Single();

        result.MemberShapeSubjects()
            .ShouldBe(["F:App.State.Ledger.PurgeCount"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void MustBeReadonly_ConstField_Passes()
    {
        // Const-satisfies, pinned as behaviour: a const field is readonly's superset, so it greens. Nothing
        // in the spec says so — the checker's predicate is `IsReadOnly || IsConst`, and IsConst is a
        // disjoint fact (a const field reports IsReadOnly false), which is what makes the second arm load-
        // bearing rather than decorative.
        Checker.Run(SceneModel, arch =>
                arch.Rule("state/no-static-mutable")
                    .Enforce(arch.Namespace("App.State.*").Fields.WithPrefix("MaxRows").MustBeReadonly())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBeReadonly_StaticAdjective_ExcludesInstanceFields()
    {
        // The adjective preserves FieldSelection, so the verb is still reachable after it — this line would
        // not compile if .ThatAreStatic() returned a bare MemberSelection. Self-guarding: Running is an
        // equally writable INSTANCE field that would red alongside RenderCount had the adjective failed to
        // narrow, and the const is in the subject (a const is static) yet still greens.
        FailedMemberIds(arch => arch.Namespace("App.State.*").Fields.ThatAreStatic()
                .MustBeReadonly())
            .ShouldBe(["F:App.State.Ledger.RenderCount"]);
    }

    // One rule over the shared scene, so the rows above differ by their subject alone — the id and the
    // reason are written once here rather than at each call.
    private static IReadOnlyList<string> FailedMemberIds(Func<Arch, Constraint> constraint)
    {
        return Checker.Run(SceneModel, arch => arch.Rule("state/no-static-mutable")
                .Enforce(constraint(arch))
                .Because("b"))
            .Single()
            .MemberShapeSubjects();
    }
}
