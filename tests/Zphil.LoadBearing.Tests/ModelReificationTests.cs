using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.Stubs;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The reified read model (plan Deliverable 1 / acceptance): rule order over the post-desugar
///     set, per-rule posture, Migrate reification and defaults (GRAMMAR §4.4), and Freeze
///     desugaring into containment + tripwire with boundary, baseline, dragons, and auto-Fix
///     (GRAMMAR §7).
/// </summary>
public class ModelReificationTests
{
    private const string DragonsProse =
        "Banker's rounding happens at line-item level, NOT invoice level. " +
        "Nightly reconciliation depends on this. Do not normalize.";

    private static ArchitectureModel BuildCanonical()
    {
        return ArchModelBuilder.Build(new ArchSpec());
    }

    private static ArchRule Rule(string id)
    {
        return BuildCanonical().Rules.Single(rule => rule.Id == id);
    }

    [Fact]
    public void Build_CanonicalSample_ReifiesSevenRulesInPinnedOrder()
    {
        BuildCanonical().Rules.Select(rule => rule.Id).ShouldBe(
        [
            "layering/domain-independent",
            "naming/interfaces",
            "data-access/no-inline-sql",
            "legacy/billing/containment",
            "legacy/billing/tripwire",
            "naming/handlers",
            "style/type-name-length"
        ]);
    }

    [Theory]
    [InlineData("layering/domain-independent", Posture.Enforce)]
    [InlineData("naming/interfaces", Posture.Enforce)]
    [InlineData("data-access/no-inline-sql", Posture.Migrate)]
    [InlineData("legacy/billing/containment", Posture.Freeze)]
    [InlineData("legacy/billing/tripwire", Posture.Freeze)]
    [InlineData("naming/handlers", Posture.Enforce)]
    [InlineData("style/type-name-length", Posture.Enforce)]
    public void Build_CanonicalSample_AssignsPostures(string id, Posture posture)
    {
        Rule(id).Posture.ShouldBe(posture);
    }

    [Fact]
    public void EnforceRule_CarriesBecauseFixAndNoPostureData()
    {
        ArchRule rule = Rule("layering/domain-independent");

        rule.Because.ShouldBe("Domain is UI-agnostic; transaction boundaries live in services.");
        rule.Fix.ShouldBe("Define an abstraction in Domain and implement it in Web.");
        rule.Constraint.ShouldNotBeNull();
        rule.Migrate.ShouldBeNull();
        rule.Freeze.ShouldBeNull();
    }

    [Fact]
    public void EnforceRule_WithoutFix_LeavesFixNull()
    {
        Rule("naming/interfaces").Fix.ShouldBeNull();
    }

    [Fact]
    public void MigrateRule_ReifiesFromToBaselineAndPolicy()
    {
        ArchRule rule = Rule("data-access/no-inline-sql");

        rule.Migrate.ShouldNotBeNull();
        rule.Migrate!.From.ShouldBe("Controllers open SqlConnection directly (legacy Active Record style).");
        rule.Migrate.ToSentence.ShouldBe("Types in the Web layer named `*Controller` must not reference `SqlConnection`.");
        rule.Migrate.ToSentence.ShouldBe(rule.Sentence);
        rule.Migrate.BaselinePath.ShouldBe("arch/baseline.json");
        rule.Migrate.Policy.ShouldBe(MigrationPolicy.MigrateIfSmall);
    }

    [Fact]
    public void MigrateRule_WithoutOptions_DefaultsBaselineToConventionalPathAndPolicyMigrateIfSmall()
    {
        ArchRule rule = ArchModelBuilder.Build(new MigrateWithoutOptionsSpec()).Rules.Single();

        rule.Posture.ShouldBe(Posture.Migrate);
        rule.Migrate.ShouldNotBeNull();
        // .Baseline omitted ⇒ conventional default derived from the rule ID (GRAMMAR §4.4).
        rule.Migrate!.BaselinePath.ShouldBe("arch/baselines/data-access/no-inline-sql.json");
        rule.Migrate.Policy.ShouldBe(MigrationPolicy.MigrateIfSmall);
    }

    [Fact]
    public void FreezeScope_DesugarsIntoContainmentCarryingBoundaryBaselineDragonsAndAutoFix()
    {
        ArchRule containment = Rule("legacy/billing/containment");

        containment.Freeze.ShouldNotBeNull();
        containment.Freeze!.Role.ShouldBe(FreezeRole.Containment);
        containment.Freeze.Boundary.ShouldBe([typeof(IBillingFacade), typeof(BillingFacade)]);
        containment.Freeze.BaselinePath.ShouldBe("arch/baseline.json");
        containment.Freeze.Dragons.ShouldBe(DragonsProse);
        containment.Freeze.ScopeId.ShouldBe("legacy/billing");
        containment.Constraint.ShouldNotBeNull();
        // Auto-derived fix from the first BoundaryOnlyVia type (GRAMMAR §5.5).
        containment.Fix.ShouldBe("use `IBillingFacade`");
        // The raw frozen selection rides on the containment child so the renderer can place it (R3).
        containment.Freeze.Frozen.ShouldNotBeNull();
    }

    [Fact]
    public void FreezeScope_DesugarsIntoTripwireCarryingDragonsButNoConstraint()
    {
        ArchRule tripwire = Rule("legacy/billing/tripwire");

        tripwire.Freeze.ShouldNotBeNull();
        tripwire.Freeze!.Role.ShouldBe(FreezeRole.Tripwire);
        tripwire.Freeze.Dragons.ShouldBe(DragonsProse);
        tripwire.Freeze.ScopeId.ShouldBe("legacy/billing");
        // Boundary and baseline are containment concerns; the tripwire has no closed-vocabulary law yet.
        tripwire.Freeze.Boundary.ShouldBeEmpty();
        tripwire.Freeze.BaselinePath.ShouldBeNull();
        // The frozen selection rides on the tripwire too so its diff-touch can map changed files.
        tripwire.Freeze.Frozen.ShouldNotBeNull();
        tripwire.Constraint.ShouldBeNull();
        tripwire.Sentence.ShouldBe(string.Empty);
    }

    [Fact]
    public void FreezeScope_SharesBecauseAcrossBothChildren()
    {
        const string because = "Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.";
        Rule("legacy/billing/containment").Because.ShouldBe(because);
        Rule("legacy/billing/tripwire").Because.ShouldBe(because);
    }

    [Fact]
    public void FreezeScope_WithDragonsDoc_ReifiesLinkedDocPathOnBothChildren()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new DragonsDocScopeSpec());

        FreezeData containment = model.Rules.Single(rule => rule.Id == "legacy/billing/containment").Freeze!;
        containment.DragonsDoc.ShouldBe("arch/billing-dragons.md");
        containment.Dragons.ShouldBeNull();
        model.Rules.Single(rule => rule.Id == "legacy/billing/tripwire").Freeze!.DragonsDoc
            .ShouldBe("arch/billing-dragons.md");
    }

    [Fact]
    public void FreezeScope_WithoutBaseline_DefaultsContainmentToConventionalPath()
    {
        // DragonsDocScopeSpec omits .Baseline, so the containment child falls back to the default.
        ArchitectureModel model = ArchModelBuilder.Build(new DragonsDocScopeSpec());

        FreezeData containment = model.Rules.Single(rule => rule.Id == "legacy/billing/containment").Freeze!;
        // .Baseline omitted ⇒ conventional default derived from the containment rule ID (GRAMMAR §4.4/§7).
        containment.BaselinePath.ShouldBe("arch/baselines/legacy/billing/containment.json");
        // The tripwire's baseline stays null — grandfathering is a containment concern.
        model.Rules.Single(rule => rule.Id == "legacy/billing/tripwire").Freeze!.BaselinePath.ShouldBeNull();
    }

    // A frozen scope documented via .DragonsDoc(...) rather than inline .Dragons(...).
    private sealed class DragonsDocScopeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing")
                .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .BoundaryOnlyVia(typeof(IBillingFacade))
                .DragonsDoc("arch/billing-dragons.md")
                .Because("Replacement scheduled; see the linked doc.");
        }
    }

    // A Migrate rule with neither .Baseline(...) nor .WhileYoureThere(...) — exercises the defaults.
    private sealed class MigrateWithoutOptionsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer web = arch.Layer("Web", "MyApp.Web.*");
            arch.Rule("data-access/no-inline-sql")
                .Migrate(
                    "Controllers open SqlConnection directly.",
                    web.WithSuffix("Controller").MustNotReference(typeof(SqlConnection)))
                .Because("Repository pattern for testability.");
        }
    }
}