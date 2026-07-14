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
}