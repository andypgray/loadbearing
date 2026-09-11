using Shouldly;
using Xunit;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Hosting;
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
        return BuildCanonical()
            .Rule(id);
    }

    [Fact]
    public void Build_CanonicalSample_ReifiesEightRulesInPinnedOrder()
    {
        BuildCanonical()
            .Rules.Select(rule => rule.Id)
            .ShouldBe(
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
        Rule(id)
            .Posture.ShouldBe(posture);
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
        Rule("naming/interfaces")
            .Fix.ShouldBeNull();
    }

    [Fact]
    public void MigrateRule_ReifiesFromToBaselineAndPolicy()
    {
        ArchRule rule = Rule("data-access/no-inline-sql");

        MigrateData migrate = rule.Migrate.ShouldNotBeNull();
        migrate.From.ShouldBe("Controllers open SqlConnection directly (legacy Active Record style).");
        migrate.ToSentence.ShouldBe("Types in the Web layer named `*Controller` must not reference `SqlConnection`.");
        migrate.ToSentence.ShouldBe(rule.Sentence);
        migrate.BaselinePath.ShouldBe("arch/baseline.json");
        migrate.Policy.ShouldBe(MigrationPolicy.MigrateIfSmall);
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
        MigrateData migrate = rule.Migrate.ShouldNotBeNull();
        // .Baseline omitted ⇒ conventional default derived from the rule ID (GRAMMAR §4.4).
        migrate.BaselinePath.ShouldBe("arch/baselines/data-access/no-inline-sql.json");
        migrate.Policy.ShouldBe(MigrationPolicy.MigrateIfSmall);
    }

    [Fact]
    public void QuarantineScope_DesugarsIntoContainmentCarryingBoundaryBaselineDragonsAndAutoFix()
    {
        ArchRule containment = Rule("legacy/billing/containment");

        QuarantineData quarantine = containment.Quarantine.ShouldNotBeNull();
        quarantine.Role.ShouldBe(QuarantineRole.Containment);
        quarantine.Boundary.ShouldBe([typeof(IBillingFacade), typeof(BillingFacade)]);
        quarantine.BaselinePath.ShouldBe("arch/baseline.json");
        quarantine.Dragons.ShouldBe(DragonsProse);
        quarantine.ScopeId.ShouldBe("legacy/billing");
        containment.Constraint.ShouldNotBeNull();
        // Auto-derived fix from the first BoundaryOnlyVia type (GRAMMAR §5.5).
        containment.Fix.ShouldBe("use `IBillingFacade`");
        // The raw quarantined selection rides on the containment child so the renderer can place it.
        quarantine.Quarantined.ShouldNotBeNull();
    }

    [Fact]
    public void QuarantineScope_DesugarsIntoTripwireCarryingDragonsButNoConstraint()
    {
        ArchRule tripwire = Rule("legacy/billing/tripwire");

        QuarantineData quarantine = tripwire.Quarantine.ShouldNotBeNull();
        quarantine.Role.ShouldBe(QuarantineRole.Tripwire);
        quarantine.Dragons.ShouldBe(DragonsProse);
        quarantine.ScopeId.ShouldBe("legacy/billing");
        // Boundary and baseline are containment concerns; the tripwire has no closed-vocabulary law yet.
        quarantine.Boundary.ShouldBeEmpty();
        quarantine.BaselinePath.ShouldBeNull();
        // The quarantined selection rides on the tripwire too so its diff-touch can map changed files.
        quarantine.Quarantined.ShouldNotBeNull();
        tripwire.Constraint.ShouldBeNull();
        tripwire.Sentence.ShouldBe(string.Empty);
    }

    [Fact]
    public void QuarantineScope_SharesBecauseAcrossBothChildren()
    {
        const string because = "Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.";
        Rule("legacy/billing/containment")
            .Because.ShouldBe(because);
        Rule("legacy/billing/tripwire")
            .Because.ShouldBe(because);
    }

    [Fact]
    public void QuarantineScope_WithDragonsDoc_ReifiesLinkedDocPathOnBothChildren()
    {
        ArchitectureModel model = ArchModelBuilder.Build(DragonsDocScopeSpec);

        QuarantineData containment = model.Rule("legacy/billing/containment")
            .Quarantine.ShouldNotBeNull();
        containment.DragonsDoc.ShouldBe("arch/billing-dragons.md");
        containment.Dragons.ShouldBeNull();
        model.Rule("legacy/billing/tripwire")
            .Quarantine.ShouldNotBeNull()
            .DragonsDoc.ShouldBe("arch/billing-dragons.md");
    }

    [Fact]
    public void QuarantineScope_WithoutBaseline_DefaultsContainmentToConventionalPath()
    {
        // DragonsDocScopeSpec omits .Baseline, so the containment child falls back to the default.
        ArchitectureModel model = ArchModelBuilder.Build(DragonsDocScopeSpec);

        QuarantineData containment = model.Rule("legacy/billing/containment")
            .Quarantine.ShouldNotBeNull();
        // .Baseline omitted ⇒ conventional default derived from the containment rule ID (GRAMMAR §4.4/§7).
        containment.BaselinePath.ShouldBe("arch/baselines/legacy/billing/containment.json");
        // The tripwire's baseline stays null — grandfathering is a containment concern.
        model.Rule("legacy/billing/tripwire")
            .Quarantine.ShouldNotBeNull()
            .BaselinePath.ShouldBeNull();
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
            .Rule("time/inject-clock");

        rule.Posture.ShouldBe(Posture.Migrate);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotUseConstraint>();

        // Members in authoring order, each with its authored declaring type + name (GRAMMAR §4.5).
        constraint.Members.Count.ShouldBe(2);
        constraint.Members[0]
            .DeclaringType.ShouldBe(typeof(DateTime));
        constraint.Members[0]
            .Name.ShouldBe("Now");
        constraint.Members[1]
            .DeclaringType.ShouldBe(typeof(DateTime));
        constraint.Members[1]
            .Name.ShouldBe("UtcNow");
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotConstructRule_ReifiesToWalkableConstructConstraint()
    {
        ArchRule rule = ArchModelBuilder.Build(CtorRuleSpec)
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotConstructConstraint>();

        // Targets in authoring order; Operands mirrors Targets (the dependency-verb walk hook, NOT MemberOperands).
        constraint.Targets.ShouldHaveSingleItem();
        constraint.Operands.ShouldBe(constraint.Targets);
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void DependencyVerbConstraint_HasEmptyMemberOperands()
    {
        // The member-operands walk hook is empty for the dependency verbs (GRAMMAR §4.5, §8 items 11–13).
        Rule("layering/domain-independent")
            .Constraint.ShouldNotBeNull()
            .MemberOperands.ShouldBeEmpty();
        // MustNotConstruct is a dependency-shape verb (overrides Operands, not MemberOperands) — its member hook is empty too.
        ArchModelBuilder.Build(CtorRuleSpec)
            .Rules.Single()
            .Constraint.ShouldNotBeNull()
            .MemberOperands.ShouldBeEmpty();
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
            .Rule("naming/async-suffix");

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MemberMustHaveSuffixConstraint>();

        // The member subject: the Methods projection carrying one Returning adjective (GRAMMAR §4.6).
        constraint.MemberSubject.Kind.ShouldBe(MemberKindFilter.Method);
        constraint.MemberSubject.Adjectives.OfType<ReturningAdjective>()
            .ShouldHaveSingleItem();

        // The inherited Subject is the underlying TYPE selection (Subject => MemberSubject.Source), so
        // foreign walks and Quarantine desugaring keep working on the type side.
        constraint.Subject.ShouldBeSameAs(constraint.MemberSubject.Source);
        constraint.Subject!.Noun.ShouldBeOfType<NamespaceNoun>();

        // Member subjects never populate MemberOperands (that hook is the MustNotUse target list).
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void PropertySubjectRule_ReifiesToWalkableMemberConstraint()
    {
        // The .Properties projection mints a PropertySelection, which is what carries the kind-only verb —
        // the reified node is the same walkable MemberConstraint shape every other member verb reifies to.
        var constraint = Checker.Model(arch => arch.Rule("domain/values-immutable")
                .Enforce(arch.Namespace("MyApp.Domain.*").Properties.MustBeGetOnly())
                .Because("A value another thread can write is not a value."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MemberMustBeGetOnlyConstraint>();

        constraint.MemberSubject.Kind.ShouldBe(MemberKindFilter.Property);
        constraint.MemberSubject.ShouldBeOfType<PropertySelection>();
        constraint.Subject.ShouldBeSameAs(constraint.MemberSubject.Source);
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void FieldSubjectRule_ReifiesToWalkableMemberConstraint()
    {
        var constraint = Checker.Model(arch => arch.Rule("state/no-static-mutable")
                .Enforce(arch.Namespace("MyApp.Web.*").Fields.MustBeReadonly())
                .Because("A writable static is process-wide state nothing declares."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MemberMustBeReadonlyConstraint>();

        constraint.MemberSubject.Kind.ShouldBe(MemberKindFilter.Field);
        constraint.MemberSubject.ShouldBeOfType<FieldSelection>();
        constraint.Subject.ShouldBeSameAs(constraint.MemberSubject.Source);
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void StaticAdjective_ReifiesOntoTheMemberSelection_KeepingTheConcreteType()
    {
        // The adjective lands in the member selection's adjective list, and Rebuild returns the concrete
        // type — which is what keeps the kind-only verb reachable after it, in any order.
        var constraint = Checker.Model(arch => arch.Rule("state/no-static-mutable")
                .Enforce(arch.Namespace("MyApp.Web.*").Fields.ThatAreStatic().MustBeReadonly())
                .Because("A writable static is process-wide state nothing declares."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MemberMustBeReadonlyConstraint>();

        constraint.MemberSubject.Adjectives.OfType<MemberThatAreStaticAdjective>()
            .ShouldHaveSingleItem();
        constraint.MemberSubject.ShouldBeOfType<FieldSelection>();
    }

    [Fact]
    public void ProjectSubjectRule_ReifiesToWalkableProjectConstraintWithNoTypeSubject()
    {
        // The third subject stratum (GRAMMAR §4.10). Its ProjectSubject carries the selection, and the
        // inherited type Subject is NULL — there is no underlying type selection to hand up, which is the
        // whole point of departure from a member constraint. Every type-side walk therefore reaches nothing
        // here, and the nullable-flow analysis is what audits that each of them dispatched first.
        var constraint = Checker.Model(arch => arch.Rule("packaging/internal-not-shipped")
                .Enforce(arch.Projects.Matching("Zphil.*").MustNotBePackable())
                .Because("An internal project on the feed is an API nobody meant to promise."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MustNotBePackableConstraint>();

        constraint.Subject.ShouldBeNull();
        constraint.Operands.ShouldBeEmpty();
        constraint.MemberOperands.ShouldBeEmpty();
        constraint.ProjectSubject.Adjectives.OfType<ProjectMatchingAdjective>()
            .ShouldHaveSingleItem()
            .Globs.ShouldBe(["Zphil.*"]);
    }

    [Fact]
    public void ProjectAdjectives_ReifyOntoTheProjectSelectionInAuthoringOrder()
    {
        // Each adjective appends and clones, so the list is the chain in authoring order — the same
        // immutable-value shape the type and member selections take.
        var constraint = Checker.Model(arch => arch.Rule("packaging/locked-restore")
                .Enforce(arch.Projects.Packable()
                    .Named("Zphil.LoadBearing", "Zphil.LoadBearing.Cli")
                    .Except(arch.Projects.Matching("*.Tests"))
                    .MustLockPackages())
                .Because("A drifting package graph is a build nobody can reproduce."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MustLockPackagesConstraint>();

        constraint.ProjectSubject.Adjectives.Select(adjective => adjective.GetType()
                .Name)
            .ShouldBe(["ProjectPackableAdjective", "ProjectNamedAdjective", "ProjectExceptAdjective"]);
    }

    [Fact]
    public void ProjectVerbOperands_ReifyOntoTheirOwnConstraintNodes()
    {
        // The two operand-carrying project verbs publish their operands as their own domain-named lists —
        // never through the type-side Operands hook, which stays empty because a project verb names no
        // type selection at all.
        var target = Checker.Model(arch => arch.Rule("packaging/contract-tfm")
                .Enforce(arch.Projects.Named("Zphil.LoadBearing").MustOnlyTarget("netstandard2.0", "net8.0"))
                .Because("The contract package has to load on every host the estate runs."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MustOnlyTargetConstraint>();

        target.Frameworks.ShouldBe(["netstandard2.0", "net8.0"]);
        target.Operands.ShouldBeEmpty();

        var must = Checker.Model(arch => arch.Rule("packaging/described")
                .Enforce(arch.Projects.Named("Zphil.LoadBearing")
                    .Must(project => project.IsPackable == true, description: "produce a package"))
                .Because("A contract nobody ships is a contract nobody has."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<ProjectMustConstraint>();

        must.Description.ShouldBe("produce a package");
        must.Subject.ShouldBeNull();
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
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MustNotInjectConstraint>();

        constraint.Subject!.Noun.ShouldBeOfType<RegisteredNoun>()
            .Lifetime.ShouldBe(Lifetime.Singleton);
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
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MustNotInjectConstraint>();
        constraint.Subject!.Noun.ShouldBeOfType<RegisteredNoun>()
            .Lifetime.ShouldBeNull();
    }

    [Fact]
    public void MustNotInject_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        ShouldReifyTypeSugarLikeWrappedSelection<MustNotInjectConstraint>(
            "di/no-inject-sql",
            arch => arch.Types.MustNotInject(typeof(SqlConnection)),
            arch => arch.Types.MustNotInject(arch.Type(typeof(SqlConnection))),
            constraint => constraint.Targets,
            typeof(SqlConnection));
    }

    [Fact]
    public void MustNotCatchRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotCatch-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/no-catch-ioe")
                .Enforce(arch.Types.MustNotCatch(typeof(InvalidOperationException)))
                .Because("Swallowing invalid-operation signals hides real defects."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotCatchConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotCatchUnfilteredRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotCatchUnfiltered-rule spec, for the dependency-verb reification + empty-member-hook
        // pins. The filter condition lives in the verb, so the node's shape is the plain catch verb's — no extra
        // operand carries it, and the member hook stays empty.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/filter-broad-catches")
                .Enforce(arch.Types.MustNotCatchUnfiltered(typeof(Exception)))
                .Because("A broad catch names what it expects in a `when` filter."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotCatchUnfilteredConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotSwallowRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotSwallow-rule spec, for the dependency-verb reification + empty-member-hook pins. Both
        // the filter condition and the rethrow condition live in the verb, so the node's shape is the plain catch
        // verb's — no extra operand carries either, and the member hook stays empty.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/no-swallowed-broad-catches")
                .Enforce(arch.Types.MustNotSwallow(typeof(Exception)))
                .Because("A handler that holds a failure and continues hides it."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotSwallowConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotThrowRule_ReifiesToWalkableThrowConstraint()
    {
        // A single MustNotThrow-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/no-bare-bcl-throws")
                .Enforce(arch.Types.MustNotThrow(typeof(Exception)))
                .Because("Bare BCL exception types carry no meaning a caller can dispatch on."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotThrowConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustOnlyThrowRule_ReifiesToWalkableThrowConstraint()
    {
        // A single MustOnlyThrow-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("errors/throw-domain-only")
                .Enforce(arch.Types.MustOnlyThrow(typeof(InvalidOperationException)))
                .Because("Domain code must surface only sanctioned exception types."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustOnlyThrowConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotCatch_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        ShouldReifyTypeSugarLikeWrappedSelection<MustNotCatchConstraint>(
            "errors/no-catch",
            arch => arch.Types.MustNotCatch(typeof(InvalidOperationException)),
            arch => arch.Types.MustNotCatch(arch.Type(typeof(InvalidOperationException))),
            constraint => constraint.Targets,
            typeof(InvalidOperationException));
    }

    [Fact]
    public void MustNotCatchUnfiltered_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        ShouldReifyTypeSugarLikeWrappedSelection<MustNotCatchUnfilteredConstraint>(
            "errors/no-unfiltered-catch",
            arch => arch.Types.MustNotCatchUnfiltered(typeof(Exception)),
            arch => arch.Types.MustNotCatchUnfiltered(arch.Type(typeof(Exception))),
            constraint => constraint.Targets,
            typeof(Exception));
    }

    [Fact]
    public void MustNotSwallow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        ShouldReifyTypeSugarLikeWrappedSelection<MustNotSwallowConstraint>(
            "errors/no-swallow",
            arch => arch.Types.MustNotSwallow(typeof(Exception)),
            arch => arch.Types.MustNotSwallow(arch.Type(typeof(Exception))),
            constraint => constraint.Targets,
            typeof(Exception));
    }

    [Fact]
    public void MustNotThrow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        ShouldReifyTypeSugarLikeWrappedSelection<MustNotThrowConstraint>(
            "errors/no-throw",
            arch => arch.Types.MustNotThrow(typeof(Exception)),
            arch => arch.Types.MustNotThrow(arch.Type(typeof(Exception))),
            constraint => constraint.Targets,
            typeof(Exception));
    }

    [Fact]
    public void MustOnlyThrow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        ShouldReifyTypeSugarLikeWrappedSelection<MustOnlyThrowConstraint>(
            "errors/throw-only",
            arch => arch.Types.MustOnlyThrow(typeof(InvalidOperationException)),
            arch => arch.Types.MustOnlyThrow(arch.Type(typeof(InvalidOperationException))),
            constraint => constraint.Targets,
            typeof(InvalidOperationException));
    }

    [Fact]
    public void MustNotExposeRule_ReifiesToWalkableExposeConstraint()
    {
        // A single MustNotExpose-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.Model(arch => arch.Rule("api/no-leaky-surface")
                .Enforce(arch.Types.MustNotExpose(typeof(SqlConnection)))
                .Because("Public signatures must not leak infrastructure types."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotExposeConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotExpose_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        ShouldReifyTypeSugarLikeWrappedSelection<MustNotExposeConstraint>(
            "api/no-expose",
            arch => arch.Types.MustNotExpose(typeof(SqlConnection)),
            arch => arch.Types.MustNotExpose(arch.Type(typeof(SqlConnection))),
            constraint => constraint.Targets,
            typeof(SqlConnection));
    }

    // ---- Membership and coverage verbs (GRAMMAR §5.3, §4.1, §4.7): one operand-carrying node, one
    //      string-carrying node and one nullary node ----

    [Fact]
    public void MustBelongToRule_ReifiesToWalkableMembershipConstraint()
    {
        // The coverage verb stores its memberships on the shared operand list, so the generic walks reach
        // them with no special-casing, and Memberships is a domain-named alias over that one list rather
        // than a second copy — which is what asserting Operands against it holds.
        ArchRule rule = Checker.Model(arch => arch.Rule("layering/no-ungoverned-types")
                .Enforce(arch.Types.MustBelongTo(arch.Namespace("MyApp.Domain.*")))
                .Because("A type in no declared layer is governed by nothing."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustBelongToConstraint>(constraint => constraint.Memberships);
    }

    [Fact]
    public void MustBelongTo_ExplicitTypeMembership_RoundTripsThroughTheOperandList()
    {
        // There is deliberately NO (Type first, params Type[] more) sugar twin on this verb, and the
        // absence is the point of this row: a membership names WHERE a type may live — a layer, a project,
        // a namespace — so a bare typeof operand would degenerate into "must be that type" rather than
        // "must belong to". Writing the type selection out is still legal, and reifies like any other
        // membership, which is what keeps the missing overload a choice rather than a gap.
        var constraint = Checker.Model(arch => arch.Rule("legacy/billing/facade-only")
                .Enforce(arch.Types.MustBelongTo(
                    arch.Type(typeof(IBillingFacade)), arch.Namespace("MyApp.Domain.*")))
                .Because("Reason."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MustBelongToConstraint>();

        // Memberships in authoring order, each carrying its own noun.
        constraint.Memberships.Count.ShouldBe(2);
        constraint.Memberships[0]
            .Noun.ShouldBeOfType<TypeNoun>()
            .Type.ShouldBe(typeof(IBillingFacade));
        constraint.Memberships[1]
            .Noun.ShouldBeOfType<NamespaceNoun>()
            .Glob.ShouldBe("MyApp.Domain.*");
        constraint.Operands.ShouldBe(constraint.Memberships);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpartRule_ReifiesToWalkableCorrespondenceConstraint()
    {
        // The correspondence verb stores its one among selection on the shared operand list, exactly as the
        // coverage verb stores its memberships — so the generic walks (foreign-Arch validation, the pattern
        // walk, Quarantine desugaring) reach it with no arm of their own, and the template rides the node.
        ArchRule rule = Checker.Model(arch => arch.Rule("naming/one-interface-per-service")
                .Enforce(arch.Types.MustHaveExactlyOneCounterpart(
                    among: arch.Namespace("MyApp.Contracts.*"), named: "I{Name}"))
                .Because("A service with no interface cannot be substituted in a test."))
            .Rules.Single();

        rule.ShouldReifyToWalkableDependencyConstraint<MustHaveExactlyOneCounterpartConstraint>(constraint => constraint.Among);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_AmongAndTemplate_RoundTripThroughTheOperandList()
    {
        // Deliberately ONE among selection rather than a params list: several would reopen the ALL/ANY
        // question the coverage verb answers with an or-join, and a correspondence law has no reading under
        // which two homes each hold exactly one counterpart. Authors union candidate homes with arch.AnyOf,
        // which reifies as one operand — which is why Among is asserted against Operands rather than beside
        // them. The template is stored verbatim: substitution happens at check time, never at mint.
        var constraint = Checker.Model(arch => arch.Rule("naming/one-interface-per-service")
                .Enforce(arch.Types.MustHaveExactlyOneCounterpart(
                    among: arch.Namespace("MyApp.Contracts.*"), named: "I{Name}"))
                .Because("Reason."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<MustHaveExactlyOneCounterpartConstraint>();

        constraint.Among.ShouldHaveSingleItem()
            .Noun.ShouldBeOfType<NamespaceNoun>()
            .Glob.ShouldBe("MyApp.Contracts.*");
        constraint.Template.ShouldBe("I{Name}");
        constraint.Operands.ShouldBe(constraint.Among);
    }

    [Fact]
    public void MustResideInProjectRule_ReifiesToWalkableProjectConstraint()
    {
        // A string-carrying shape verb: the project name rides on the node itself, so the rule names no
        // selection beyond its subject and both walk hooks stay empty.
        ArchRule rule = Checker.Model(arch => arch.Rule("layering/services-in-web")
                .Enforce(arch.Types.MustResideInProject("MyApp.Web"))
                .Because("A service type belongs to the project that hosts it."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustResideInProjectConstraint>();

        constraint.ProjectName.ShouldBe("MyApp.Web");
        constraint.Operands.ShouldBeEmpty();
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustBeRegisteredRule_ReifiesToWalkableNullaryConstraint()
    {
        // The nullary shape verb: its membership is an extracted fact rather than an authored operand, so
        // there is nothing on the node for a walk to reach and the subject is the only selection the rule
        // names — the node's whole payload is which verb it is.
        ArchRule rule = Checker.Model(arch => arch.Rule("di/handlers-registered")
                .Enforce(arch.Types.MustBeRegistered())
                .Because("A type the container never sees cannot be resolved."))
            .Rules.Single();

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustBeRegisteredConstraint>();

        constraint.Operands.ShouldBeEmpty();
        constraint.MemberOperands.ShouldBeEmpty();
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
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

        union.Parts.Select(part => part.Noun.ShouldBeOfType<ProjectNoun>()
                .Name)
            .ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void AnyOf_NestedUnionCarryingAdjectives_StaysALeaf()
    {
        // An inner union with adjectives is a narrowed set of its own — flattening it would lose the
        // narrowing, so it survives as one operand.
        var arch = new Arch();
        Selection inner = arch.AnyOf(arch.Project("A"), arch.Project("B")).Except(arch.Type(typeof(SqlConnection)));
        var outer = arch.AnyOf(inner, arch.Project("C"))
            .ShouldBeOfType<UnionSelection>();

        outer.Parts.Count.ShouldBe(2);
        outer.Parts[0]
            .ShouldBeOfType<UnionSelection>()
            .Parts.Count.ShouldBe(2);
    }

    [Fact]
    public void AnyOf_Adjective_IsOwnedByTheUnionNotDistributedThroughIt()
    {
        // (a ∪ b) − c, not (a − c) ∪ (b − c): the union keeps its two operands and grows its own adjective.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"), arch.Project("B")).Except(arch.Type(typeof(SqlConnection)))
            .ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(2);
        union.Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<ExceptAdjective>();
    }

    [Fact]
    public void Authored_AppendsOneAuthoredAdjective()
    {
        // The adjective carries no payload — the whole statement is the placement and the fragment — so
        // the model pin is that one lands, on the union as well as on a plain selection.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"), arch.Project("B")).Authored()
            .ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(2);
        union.Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<AuthoredAdjective>();
    }

    [Fact]
    public void AnyOf_SingleOperand_StaysAUnionInTheModel()
    {
        // Legal and an identity (§2 principle 5): a loop that yields one operand must not become an error,
        // and the node stays a union so the shape does not depend on how many times the loop ran.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"))
            .ShouldBeOfType<UnionSelection>();

        union.Parts.ShouldHaveSingleItem();
        union.Adjectives.ShouldBeEmpty();
    }

    /// <summary>
    ///     Asserts a verb's bare-<c>Type</c> sugar overload reifies to exactly what the <c>arch.Type(…)</c>
    ///     spelling does (GRAMMAR §3.3). The sugar wraps each bare type as a single-type selection, so both
    ///     spellings carry one bare <see cref="TypeNoun" /> operand for <paramref name="expected" />, and it
    ///     carries no adjectives.
    /// </summary>
    private static void ShouldReifyTypeSugarLikeWrappedSelection<TConstraint>(
        string ruleId,
        Func<Arch, Constraint> sugar,
        Func<Arch, Constraint> wrapped,
        Func<TConstraint, IReadOnlyList<Selection>> targets,
        Type expected)
        where TConstraint : OperandConstraint
    {
        var sugared = Checker.Model(arch => arch.Rule(ruleId)
                .Enforce(sugar(arch))
                .Because("Reason."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<TConstraint>();
        var handWritten = Checker.Model(arch => arch.Rule(ruleId)
                .Enforce(wrapped(arch))
                .Because("Reason."))
            .Rules.Single()
            .Constraint
            .ShouldBeOfType<TConstraint>();
        IReadOnlyList<Selection> sugaredTargets = targets(sugared);
        IReadOnlyList<Selection> handWrittenTargets = targets(handWritten);

        Type sugarType = sugaredTargets.ShouldHaveSingleItem()
            .Noun.ShouldBeOfType<TypeNoun>()
            .Type;
        Type wrappedType = handWrittenTargets[0]
            .Noun.ShouldBeOfType<TypeNoun>()
            .Type;
        sugarType.ShouldBe(expected);
        wrappedType.ShouldBe(sugarType);
        sugaredTargets[0]
            .Adjectives.ShouldBeEmpty();
    }
}

/// <summary>
///     The shared claim the operand-carrying-verb rows above make about a reified <see cref="ArchRule" />, as
///     an extension so the rule each row already arranged reads as the sentence's subject.
/// </summary>
/// <remarks>
///     <para>
///         <c>file</c>-scoped because it encodes one suite's claim about the dependency-verb walk hook rather
///         than reusable vocabulary — a top-level class would put it in every test file's extension lookup for
///         a helper only these rows want.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
file static class ArchRuleReificationAssertions
{
    /// <summary>
    ///     Asserts <paramref name="rule" /> reified to the walkable shape every operand-carrying verb shares —
    ///     the dependency verbs and the coverage verb alike: an Enforce <typeparamref name="TConstraint" />
    ///     carrying one operand, with the generic walk reaching that operand through <c>Operands</c>, an empty
    ///     member hook, and the subject selection intact.
    /// </summary>
    /// <remarks>
    ///     <paramref name="targets" /> is a parameter because each verb declares its own domain-named list
    ///     (<c>Targets</c>, <c>Memberships</c>) rather than inheriting one from
    ///     <see cref="OperandConstraint" /> — reading it through the concrete type is what keeps every row
    ///     pinning the property its own verb publishes.
    /// </remarks>
    internal static void ShouldReifyToWalkableDependencyConstraint<TConstraint>(
        this ArchRule rule, Func<TConstraint, IReadOnlyList<Selection>> targets)
        where TConstraint : OperandConstraint
    {
        string report = Describe(rule);
        rule.Posture.ShouldBe(Posture.Enforce, report);
        var constraint = rule.Constraint.ShouldBeOfType<TConstraint>(report);
        IReadOnlyList<Selection> declared = targets(constraint);

        // Operands in authoring order; the verb's own list is an alias over them (the operand walk hook, NOT
        // MemberOperands).
        declared.ShouldHaveSingleItem(report);
        constraint.Operands.ShouldBe(declared, report);
        // An operand-carrying verb overrides Operands, not MemberOperands — its member hook is empty.
        constraint.MemberOperands.ShouldBeEmpty(report);
        // Subject selection intact — the bare Types noun, no adjectives.
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>(report);
        constraint.Subject.Adjectives.ShouldBeEmpty(report);
    }

    /// <summary>The rule's identity, posture and the constraint type it actually reified to.</summary>
    private static string Describe(ArchRule rule)
    {
        string reified = rule.Constraint?.GetType()
            .Name ?? "(no constraint)";
        return $"Rule '{rule.Id}' ({rule.Posture}) reified {reified}.";
    }
}
