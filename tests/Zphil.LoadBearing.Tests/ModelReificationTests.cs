using Shouldly;
using Xunit;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Stubs;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The reified read model (acceptance): rule order over the post-desugar
///     set, per-rule posture, Migrate reification and defaults (GRAMMAR §4.4), and Quarantine
///     desugaring into containment + tripwire with boundary, baseline, dragons, and auto-Fix
///     (GRAMMAR §7).
/// </summary>
public class ModelReificationTests
{
    private const string DragonsProse =
        "Banker's rounding happens at line-item level, NOT invoice level. " +
        "Nightly reconciliation depends on this. Do not normalize.";

    // A quarantined scope documented via .DragonsDoc(...) rather than inline .Dragons(...).
    private static readonly IArchitectureSpec DragonsDocScopeSpec = new InlineSpec(arch =>
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade))
            .DragonsDoc("arch/billing-dragons.md")
            .Because("Replacement scheduled; see the linked doc."));

    // A single MustNotConstruct-rule spec, reused for the dependency-verb reification + empty-member-hook pins.
    private static readonly IArchitectureSpec CtorRuleSpec = new InlineSpec(arch =>
        arch.Rule("di/no-new-services")
            .Enforce(arch.Types.MustNotConstruct(typeof(SqlConnection)))
            .Because("Services are DI-resolved; direct construction bypasses the container."));

    private static ArchitectureModel BuildCanonical()
    {
        return ArchModelBuilder.Build(new ArchSpec());
    }

    private static ArchRule Rule(string id)
    {
        return BuildCanonical().Rules.Single(rule => rule.Id == id);
    }

    [Fact]
    public void Build_CanonicalSample_ReifiesEightRulesInPinnedOrder()
    {
        BuildCanonical().Rules.Select(rule => rule.Id).ShouldBe(
        [
            "layering/domain-independent",
            "naming/interfaces",
            "data-access/no-inline-sql",
            "legacy/billing/containment",
            "legacy/billing/tripwire",
            "naming/handlers",
            "di/handlers-via-registry",
            "style/type-name-length"
        ]);
    }

    [Theory]
    [InlineData("layering/domain-independent", Posture.Enforce)]
    [InlineData("naming/interfaces", Posture.Enforce)]
    [InlineData("data-access/no-inline-sql", Posture.Migrate)]
    [InlineData("legacy/billing/containment", Posture.Quarantine)]
    [InlineData("legacy/billing/tripwire", Posture.Quarantine)]
    [InlineData("naming/handlers", Posture.Enforce)]
    [InlineData("di/handlers-via-registry", Posture.Enforce)]
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
        rule.Quarantine.ShouldBeNull();
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
        // A Migrate rule with neither .Baseline(...) nor .WhileYoureThere(...) — exercises the defaults.
        ArchRule rule = Checker.Model(arch =>
            {
                Layer web = arch.Layer("Web", "MyApp.Web.*");
                arch.Rule("data-access/no-inline-sql")
                    .Migrate(
                        "Controllers open SqlConnection directly.",
                        web.WithSuffix("Controller").MustNotReference(typeof(SqlConnection)))
                    .Because("Repository pattern for testability.");
            })
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Migrate);
        rule.Migrate.ShouldNotBeNull();
        // .Baseline omitted ⇒ conventional default derived from the rule ID (GRAMMAR §4.4).
        rule.Migrate!.BaselinePath.ShouldBe("arch/baselines/data-access/no-inline-sql.json");
        rule.Migrate.Policy.ShouldBe(MigrationPolicy.MigrateIfSmall);
    }

    [Fact]
    public void QuarantineScope_DesugarsIntoContainmentCarryingBoundaryBaselineDragonsAndAutoFix()
    {
        ArchRule containment = Rule("legacy/billing/containment");

        containment.Quarantine.ShouldNotBeNull();
        containment.Quarantine!.Role.ShouldBe(QuarantineRole.Containment);
        containment.Quarantine.Boundary.ShouldBe([typeof(IBillingFacade), typeof(BillingFacade)]);
        containment.Quarantine.BaselinePath.ShouldBe("arch/baseline.json");
        containment.Quarantine.Dragons.ShouldBe(DragonsProse);
        containment.Quarantine.ScopeId.ShouldBe("legacy/billing");
        containment.Constraint.ShouldNotBeNull();
        // Auto-derived fix from the first BoundaryOnlyVia type (GRAMMAR §5.5).
        containment.Fix.ShouldBe("use `IBillingFacade`");
        // The raw quarantined selection rides on the containment child so the renderer can place it.
        containment.Quarantine.Quarantined.ShouldNotBeNull();
    }

    [Fact]
    public void QuarantineScope_DesugarsIntoTripwireCarryingDragonsButNoConstraint()
    {
        ArchRule tripwire = Rule("legacy/billing/tripwire");

        tripwire.Quarantine.ShouldNotBeNull();
        tripwire.Quarantine!.Role.ShouldBe(QuarantineRole.Tripwire);
        tripwire.Quarantine.Dragons.ShouldBe(DragonsProse);
        tripwire.Quarantine.ScopeId.ShouldBe("legacy/billing");
        // Boundary and baseline are containment concerns; the tripwire has no closed-vocabulary law yet.
        tripwire.Quarantine.Boundary.ShouldBeEmpty();
        tripwire.Quarantine.BaselinePath.ShouldBeNull();
        // The quarantined selection rides on the tripwire too so its diff-touch can map changed files.
        tripwire.Quarantine.Quarantined.ShouldNotBeNull();
        tripwire.Constraint.ShouldBeNull();
        tripwire.Sentence.ShouldBe(string.Empty);
    }

    [Fact]
    public void QuarantineScope_SharesBecauseAcrossBothChildren()
    {
        const string because = "Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.";
        Rule("legacy/billing/containment").Because.ShouldBe(because);
        Rule("legacy/billing/tripwire").Because.ShouldBe(because);
    }

    [Fact]
    public void QuarantineScope_WithDragonsDoc_ReifiesLinkedDocPathOnBothChildren()
    {
        ArchitectureModel model = ArchModelBuilder.Build(DragonsDocScopeSpec);

        QuarantineData containment = model.Rules.Single(rule => rule.Id == "legacy/billing/containment").Quarantine!;
        containment.DragonsDoc.ShouldBe("arch/billing-dragons.md");
        containment.Dragons.ShouldBeNull();
        model.Rules.Single(rule => rule.Id == "legacy/billing/tripwire").Quarantine!.DragonsDoc
            .ShouldBe("arch/billing-dragons.md");
    }

    [Fact]
    public void QuarantineScope_WithoutBaseline_DefaultsContainmentToConventionalPath()
    {
        // DragonsDocScopeSpec omits .Baseline, so the containment child falls back to the default.
        ArchitectureModel model = ArchModelBuilder.Build(DragonsDocScopeSpec);

        QuarantineData containment = model.Rules.Single(rule => rule.Id == "legacy/billing/containment").Quarantine!;
        // .Baseline omitted ⇒ conventional default derived from the containment rule ID (GRAMMAR §4.4/§7).
        containment.BaselinePath.ShouldBe("arch/baselines/legacy/billing/containment.json");
        // The tripwire's baseline stays null — grandfathering is a containment concern.
        model.Rules.Single(rule => rule.Id == "legacy/billing/tripwire").Quarantine!.BaselinePath.ShouldBeNull();
    }

    [Fact]
    public void MustNotUseRule_ReifiesToWalkableMemberConstraint()
    {
        // The flagship member-ban rule, for the walkable-model pins (Migrate posture, real members).
        ArchRule rule = Checker.Model(arch => arch.Rule("time/inject-clock")
                .Migrate(
                    "Code reads the ambient clock directly.",
                    arch.Types.MustNotUse(
                        arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                        arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
                .Because("Wall-clock reads are untestable; inject IClock — ADR-nnn."))
            .Rules.Single(r => r.Id == "time/inject-clock");

        rule.Posture.ShouldBe(Posture.Migrate);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotUseConstraint>();

        // Members in authoring order, each with its authored declaring type + name (GRAMMAR §4.5).
        constraint.Members.Count.ShouldBe(2);
        constraint.Members[0].DeclaringType.ShouldBe(typeof(DateTime));
        constraint.Members[0].Name.ShouldBe("Now");
        constraint.Members[1].DeclaringType.ShouldBe(typeof(DateTime));
        constraint.Members[1].Name.ShouldBe("UtcNow");
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotConstructRule_ReifiesToWalkableConstructConstraint()
    {
        ArchRule rule = ArchModelBuilder.Build(CtorRuleSpec).Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotConstructConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.Count.ShouldBe(1);
        constraint.Operands.ShouldBe(constraint.Targets);
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void DependencyVerbConstraint_HasEmptyMemberOperands()
    {
        // The member-operands walk hook is empty for the dependency verbs (GRAMMAR §4.5, §8 items 11–13).
        Rule("layering/domain-independent").Constraint!.MemberOperands.ShouldBeEmpty();
        // MustNotConstruct is a dependency-shape verb (overrides Operands, not MemberOperands) — its member hook is empty too.
        ArchModelBuilder.Build(CtorRuleSpec).Rules.Single().Constraint!.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void MemberSubjectRule_ReifiesToWalkableMemberConstraint()
    {
        // The flagship member-subject rule, for the walkable-model pins (GRAMMAR §4.6).
        ArchRule rule = Checker.Model(arch =>
            {
                Selection web = arch.Namespace("MyApp.Web.*");
                arch.Rule("naming/async-suffix")
                    .Enforce(web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async"))
                    .Because("Async methods are discovered by suffix.");
            })
            .Rules.Single(r => r.Id == "naming/async-suffix");

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MemberMustHaveSuffixConstraint>();

        // The member subject: the Methods projection carrying one Returning adjective (GRAMMAR §4.6).
        constraint.MemberSubject.Kind.ShouldBe(MemberKindFilter.Method);
        constraint.MemberSubject.Adjectives.OfType<ReturningAdjective>().ShouldHaveSingleItem();

        // The inherited Subject is the underlying TYPE selection (Subject => MemberSubject.Source), so
        // foreign walks and Quarantine desugaring keep working on the type side.
        constraint.Subject.ShouldBeSameAs(constraint.MemberSubject.Source);
        constraint.Subject.Noun.ShouldBeOfType<NamespaceNoun>();

        // Member subjects never populate MemberOperands (that hook is the MustNotUse target list).
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void RegisteredNoun_WithLifetime_ReifiesToInjectConstraintCarryingLifetimes()
    {
        // arch.Registered(Lifetime.X) reifies to a RegisteredNoun carrying that lifetime; MustNotInject
        // reifies to a MustNotInjectConstraint whose Operands mirror its Targets (GRAMMAR §4.7).
        // The captive-dependency flagship: singleton-registered types must not inject scoped/transient ones.
        var constraint = Checker.Model(arch => arch.Rule("di/no-captive-dependencies")
                .Enforce(arch.Registered(Lifetime.Singleton)
                    .MustNotInject(arch.Registered(Lifetime.Scoped), arch.Registered(Lifetime.Transient)))
                .Because("Singletons capturing scoped/transient services leak state across scopes."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotInjectConstraint>();

        constraint.Subject.Noun.ShouldBeOfType<RegisteredNoun>().Lifetime.ShouldBe(Lifetime.Singleton);
        constraint.Operands.ShouldBe(constraint.Targets);
        constraint.Targets.Select(target => ((RegisteredNoun)target.Noun).Lifetime)
            .ShouldBe([Lifetime.Scoped, Lifetime.Transient]);
        // MustNotInject is a dependency-shape verb (overrides Operands, not MemberOperands).
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void RegisteredNoun_NoArg_ReifiesWithNullLifetime()
    {
        // arch.Registered() reifies to a RegisteredNoun with a null lifetime (any lifetime).
        var constraint = Checker.Model(arch => arch.Rule("di/registered-inject")
                .Enforce(arch.Registered().MustNotInject(arch.Registered(Lifetime.Scoped)))
                .Because("Any registration must not inject a scoped service."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotInjectConstraint>();
        constraint.Subject.Noun.ShouldBeOfType<RegisteredNoun>().Lifetime.ShouldBeNull();
    }

    [Fact]
    public void MustNotInject_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        // The Type-sugar overload wraps each bare type as a single-type selection — the model is identical to
        // writing arch.Type(...) by hand (GRAMMAR §3.3): one bare TypeNoun operand for SqlConnection either way.
        var sugar = Checker.Model(arch => arch.Rule("di/no-inject-sql")
                .Enforce(arch.Types.MustNotInject(typeof(SqlConnection)))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotInjectConstraint>();
        var wrapped = Checker.Model(arch => arch.Rule("di/no-inject-sql")
                .Enforce(arch.Types.MustNotInject(arch.Type(typeof(SqlConnection))))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotInjectConstraint>();

        sugar.Targets.Count.ShouldBe(1);
        Type sugarType = sugar.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        Type wrappedType = wrapped.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        sugarType.ShouldBe(typeof(SqlConnection));
        wrappedType.ShouldBe(sugarType);
        sugar.Targets[0].Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatchRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotCatch-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/no-catch-ioe")
                .Enforce(arch.Types.MustNotCatch(typeof(InvalidOperationException)))
                .Because("Swallowing invalid-operation signals hides real defects."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotCatchConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.Count.ShouldBe(1);
        constraint.Operands.ShouldBe(constraint.Targets);
        // MustNotCatch is a dependency-shape verb (overrides Operands, not MemberOperands) — its member hook is empty.
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatchUnfilteredRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotCatchUnfiltered-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/filter-broad-catches")
                .Enforce(arch.Types.MustNotCatchUnfiltered(typeof(Exception)))
                .Because("A broad catch names what it expects in a `when` filter."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotCatchUnfilteredConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.Count.ShouldBe(1);
        constraint.Operands.ShouldBe(constraint.Targets);
        // The filter condition lives in the verb, so the node's shape is the plain catch verb's — no extra operand
        // carries it, and the member hook stays empty.
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotSwallowRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotSwallow-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/no-swallowed-broad-catches")
                .Enforce(arch.Types.MustNotSwallow(typeof(Exception)))
                .Because("A handler that holds a failure and continues hides it."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotSwallowConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.Count.ShouldBe(1);
        constraint.Operands.ShouldBe(constraint.Targets);
        // Both the filter condition and the rethrow condition live in the verb, so the node's shape is the plain
        // catch verb's — no extra operand carries either, and the member hook stays empty.
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotThrowRule_ReifiesToWalkableThrowConstraint()
    {
        // A single MustNotThrow-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/no-bare-bcl-throws")
                .Enforce(arch.Types.MustNotThrow(typeof(Exception)))
                .Because("Bare BCL exception types carry no meaning a caller can dispatch on."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotThrowConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.Count.ShouldBe(1);
        constraint.Operands.ShouldBe(constraint.Targets);
        // MustNotThrow is a dependency-shape verb (overrides Operands, not MemberOperands) — its member hook is empty.
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustOnlyThrowRule_ReifiesToWalkableThrowConstraint()
    {
        // A single MustOnlyThrow-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/throw-domain-only")
                .Enforce(arch.Types.MustOnlyThrow(typeof(InvalidOperationException)))
                .Because("Domain code must surface only sanctioned exception types."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustOnlyThrowConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.Count.ShouldBe(1);
        constraint.Operands.ShouldBe(constraint.Targets);
        // MustOnlyThrow is a dependency-shape verb (overrides Operands, not MemberOperands) — its member hook is empty.
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatch_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        // The Type-sugar overload wraps each bare type as a single-type selection — identical to writing
        // arch.Type(...) by hand (GRAMMAR §3.3): one bare TypeNoun operand for the exception type either way.
        var sugar = Checker.Model(arch => arch.Rule("errors/no-catch")
                .Enforce(arch.Types.MustNotCatch(typeof(InvalidOperationException)))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotCatchConstraint>();
        var wrapped = Checker.Model(arch => arch.Rule("errors/no-catch")
                .Enforce(arch.Types.MustNotCatch(arch.Type(typeof(InvalidOperationException))))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotCatchConstraint>();

        sugar.Targets.Count.ShouldBe(1);
        Type sugarType = sugar.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        Type wrappedType = wrapped.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        sugarType.ShouldBe(typeof(InvalidOperationException));
        wrappedType.ShouldBe(sugarType);
        sugar.Targets[0].Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatchUnfiltered_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        // The Type-sugar overload wraps each bare type as a single-type selection — identical to writing
        // arch.Type(...) by hand (GRAMMAR §3.3): one bare TypeNoun operand for the exception type either way.
        var sugar = Checker.Model(arch => arch.Rule("errors/no-unfiltered-catch")
                .Enforce(arch.Types.MustNotCatchUnfiltered(typeof(Exception)))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotCatchUnfilteredConstraint>();
        var wrapped = Checker.Model(arch => arch.Rule("errors/no-unfiltered-catch")
                .Enforce(arch.Types.MustNotCatchUnfiltered(arch.Type(typeof(Exception))))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotCatchUnfilteredConstraint>();

        sugar.Targets.Count.ShouldBe(1);
        Type sugarType = sugar.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        Type wrappedType = wrapped.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        sugarType.ShouldBe(typeof(Exception));
        wrappedType.ShouldBe(sugarType);
        sugar.Targets[0].Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotSwallow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        // The Type-sugar overload wraps each bare type as a single-type selection — identical to writing
        // arch.Type(...) by hand (GRAMMAR §3.3): one bare TypeNoun operand for the exception type either way.
        var sugar = Checker.Model(arch => arch.Rule("errors/no-swallow")
                .Enforce(arch.Types.MustNotSwallow(typeof(Exception)))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotSwallowConstraint>();
        var wrapped = Checker.Model(arch => arch.Rule("errors/no-swallow")
                .Enforce(arch.Types.MustNotSwallow(arch.Type(typeof(Exception))))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotSwallowConstraint>();

        sugar.Targets.Count.ShouldBe(1);
        Type sugarType = sugar.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        Type wrappedType = wrapped.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        sugarType.ShouldBe(typeof(Exception));
        wrappedType.ShouldBe(sugarType);
        sugar.Targets[0].Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotThrow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        // The Type-sugar overload wraps each bare type as a single-type selection — identical to writing
        // arch.Type(...) by hand (GRAMMAR §3.3): one bare TypeNoun operand for the exception type either way.
        var sugar = Checker.Model(arch => arch.Rule("errors/no-throw")
                .Enforce(arch.Types.MustNotThrow(typeof(Exception)))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotThrowConstraint>();
        var wrapped = Checker.Model(arch => arch.Rule("errors/no-throw")
                .Enforce(arch.Types.MustNotThrow(arch.Type(typeof(Exception))))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotThrowConstraint>();

        sugar.Targets.Count.ShouldBe(1);
        Type sugarType = sugar.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        Type wrappedType = wrapped.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        sugarType.ShouldBe(typeof(Exception));
        wrappedType.ShouldBe(sugarType);
        sugar.Targets[0].Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustOnlyThrow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        // The Type-sugar overload wraps each bare type as a single-type selection — identical to writing
        // arch.Type(...) by hand (GRAMMAR §3.3): one bare TypeNoun operand for the exception type either way.
        var sugar = Checker.Model(arch => arch.Rule("errors/throw-only")
                .Enforce(arch.Types.MustOnlyThrow(typeof(InvalidOperationException)))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustOnlyThrowConstraint>();
        var wrapped = Checker.Model(arch => arch.Rule("errors/throw-only")
                .Enforce(arch.Types.MustOnlyThrow(arch.Type(typeof(InvalidOperationException))))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustOnlyThrowConstraint>();

        sugar.Targets.Count.ShouldBe(1);
        Type sugarType = sugar.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        Type wrappedType = wrapped.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        sugarType.ShouldBe(typeof(InvalidOperationException));
        wrappedType.ShouldBe(sugarType);
        sugar.Targets[0].Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotExposeRule_ReifiesToWalkableExposeConstraint()
    {
        // A single MustNotExpose-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("api/no-leaky-surface")
                .Enforce(arch.Types.MustNotExpose(typeof(SqlConnection)))
                .Because("Public signatures must not leak infrastructure types."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotExposeConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.Count.ShouldBe(1);
        constraint.Operands.ShouldBe(constraint.Targets);
        // MustNotExpose is a dependency-shape verb (overrides Operands, not MemberOperands) — its member hook is empty.
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotExpose_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        // The Type-sugar overload wraps each bare type as a single-type selection — identical to writing
        // arch.Type(...) by hand (GRAMMAR §3.3): one bare TypeNoun operand for the exposed type either way.
        var sugar = Checker.Model(arch => arch.Rule("api/no-expose")
                .Enforce(arch.Types.MustNotExpose(typeof(SqlConnection)))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotExposeConstraint>();
        var wrapped = Checker.Model(arch => arch.Rule("api/no-expose")
                .Enforce(arch.Types.MustNotExpose(arch.Type(typeof(SqlConnection))))
                .Because("Reason."))
            .Rules.Single().Constraint
            .ShouldBeOfType<MustNotExposeConstraint>();

        sugar.Targets.Count.ShouldBe(1);
        Type sugarType = sugar.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        Type wrappedType = wrapped.Targets[0].Noun.ShouldBeOfType<TypeNoun>().Type;
        sugarType.ShouldBe(typeof(SqlConnection));
        wrappedType.ShouldBe(sugarType);
        sugar.Targets[0].Adjectives.ShouldBeEmpty();
    }

    // ---- Surface union: arch.AnyOf reification (GRAMMAR §5.1) ----

    [Fact]
    public void AnyOf_NestedUnion_FlattensAtMint()
    {
        // Prose and evaluation both read one leaf list: AnyOf(AnyOf(a, b), c) ≡ AnyOf(a, b, c), operand
        // order preserved.
        var arch = new Arch();
        var union = arch.AnyOf(arch.AnyOf(arch.Project("A"), arch.Project("B")), arch.Project("C"))
            .ShouldBeOfType<UnionSelection>();

        union.Parts.Select(part => part.Noun.ShouldBeOfType<ProjectNoun>().Name).ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void AnyOf_NestedUnionCarryingAdjectives_StaysALeaf()
    {
        // An inner union with adjectives is a narrowed set of its own — flattening it would lose the
        // narrowing, so it survives as one operand.
        var arch = new Arch();
        Selection inner = arch.AnyOf(arch.Project("A"), arch.Project("B")).Except(arch.Type(typeof(SqlConnection)));
        var outer = arch.AnyOf(inner, arch.Project("C")).ShouldBeOfType<UnionSelection>();

        outer.Parts.Count.ShouldBe(2);
        outer.Parts[0].ShouldBeOfType<UnionSelection>().Parts.Count.ShouldBe(2);
    }

    [Fact]
    public void AnyOf_Adjective_IsOwnedByTheUnionNotDistributedThroughIt()
    {
        // (a ∪ b) − c, not (a − c) ∪ (b − c): the union keeps its two operands and grows its own adjective.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"), arch.Project("B"))
            .Except(arch.Type(typeof(SqlConnection)))
            .ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(2);
        union.Adjectives.Count.ShouldBe(1);
        union.Adjectives[0].ShouldBeOfType<ExceptAdjective>();
    }

    [Fact]
    public void Authored_AppendsOneAuthoredAdjective()
    {
        // The adjective carries no payload — the whole statement is the placement and the fragment — so
        // the model pin is that one lands, on the union as well as on a plain selection.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"), arch.Project("B")).Authored().ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(2);
        union.Adjectives.Count.ShouldBe(1);
        union.Adjectives[0].ShouldBeOfType<AuthoredAdjective>();
    }

    [Fact]
    public void AnyOf_SingleOperand_StaysAUnionInTheModel()
    {
        // Legal and an identity (§2 principle 5): a loop that yields one operand must not become an error,
        // and the node stays a union so the shape does not depend on how many times the loop ran.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A")).ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(1);
        union.Adjectives.ShouldBeEmpty();
    }
}
