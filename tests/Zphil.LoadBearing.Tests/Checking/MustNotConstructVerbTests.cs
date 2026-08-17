using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The construction verb <c>MustNotConstruct</c> over the fast path (GRAMMAR §4.5, §4.3, §5.3): a
///     construction edge trips only where a subject <c>new</c>s a forbidden type (explicit and target-typed),
///     the "you may use it; you may not create it" acceptance (bare references never trip), the sanctioned-root
///     exemption via <c>.Except</c>, the type-pair ratchet (one edge grandfathered, a new pair stays red), the
///     inert-target warning, and the pinned human line + JSON kind. Violation identity is the (source,
///     constructed) type pair, overload-indifferent, riding <see cref="BaselineEntry.ForEdge" /> unchanged.
/// </summary>
public sealed class MustNotConstructVerbTests
{
    // Widgets.Widget is the DI-resolved type nobody may `new`. WidgetFactory `new`s it (the red edge);
    // WidgetConsumer only USES it — a ctor param, a held field, a returned value — every channel a reference
    // edge, never a construction (the acceptance the verb is built around).
    private const string Scene = """
                                 namespace Widgets
                                 {
                                     public class Widget {}
                                 }
                                 namespace App
                                 {
                                     using Widgets;
                                     public class WidgetFactory
                                     {
                                         public Widget Make() => new Widget();
                                     }
                                     public class WidgetConsumer
                                     {
                                         private readonly Widget _held;
                                         public WidgetConsumer(Widget w) { _held = w; }
                                         public Widget Current() => _held;
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustNotConstruct_SubjectConstructsBannedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("di/no-new-widget")
                    .Enforce(arch.Namespace("App.*").MustNotConstruct(arch.Namespace("Widgets.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdge(ViolationKind.Construction, "App.WidgetFactory", "Widgets.Widget");

        string block = result.HumanBlock();
        block.ShouldContain("App.WidgetFactory constructs Widgets.Widget");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotConstruct_SubjectOnlyReferencesTarget_PassesCleanWithNoWarnings()
    {
        // WidgetConsumer takes Widget as a ctor param, holds it as a field, and returns it — reference edges
        // all — but never `new`s it. "You may use it; you may not create it": construction is silent here even
        // though MustNotReference on the same subject would fire. The target resolves, so this is not inert.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("di/no-new-widget")
                    .Enforce(arch.Namespace("App.*").WithSuffix("Consumer")
                        .MustNotConstruct(arch.Namespace("Widgets.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotConstruct_TargetTypedNew_IsRed()
    {
        // The modern-codebase spelling `new()` mints the same construction edge as `new Widget()` (GRAMMAR
        // §4.5), so the verb catches it identically — pinned early, per the ratified semantics.
        const string source = """
                              namespace Widgets { public class Widget {} }
                              namespace App
                              {
                                  using Widgets;
                                  public static class Factory
                                  {
                                      public static void Take(Widget w) {}
                                      public static void Call() => Take(new());
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("di/no-new-widget")
                    .Enforce(arch.Namespace("App.*").MustNotConstruct(arch.Namespace("Widgets.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdge(ViolationKind.Construction, "App.Factory", "Widgets.Widget");
    }

    [Fact]
    public void MustNotConstruct_ExceptSanctionedRoot_ExemptsRootButOtherSiteStaysRed()
    {
        // The DI shape: the sanctioned composition root may `new` the concrete type; everyone else may not.
        // .Except carves the root out of the subject set, so its construction is exempt while the ordinary
        // factory in the same App subtree stays red.
        const string source = """
                              namespace Widgets { public class Widget {} }
                              namespace App { public class WidgetFactory { public Widgets.Widget Make() => new Widgets.Widget(); } }
                              namespace App.Composition { public class Root { public Widgets.Widget Compose() => new Widgets.Widget(); } }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("di/no-new-widget")
                    .Enforce(arch.Namespace("App.*").Except(arch.Namespace("App.Composition.*"))
                        .MustNotConstruct(arch.Namespace("Widgets.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Construction, ["App.WidgetFactory -> Widgets.Widget"]);
    }

    [Fact]
    public void MustNotConstruct_GrandfatheredEdgePasses_NewConstructionStaysRed()
    {
        // The construction ratchet keys the (source, constructed) type pair (GRAMMAR §4.3): WidgetFactory is
        // grandfathered for `new`ing Widget, but its NEW `new Gadget()` is not covered → red.
        const string source = """
                              namespace Widgets { public class Widget {} public class Gadget {} }
                              namespace App
                              {
                                  using Widgets;
                                  public class WidgetFactory
                                  {
                                      public Widget A() => new Widget();
                                      public Gadget B() => new Gadget();
                                  }
                              }
                              """;
        BaselineIndex index = Checker.Baselines("di/no-new", BaselineEntry.ForEdge("T:App.WidgetFactory", "T:Widgets.Widget"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("di/no-new")
                    .Migrate("legacy direct construction", arch.Namespace("App.*")
                        .MustNotConstruct(arch.Namespace("Widgets.*")))
                    .Because("resolve via DI"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Construction, ["App.WidgetFactory -> Widgets.Gadget"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void MustNotConstruct_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate, exactly as MustNotReference's inert-target semantics (§4.5).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("di/inert")
                    .Enforce(arch.Namespace("App.*").MustNotConstruct(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void JsonReportRenderer_ConstructionViolation_EmitsConstructionKindAndTargetAndOmitsMemberSlots()
    {
        // The JSON kind string is "construction" and the constructed type rides the existing `target` field —
        // no new slot, schemaVersion stays 3, so member/subject slots stay omitted (null) as before.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("di/no-new-widget")
                .Enforce(arch.Namespace("App.*").MustNotConstruct(arch.Namespace("Widgets.*")))
                .Because("b"));

        report.ShouldRenderEdgeViolation("construction", "App.WidgetFactory", "Widgets.Widget");
    }
}
