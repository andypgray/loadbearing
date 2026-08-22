using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The mutability verb <c>MustBeGetOnly</c> over the fast path (GRAMMAR §5.7, §4.6): a property is red
///     unless it declares no setter accessor at all. Properties-only by receiver type — it lives on
///     <see cref="Fluent.PropertySelection" />, so the other projections cannot spell it.
/// </summary>
/// <remarks>
///     Two decisions are pinned here rather than left to be discovered. <b>Strict</b>: an init-only setter
///     reds, because get-only is a claim about the declaration, not about when the write is allowed to
///     happen. <b>Accessibility-blind</b>: a <c>private set</c> reds, because the setter exists whatever
///     can reach it. Violations are <see cref="ViolationKind.MemberShape" />, identity the member's DocId
///     riding <see cref="BaselineEntry.ForSubject" />.
/// </remarks>
public sealed class MustBeGetOnlyVerbTests
{
    // Every property shape the verb distinguishes, in one scene: two auto-settable, two narrower-setter
    // (the accessibility-blind pair), one auto get-only, an expression-bodied and a manual getter, a static
    // settable and a static get-only (the adjective pair), and an init-only setter on a second type.
    private const string Scene = """
                                 namespace App.Values
                                 {
                                     public class Order
                                     {
                                         public string Reference { get; set; }
                                         public int Total { get; private set; }
                                         public int Draft { get; internal set; }
                                         public int Count { get; }
                                         public int Doubled => Count * 2;
                                         public int Manual { get { return Count; } }
                                         public static int Limit { get; set; }
                                         public static int Ceiling { get; }
                                     }
                                     public class Line
                                     {
                                         public string Name { get; init; }
                                         public decimal Price { get; }
                                     }
                                 }
                                 """;

    // Two settable properties beside a get-only one, so the ratchet has an identity to bless and another to
    // keep red.
    private const string RatchetScene = """
                                        namespace App.Values
                                        {
                                            public class Order
                                            {
                                                public string Reference { get; set; }
                                                public int Total { get; set; }
                                                public int Count { get; }
                                            }
                                        }
                                        """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel RatchetModel = CompilationFactory.Extract(RatchetScene);

    [Fact]
    public void MustBeGetOnly_SettableProperty_FailsWithMemberIdAndDeclarationSite()
    {
        // A member-shape violation has no edge to cite, so the file:line an agent jumps to is where the
        // settable property is declared — its own declaration sites, carried as evidence.
        Checker.Run(SceneModel, arch =>
                arch.Rule("domain/values-immutable")
                    .Enforce(arch.Namespace("App.Values.*").Properties.WithPrefix("Reference").MustBeGetOnly())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithMemberAtSites("P:App.Values.Order.Reference", ["Test.cs:5"]);
    }

    [Fact]
    public void MustBeGetOnly_GetOnlyProperties_PassClean()
    {
        // An auto get-only property on each of the two types: no setter accessor, so nothing to red.
        Checker.Run(SceneModel, arch =>
                arch.Rule("domain/values-immutable")
                    .Enforce(arch.Namespace("App.Values.*").Properties.WithPrefix("Count").MustBeGetOnly())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(SceneModel, arch =>
                arch.Rule("domain/values-immutable")
                    .Enforce(arch.Namespace("App.Values.*").Properties.WithPrefix("Price").MustBeGetOnly())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBeGetOnly_EmptySubject_FailsWithMemberMessage()
    {
        // An empty member subject fails the rule with the member-flavored message (GRAMMAR §4.6), exactly as
        // every other member verb — the mutability verbs take the same subject gate.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("domain/empty")
                    .Enforce(arch.Namespace("Nowhere.*").Properties.MustBeGetOnly())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptyMemberSubjectMessage);
    }

    [Fact]
    public void MustBeGetOnly_GrandfatheredSettableProperty_NewOneStaysRed()
    {
        // Identity is the member's own DocId (GRAMMAR §4.3, §4.6), one entry per settable property.
        // Order.Reference is blessed; Order.Total is a distinct identity the same rule keeps red.
        BaselineIndex index = Checker.Baselines(
            "domain/values-immutable", BaselineEntry.ForSubject("P:App.Values.Order.Reference"));

        RuleResult result = Checker.Run(RatchetModel, index, arch =>
                arch.Rule("domain/values-immutable")
                    .Migrate(
                        "Some values still expose setters.",
                        arch.Namespace("App.Values.*").Properties.MustBeGetOnly())
                    .Because("A value another thread can write is not a value."))
            .Single();

        result.MemberShapeSubjects()
            .ShouldBe(["P:App.Values.Order.Total"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void MustBeGetOnly_InitOnlySetter_Reds()
    {
        // THE ratified boundary: `{ get; init; }` declares a setter, so it reds. Get-only is a claim about
        // the declaration, not about when the write is allowed to happen — an init-only property is written
        // exactly once, and the verb still refuses it. The narrower "set banned, init fine" reading stays
        // reachable through the escape hatch, because HasInitOnlySetter records the setter's kind.
        FailedMemberIds(arch => arch.Namespace("App.Values.*").Properties.WithPrefix("Name")
                .MustBeGetOnly())
            .ShouldBe(["P:App.Values.Line.Name"]);
    }

    [Fact]
    public void MustBeGetOnly_PrivateAndInternalSetters_Red()
    {
        // Accessibility-blind: a `private set` and an `internal set` are setters, so both red. The verb tests
        // whether the declaration has a setter, never whether a caller outside the type could reach it.
        FailedMemberIds(arch => arch.Namespace("App.Values.*").Properties.WithPrefix("Total")
                .MustBeGetOnly())
            .ShouldBe(["P:App.Values.Order.Total"]);

        FailedMemberIds(arch => arch.Namespace("App.Values.*").Properties.WithPrefix("Draft")
                .MustBeGetOnly())
            .ShouldBe(["P:App.Values.Order.Draft"]);
    }

    [Fact]
    public void MustBeGetOnly_ExpressionBodiedAndManualGetter_Pass()
    {
        // Neither an expression body nor a hand-written getter block declares a setter, so both green — the
        // fact is read off the symbol's SetMethod, not off the property's syntax.
        Checker.Run(SceneModel, arch =>
                arch.Rule("domain/values-immutable")
                    .Enforce(arch.Namespace("App.Values.*").Properties.WithPrefix("Doubled").MustBeGetOnly())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(SceneModel, arch =>
                arch.Rule("domain/values-immutable")
                    .Enforce(arch.Namespace("App.Values.*").Properties.WithPrefix("Manual").MustBeGetOnly())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBeGetOnly_ComposesWithTheStaticAdjective_NarrowsToStaticProperties()
    {
        // The adjective preserves PropertySelection, so the verb is still reachable after it — this line
        // would not compile if .ThatAreStatic() returned a bare MemberSelection. Self-guarding: the four
        // settable INSTANCE properties would red alongside Limit had the adjective failed to narrow.
        FailedMemberIds(arch => arch.Namespace("App.Values.*").Properties.ThatAreStatic()
                .MustBeGetOnly())
            .ShouldBe(["P:App.Values.Order.Limit"]);
    }

    // One rule over the shared scene, so the rows above differ by their subject alone — the id and the
    // reason are written once here rather than at each call.
    private static IReadOnlyList<string> FailedMemberIds(Func<Arch, Constraint> constraint)
    {
        return Checker.Run(SceneModel, arch => arch.Rule("domain/values-immutable")
                .Enforce(constraint(arch))
                .Because("b"))
            .Single()
            .MemberShapeSubjects();
    }
}
