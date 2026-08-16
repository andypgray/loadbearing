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
///     construction exactly. Runs over the shared <see cref="Sources.HierarchyModel" />.
/// </summary>
public sealed class HierarchyVerbTests
{
    private const string T = "Zphil.LoadBearing.Tests.Checking.Targets.";

    private static readonly CodebaseModel Model = Sources.HierarchyModel;
    private static readonly CodebaseModel TransitiveModel = CompilationFactory.Extract(Sources.HierarchyTransitive);
    private static readonly CodebaseModel GenericAttributeModel = CompilationFactory.Extract(Sources.GenericAttributes);
    private static readonly CodebaseModel ExternalBaseModel = CompilationFactory.Extract(Sources.ExternalBaseHierarchy);

    [Fact]
    public void Implementing_OpenGeneric_SelectsEveryConstruction()
    {
        Checker.Selects(Model, arch => arch.Types.Implementing(typeof(IHandler<>)))
            .ShouldBe([$"{T}OrderHandler", $"{T}TextHandler"]);
    }

    [Fact]
    public void Implementing_ClosedGeneric_SelectsOnlyThatConstruction()
    {
        Checker.Selects(Model, arch => arch.Types.Implementing(typeof(IHandler<Order>)))
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
        Checker.Selects(Model, arch => arch.Types.DerivedFrom(typeof(ThingBase)))
            .ShouldBe([$"{T}SubType"]);
    }

    [Fact]
    public void AttributedWith_SelectsAttributedType()
    {
        Checker.Selects(Model, arch => arch.Types.AttributedWith(typeof(MarkAttribute)))
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
        // The same fixture and the same probe as AttributedWith_SelectsAttributedType: naming
        // MarkAttribute by FQN string picks out the identical subject.
        Checker.Selects(Model, arch => arch.Types.AttributedWith($"{T}MarkAttribute"))
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
        Checker.Selects(GenericAttributeModel, arch => arch.Types.AttributedWith($"{T}MarkAttribute<T>"))
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
        Checker.Selects(Model, arch => arch.Types.Implementing($"{T}IHandler<T>"))
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
        Checker.Selects(Model, arch => arch.Types.DerivedFrom($"{T}ThingBase"))
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

    // ── external anchors (GRAMMAR §5.2): the shallow hierarchy external types carry is a fact about the
    //    SUBJECT position, not the anchor position. A declared subject's base chain is walked straight
    //    through metadata, so a base the spec's own universe does not declare is matchable — which is what
    //    makes the string hatch a real answer for a spec that cannot compile against the anchor at all ──

    [Fact]
    public void DerivedFrom_ExternalAnchor_StringArmSelectsExactlyWhatTheTypeofArmSelects()
    {
        // Both arms in ONE model, asserted against EACH OTHER rather than against their own literals:
        // a string anchor always matches on TypeConstruction.Definition.FullName while a non-generic typeof
        // anchor matches on TypeConstruction.FullName (SelectionEvaluator.AnchorKey). Different fields that
        // coincide for a non-generic type — so only comparing the two results catches them diverging.
        CheckReport report = Checker.Run(ExternalBaseModel, arch =>
        {
            arch.Rule("h/typed")
                .Enforce(arch.Types.DerivedFrom(typeof(Exception))
                    .MustHavePrefix("ZZZ"))
                .Because("b");
            arch.Rule("h/string")
                .Enforce(arch.Types.DerivedFrom("System.Exception")
                    .MustHavePrefix("ZZZ"))
                .Because("b");
        });

        var typed = report.ForRule("h/typed")
            .ShapeSubjects();
        var stringed = report.ForRule("h/string")
            .ShapeSubjects();

        stringed.ShouldBe(typed);

        // Non-vacuous, and the selection is not merely the direct deriver: IndirectDeriver reaches the anchor
        // through System.ArgumentException, so both arms walked a chain of external constructions to get there.
        typed.ShouldBe([$"{T}DirectDeriver", $"{T}IndirectDeriver"]);
    }
}
