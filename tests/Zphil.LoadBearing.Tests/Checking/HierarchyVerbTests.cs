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
    private static readonly CodebaseModel GenericAttributeModel = CompilationFactory.Extract(Sources.GenericAttributes);

    [Fact]
    public void Implementing_OpenGeneric_SelectsEveryConstruction()
    {
        // ZZZ prefix fails all subjects, so the shape violations reveal exactly who was selected.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing(typeof(IHandler<>))
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}OrderHandler", $"{T}TextHandler"]);
    }

    [Fact]
    public void Implementing_ClosedGeneric_SelectsOnlyThatConstruction()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing(typeof(IHandler<Order>))
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}OrderHandler"]);
    }

    [Fact]
    public void Implementing_NonGenericInterface_SelectsImplementer()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing(typeof(IThing))
                        .MustHavePrefix("Widget"))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
    }

    [Fact]
    public void DerivedFrom_SelectsDeriver()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.DerivedFrom(typeof(ThingBase))
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}SubType"]);
    }

    [Fact]
    public void AttributedWith_SelectsAttributedType()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.AttributedWith(typeof(MarkAttribute))
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}Tagged"]);
    }

    [Fact]
    public void MustImplement_HoldsForImplementer_FailsForNonImplementer()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Widget")
                    .MustImplement(typeof(IThing)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();

        RuleResult failing = Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Gizmo")
                    .MustImplement(typeof(IThing)))
                .Because("b"))
            .Single();
        failing.ShapeSubjects()
            .ShouldBe([$"{T}Gizmo"]);
    }

    [Fact]
    public void MustDeriveFrom_HoldsForDeriver_FailsForNonDeriver()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubType")
                    .MustDeriveFrom(typeof(ThingBase)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("FreeType")
                    .MustDeriveFrom(typeof(ThingBase)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}FreeType"]);
    }

    [Fact]
    public void MustBeAttributedWith_HoldsForAttributed_FailsForBare()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Tagged")
                    .MustBeAttributedWith(typeof(MarkAttribute)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Plain")
                    .MustBeAttributedWith(typeof(MarkAttribute)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Plain"]);
    }

    // ── negative twins: red where the positive matches an anchor, green on the inverse (GRAMMAR §5.3) ──

    [Fact]
    public void MustNotImplement_RedsImplementer_PassesForNonImplementer()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Widget")
                    .MustNotImplement(typeof(IThing)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Widget"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Gizmo")
                    .MustNotImplement(typeof(IThing)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotDeriveFrom_RedsDeriver_PassesForNonDeriver()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubType")
                    .MustNotDeriveFrom(typeof(ThingBase)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}SubType"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("FreeType")
                    .MustNotDeriveFrom(typeof(ThingBase)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotBeAttributedWith_RedsAttributed_PassesForBare()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Tagged")
                    .MustNotBeAttributedWith(typeof(MarkAttribute)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Tagged"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Plain")
                    .MustNotBeAttributedWith(typeof(MarkAttribute)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    // ── open-vs-closed generic anchors, negated (GRAMMAR §5.2) ──

    [Fact]
    public void MustNotImplement_ClosedGenericAnchor_RedsOnlyThatConstruction()
    {
        // typeof(IHandler<Order>) reds OrderHandler (that construction) but not TextHandler (IHandler<string>).
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("OrderHandler")
                    .MustNotImplement(typeof(IHandler<Order>)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}OrderHandler"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("TextHandler")
                    .MustNotImplement(typeof(IHandler<Order>)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotImplement_OpenGenericAnchor_RedsEveryConstruction()
    {
        // typeof(IHandler<>) reds every construction — both OrderHandler and TextHandler.
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("OrderHandler")
                    .MustNotImplement(typeof(IHandler<>)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}OrderHandler"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("TextHandler")
                    .MustNotImplement(typeof(IHandler<>)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}TextHandler"]);
    }

    // ── matcher parity over the transitive/substitution/declared-only fixture (GRAMMAR §5.2, negated) ──

    [Fact]
    public void MustNotImplement_TransitiveInterfaceThroughBaseClass_Reds()
    {
        // WidgetChild : Widget : IThing — an interface reached through a base class still reds the ban (the
        // negative reads the full interface closure, exactly like the positive matcher).
        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("WidgetChild")
                    .MustNotImplement(typeof(IThing)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}WidgetChild"]);
    }

    [Fact]
    public void MustNotImplement_TypeArgumentSubstitutionThroughGenericBase_RedsClosedConstruction()
    {
        // SubstHandler : HandlerBase<Order> where HandlerBase<T> : IHandler<T> — the substituted IHandler<Order>
        // reds MustNotImplement(typeof(IHandler<Order>)) (the §5.2 substitution example, negated).
        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubstHandler")
                    .MustNotImplement(typeof(IHandler<Order>)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}SubstHandler"]);
    }

    [Fact]
    public void MustNotBeAttributedWith_DeclaredOnly_BaseAttributeDoesNotRedDerived()
    {
        // Attributes are declared-only (§5.2): [Mark] on AttrBase reds it, but AttrDerived : AttrBase does not
        // inherit the attribute, so the ban silently passes for the derived type.
        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("AttrBase")
                    .MustNotBeAttributedWith(typeof(MarkAttribute)))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}AttrBase"]);

        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("AttrDerived")
                    .MustNotBeAttributedWith(typeof(MarkAttribute)))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    // ── string attribute anchors (GRAMMAR §5.2–§5.3): the escape hatch names the attribute DEFINITION by
    //    fully-qualified string, so a spec need not compile against the attribute's package. Parity band —
    //    each anchor selects and reds exactly what its typeof twin above does ──

    [Fact]
    public void AttributedWith_StringAnchor_SelectsWhatTheTypeofTwinSelects()
    {
        // The same fixture and the same ZZZ-prefix probe as AttributedWith_SelectsAttributedType: naming
        // MarkAttribute by FQN string picks out the identical subject.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.AttributedWith($"{T}MarkAttribute")
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}Tagged"]);
    }

    [Fact]
    public void MustBeAttributedWith_StringAnchor_HoldsForAttributed_FailsForBare()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Tagged")
                    .MustBeAttributedWith($"{T}MarkAttribute"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Plain")
                    .MustBeAttributedWith($"{T}MarkAttribute"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Plain"]);
    }

    [Fact]
    public void MustNotBeAttributedWith_StringAnchor_RedsAttributed_PassesForBare()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Tagged")
                    .MustNotBeAttributedWith($"{T}MarkAttribute"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Tagged"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Plain")
                    .MustNotBeAttributedWith($"{T}MarkAttribute"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void AttributedWith_StringDefinitionAnchor_SelectsEveryConstruction()
    {
        // A string names the DEFINITION, so it reads like an open-generic typeof anchor: `MarkAttribute<T>`
        // reaches both [Mark<int>] and [Mark<string>].
        RuleResult result = Checker.Run(GenericAttributeModel, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.AttributedWith($"{T}MarkAttribute<T>")
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}TaggedInt", $"{T}TaggedText"]);
    }

    [Fact]
    public void AttributedWith_StringConstructedSpelling_SelectsNothing()
    {
        // The stated honesty boundary: a constructed spelling names no definition, so it matches nothing —
        // and the empty subject fails the rule loudly (GRAMMAR §4.1) rather than passing vacuously.
        RuleResult result = Checker.Run(GenericAttributeModel, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.AttributedWith($"{T}MarkAttribute<System.Int32>")
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.EmptySubject);
    }

    [Fact]
    public void MustNotBeAttributedWith_StringConstructedSpelling_NeverReds()
    {
        // Definition string vs constructed spelling on one subject: the first reds TaggedInt, the second —
        // naming a construction rather than a definition — silently passes.
        Checker.Run(GenericAttributeModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("TaggedInt")
                    .MustNotBeAttributedWith($"{T}MarkAttribute<T>"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}TaggedInt"]);

        Checker.Run(GenericAttributeModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("TaggedInt")
                    .MustNotBeAttributedWith($"{T}MarkAttribute<System.Int32>"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotBeAttributedWith_StringAnchorList_RedsOnAnyAnchor()
    {
        // None-of over a homogeneous string list: TaggedPlain carries only [Plain], and the list reds it
        // through the second anchor.
        Checker.Run(GenericAttributeModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("TaggedPlain")
                    .MustNotBeAttributedWith($"{T}MarkAttribute<T>", $"{T}PlainAttribute"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}TaggedPlain"]);

        Checker.Run(GenericAttributeModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Untagged")
                    .MustNotBeAttributedWith($"{T}MarkAttribute<T>", $"{T}PlainAttribute"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    // ── string hierarchy anchors (GRAMMAR §5.2–§5.3): the same escape hatch in interface and base-type
    //    position, so a spec can govern a contract it cannot compile against. Parity band — each string
    //    anchor selects and reds exactly what its typeof twin above does, and the name is the model's own
    //    rendered FullName, which is what a report prints ──

    [Fact]
    public void Implementing_StringAnchor_SelectsEveryConstructionLikeTheOpenGenericTwin()
    {
        // A string names the DEFINITION, so it reads like typeof(IHandler<>) rather than a construction:
        // the same two handlers Implementing_OpenGeneric_SelectsEveryConstruction picks out.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing($"{T}IHandler<T>")
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}OrderHandler", $"{T}TextHandler"]);
    }

    [Fact]
    public void Implementing_StringConstructedSpelling_SelectsNothing()
    {
        // The stated honesty boundary, in interface position: a constructed spelling names no definition, so
        // it matches nothing — and the empty subject fails the rule loudly (GRAMMAR §4.1). This is the one
        // place the string form is deliberately WEAKER than its typeof twin, which can name a construction.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.Implementing($"{T}IHandler<{T}Order>")
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.EmptySubject);
    }

    [Fact]
    public void DerivedFrom_StringAnchor_SelectsWhatTheTypeofTwinSelects()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("h/x")
                    .Enforce(arch.Types.DerivedFrom($"{T}ThingBase")
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShapeSubjects()
            .ShouldBe([$"{T}SubType"]);
    }

    [Fact]
    public void MustImplement_StringAnchor_HoldsForImplementer_FailsForNonImplementer()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Widget")
                    .MustImplement($"{T}IThing"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Gizmo")
                    .MustImplement($"{T}IThing"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Gizmo"]);
    }

    [Fact]
    public void MustNotImplement_StringAnchor_RedsImplementer_PassesForNonImplementer()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Widget")
                    .MustNotImplement($"{T}IThing"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Widget"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Gizmo")
                    .MustNotImplement($"{T}IThing"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustDeriveFrom_StringAnchor_HoldsForDeriver_FailsForNonDeriver()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubType")
                    .MustDeriveFrom($"{T}ThingBase"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("FreeType")
                    .MustDeriveFrom($"{T}ThingBase"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}FreeType"]);
    }

    [Fact]
    public void MustNotDeriveFrom_StringAnchor_RedsDeriver_PassesForNonDeriver()
    {
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubType")
                    .MustNotDeriveFrom($"{T}ThingBase"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}SubType"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("FreeType")
                    .MustNotDeriveFrom($"{T}ThingBase"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotImplement_StringAnchorList_RedsOnAnyAnchor()
    {
        // None-of over a homogeneous string list: Widget implements only IThing, and the list reds it
        // through the first anchor; OrderHandler through the second.
        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Widget")
                    .MustNotImplement($"{T}IThing", $"{T}IHandler<T>"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}Widget"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("OrderHandler")
                    .MustNotImplement($"{T}IThing", $"{T}IHandler<T>"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}OrderHandler"]);

        Checker.Run(Model, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("Gizmo")
                    .MustNotImplement($"{T}IThing", $"{T}IHandler<T>"))
                .Because("b"))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotImplement_StringAnchor_ReadsTheWholeInterfaceClosure()
    {
        // The string arm reads the same closure the typeof arm does, so the two hardest positive cases hold
        // for it too: an interface reached through a base class (WidgetChild : Widget : IThing), and a
        // type-argument substitution (SubstHandler : HandlerBase<Order> where HandlerBase<T> : IHandler<T>),
        // which the definition-level anchor reaches because every construction matches its definition.
        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("WidgetChild")
                    .MustNotImplement($"{T}IThing"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}WidgetChild"]);

        Checker.Run(TransitiveModel, arch => arch.Rule("h/x")
                .Enforce(arch.Types.WithPrefix("SubstHandler")
                    .MustNotImplement($"{T}IHandler<T>"))
                .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe([$"{T}SubstHandler"]);
    }
}
