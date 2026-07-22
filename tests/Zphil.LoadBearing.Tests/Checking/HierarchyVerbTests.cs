using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Checking.Targets;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The hierarchy adjectives and their constraint twins (GRAMMAR §5.2–§5.3): open generic matches
///     any construction (on the definition FullName), a closed or non-generic type matches that
///     construction exactly. Shared model extracted once from <see cref="Sources.Hierarchy" />.
/// </summary>
public sealed class HierarchyVerbTests
{
    private const string T = "Zphil.LoadBearing.Tests.Checking.Targets.";

    private static readonly CodebaseModel Model = CompilationFactory.Extract(Sources.Hierarchy);
    private static readonly CodebaseModel TransitiveModel = CompilationFactory.Extract(Sources.HierarchyTransitive);

    [Fact]
    public void Implementing_OpenGeneric_SelectsEveryConstruction()
    {
        // ZZZ prefix fails all subjects, so the shape violations reveal exactly who was selected.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing(typeof(IHandler<>)).MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects().ShouldBe([$"{T}OrderHandler", $"{T}TextHandler"]);
    }

    [Fact]
    public void Implementing_ClosedGeneric_SelectsOnlyThatConstruction()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing(typeof(IHandler<Order>)).MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects().ShouldBe([$"{T}OrderHandler"]);
    }

    [Fact]
    public void Implementing_NonGenericInterface_SelectsImplementer()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing(typeof(IThing)).MustHavePrefix("Widget"))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public void DerivedFrom_SelectsDeriver()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.DerivedFrom(typeof(ThingBase)).MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects().ShouldBe([$"{T}SubType"]);
    }

    [Fact]
    public void AttributedWith_SelectsAttributedType()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.AttributedWith(typeof(MarkAttribute)).MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects().ShouldBe([$"{T}Tagged"]);
    }

    [Fact]
    public void MustImplement_HoldsForImplementer_FailsForNonImplementer()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Widget").MustImplement(typeof(IThing))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        RuleResult failing = Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Gizmo").MustImplement(typeof(IThing))).Because("b"))
            .Single();
        failing.ShapeSubjects().ShouldBe([$"{T}Gizmo"]);
    }

    [Fact]
    public void MustDeriveFrom_HoldsForDeriver_FailsForNonDeriver()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubType").MustDeriveFrom(typeof(ThingBase))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("FreeType").MustDeriveFrom(typeof(ThingBase))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}FreeType"]);
    }

    [Fact]
    public void MustBeAttributedWith_HoldsForAttributed_FailsForBare()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Tagged").MustBeAttributedWith(typeof(MarkAttribute))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Plain").MustBeAttributedWith(typeof(MarkAttribute))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}Plain"]);
    }

    // ── negative twins: red where the positive matches an anchor, green on the inverse (GRAMMAR §5.3) ──

    [Fact]
    public void MustNotImplement_RedsImplementer_PassesForNonImplementer()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Widget").MustNotImplement(typeof(IThing))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}Widget"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Gizmo").MustNotImplement(typeof(IThing))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public void MustNotDeriveFrom_RedsDeriver_PassesForNonDeriver()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubType").MustNotDeriveFrom(typeof(ThingBase))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}SubType"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("FreeType").MustNotDeriveFrom(typeof(ThingBase))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public void MustNotBeAttributedWith_RedsAttributed_PassesForBare()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Tagged").MustNotBeAttributedWith(typeof(MarkAttribute))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}Tagged"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Plain").MustNotBeAttributedWith(typeof(MarkAttribute))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    // ── open-vs-closed generic anchors, negated (GRAMMAR §5.2) ──

    [Fact]
    public void MustNotImplement_ClosedGenericAnchor_RedsOnlyThatConstruction()
    {
        // typeof(IHandler<Order>) reds OrderHandler (that construction) but not TextHandler (IHandler<string>).
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("OrderHandler").MustNotImplement(typeof(IHandler<Order>))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}OrderHandler"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("TextHandler").MustNotImplement(typeof(IHandler<Order>))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public void MustNotImplement_OpenGenericAnchor_RedsEveryConstruction()
    {
        // typeof(IHandler<>) reds every construction — both OrderHandler and TextHandler.
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("OrderHandler").MustNotImplement(typeof(IHandler<>))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}OrderHandler"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("TextHandler").MustNotImplement(typeof(IHandler<>))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}TextHandler"]);
    }

    // ── matcher parity over the transitive/substitution/declared-only fixture (GRAMMAR §5.2, negated) ──

    [Fact]
    public void MustNotImplement_TransitiveInterfaceThroughBaseClass_Reds()
    {
        // WidgetChild : Widget : IThing — an interface reached through a base class still reds the ban (the
        // negative reads the full interface closure, exactly like the positive matcher).
        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("WidgetChild").MustNotImplement(typeof(IThing))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}WidgetChild"]);
    }

    [Fact]
    public void MustNotImplement_TypeArgumentSubstitutionThroughGenericBase_RedsClosedConstruction()
    {
        // SubstHandler : HandlerBase<Order> where HandlerBase<T> : IHandler<T> — the substituted IHandler<Order>
        // reds MustNotImplement(typeof(IHandler<Order>)) (the §5.2 substitution example, negated).
        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubstHandler").MustNotImplement(typeof(IHandler<Order>))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}SubstHandler"]);
    }

    [Fact]
    public void MustNotBeAttributedWith_DeclaredOnly_BaseAttributeDoesNotRedDerived()
    {
        // Attributes are declared-only (§5.2): [Mark] on AttrBase reds it, but AttrDerived : AttrBase does not
        // inherit the attribute, so the ban silently passes for the derived type.
        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("AttrBase").MustNotBeAttributedWith(typeof(MarkAttribute))).Because("b"))
            .Single().ShapeSubjects().ShouldBe([$"{T}AttrBase"]);

        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("AttrDerived").MustNotBeAttributedWith(typeof(MarkAttribute))).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }
}