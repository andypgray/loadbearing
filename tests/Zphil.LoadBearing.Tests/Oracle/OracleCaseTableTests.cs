using ArchUnitNET.Fluent;
using MyApp.Web;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Oracle;

/// <summary>
///     The differential-testing oracle (Phase 10 Deliverable 1). LoadBearing's checker builds its
///     dependency model from <em>Roslyn source</em>; ArchUnitNET builds its model from <em>compiled IL</em>
///     (Mono.Cecil). Each row below expresses the same architecture rule on both substrates over the same
///     <c>MyApp</c> fixture and asserts they reach the <em>same verdict</em> — the set of type FullNames
///     that violate. Every row pins that set against a hand-derived expected truth <em>and</em> asserts the
///     two substrates equal each other: agreeing with each other alone could mean both are wrong the same
///     way, so the pinned truth is the third leg.
/// </summary>
/// <remarks>
///     <para>
///         The comparison is deliberately <b>raw-constraint, verdict-level, type granularity</b>. The
///         following LoadBearing behaviour is <b>out of oracle scope</b> — a documented boundary, not a
///         silent omission (PLAN.md "compare raw constraints only"):
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Escape hatches</b> (<c>.Where(...)</c> / <c>.Must(...)</c>): arbitrary C# predicates that
///             ArchUnitNET cannot see, so there is no analog to compare against.
///         </item>
///         <item>
///             <b>Posture machinery</b>: Migrate/Freeze baselines, ratchet grandfathering, and the Freeze
///             tripwire's diff-aware touch check. ArchUnitNET has no posture concept; only the raw
///             constraint a posture reduces to (e.g. Freeze → <c>MustOnlyBeReferencedBy</c>, row 7) is
///             compared.
///         </item>
///         <item>
///             <b>LoadBearing-specific verdict rules</b>: the inert-target warning (a matched-nothing
///             forbidden set) and the empty-subject fail-by-default. These are asserted directly in the
///             checker's own tests; the oracle rows are all non-empty-subject, non-inert by construction.
///         </item>
///         <item>
///             <b><c>file:line</c> locations</b>: ArchUnitNET carries none. This is precisely why the
///             comparison is at type granularity — LoadBearing's per-site locations have nothing to compare
///             against.
///         </item>
///     </list>
/// </remarks>
public sealed class OracleCaseTableTests(WorkspaceFixture workspace, OracleArchitecture oracle) : IClassFixture<OracleArchitecture>
{
    // Row 1: Domain must not reference Web. Only MyApp.Domain.OrderService reaches into Web
    // (new HomeController(), typeof(HomeController), and the WebTextExtensions extension call).
    [Fact]
    public void Row1_DomainMustNotReferenceWeb()
    {
        var loadBearing = LoadBearingReferenceViolators(arch =>
            arch.Rule("oracle/domain-not-web")
                .Enforce(arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*")))
                .Because("Oracle row 1: Domain must not reference Web."));

        IArchRule rule = ArchRuleDefinition.Types().That()
            .ResideInNamespace("MyApp.Domain")
            .Should().NotDependOnAnyTypesThat().ResideInNamespace("MyApp.Web");
        var archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(loadBearing, archUnit, "MyApp.Domain.OrderService");
    }

    // Row 2: Web must not reference Domain. No Web type reaches down into Domain — clean on both.
    [Fact]
    public void Row2_WebMustNotReferenceDomain()
    {
        var loadBearing = LoadBearingReferenceViolators(arch =>
            arch.Rule("oracle/web-not-domain")
                .Enforce(arch.Layer("Web", "MyApp.Web.*").MustNotReference(arch.Layer("Domain", "MyApp.Domain.*")))
                .Because("Oracle row 2: Web must not reference Domain."));

        IArchRule rule = ArchRuleDefinition.Types().That()
            .ResideInNamespace("MyApp.Web")
            .Should().NotDependOnAnyTypesThat().ResideInNamespace("MyApp.Domain");
        var archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(loadBearing, archUnit);
    }

    // Row 3: Billing must not reference Web. Billing depends only downward/internally — clean on both.
    [Fact]
    public void Row3_BillingMustNotReferenceWeb()
    {
        var loadBearing = LoadBearingReferenceViolators(arch =>
            arch.Rule("oracle/billing-not-web")
                .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
                .Because("Oracle row 3: Billing must not reference Web."));

        IArchRule rule = ArchRuleDefinition.Types().That()
            .ResideInNamespace("MyApp.Legacy.Billing")
            .Should().NotDependOnAnyTypesThat().ResideInNamespace("MyApp.Web");
        var archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(loadBearing, archUnit);
    }

    // Row 4: Web *Controller types must not reference System.Data (an EXTERNAL target, kept on both
    // sides for MustNot*). Both controllers return/new a System.Data.DataTable.
    [Fact]
    public void Row4_ControllersMustNotReferenceSystemData()
    {
        var loadBearing = LoadBearingReferenceViolators(arch =>
            arch.Rule("oracle/controllers-no-system-data")
                .Enforce(arch.Namespace("MyApp.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
                .Because("Oracle row 4: controllers must not touch System.Data."));

        IArchRule rule = ArchRuleDefinition.Types().That()
            .ResideInNamespace("MyApp.Web").And().HaveNameEndingWith("Controller")
            .Should().NotDependOnAnyTypesThat().ResideInNamespace("System.Data");
        var archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(loadBearing, archUnit, "MyApp.Web.HomeController", "MyApp.Web.InvoiceController");
    }

    // Row 5: interfaces under MyApp.* must be I-prefixed. IHandler and IBillingFacade both are — clean.
    [Fact]
    public void Row5_InterfacesMustHaveIPrefix()
    {
        var loadBearing = LoadBearingShapeViolators(arch =>
            arch.Rule("oracle/interface-prefix")
                .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*").MustHavePrefix("I"))
                .Because("Oracle row 5: interfaces are I-prefixed."));

        IArchRule rule = ArchRuleDefinition.Interfaces().That()
            .ResideInAssembly(oracle.Domain, oracle.Web, oracle.Billing)
            .Should().HaveNameStartingWith("I");
        var archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(loadBearing, archUnit);
    }

    // Row 6: types implementing IHandler<T> must be *Handler-suffixed. RefundProcessor implements
    // IHandler<InvoiceCreated> but lacks the suffix; InvoiceCreatedHandler has it. HIGHEST-RISK row:
    // open-generic / transitive interface matching across the two substrates.
    [Fact]
    public void Row6_HandlerImplementorsMustHaveHandlerSuffix()
    {
        var loadBearing = LoadBearingShapeViolators(arch =>
            arch.Rule("oracle/handler-suffix")
                .Enforce(arch.Types.Implementing(typeof(IHandler<>)).MustHaveSuffix("Handler"))
                .Because("Oracle row 6: handler implementors carry the Handler suffix."));

        IArchRule rule = ArchRuleDefinition.Types().That()
            .ImplementInterface(oracle.HandlerInterface())
            .Should().HaveNameEndingWith("Handler");
        var archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(loadBearing, archUnit, "MyApp.Web.RefundProcessor");
    }

    // Row 7: raw frozen-containment (what a Freeze scope desugars to). The frozen interior
    // {BillingCalculator, RoundingMode} may be referenced only from within Billing or via the facade;
    // InvoiceController reaches the interior directly, HomeController rides the IBillingFacade facade.
    [Fact]
    public void Row7_FrozenInteriorContainment()
    {
        var loadBearing = LoadBearingReferenceViolators(arch =>
        {
            Selection frozen = arch.Namespace("MyApp.Legacy.Billing.*");
            Selection facadeImpl = arch.Types.WithNameMatching("BillingFacade");
            Selection facadeIface = arch.Types.WithNameMatching("IBillingFacade");
            arch.Rule("oracle/frozen-containment")
                .Enforce(frozen.Except(facadeImpl).Except(facadeIface)
                    .MustOnlyBeReferencedBy(frozen, facadeImpl, facadeIface))
                .Because("Oracle row 7: frozen billing interior is facade-only.");
        });

        // The interior is the ArchUnitNET analog of frozen.Except(facadeImpl).Except(facadeIface).
        oracle.FrozenInterior().Select(type => type.FullName).ShouldBe(
            ["MyApp.Legacy.Billing.BillingCalculator", "MyApp.Legacy.Billing.RoundingMode"], true);

        // Equivalent to inbound containment: no outsider (a MyApp type outside Billing) may depend on
        // the interior. Only MyApp.Web.InvoiceController does.
        IArchRule rule = ArchRuleDefinition.Types().That()
            .ResideInAssembly(oracle.Domain, oracle.Web)
            .Should().NotDependOnAny(oracle.FrozenInterior());
        var archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(loadBearing, archUnit, "MyApp.Web.InvoiceController");
    }

    // Row 8 (Phase 13 member-use, best-effort): the ambient-clock ban at caller-type granularity. LoadBearing's
    // MustNotUse(DateTime.Now, DateTime.UtcNow) flags the using TYPE (GRAMMAR §4.5); ArchUnitNET sees the IL
    // getter calls (a property read compiles to get_Now()/get_UtcNow()). ArchUnitNET has no fluent member-call
    // predicate, so its dependency model is queried directly (OracleArchitecture) — same substrate, verdict-level.
    // Only HomeController reads the clock.
    [Fact]
    public void Row8_AmbientClockReadsAtCallerTypeGranularity()
    {
        var loadBearing = LoadBearingMemberUseViolators(arch =>
            arch.Rule("oracle/no-ambient-clock")
                .Enforce(arch.Types.MustNotUse(
                    arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                    arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
                .Because("Oracle row 8: no ambient-clock reads."));

        var archUnit = oracle.TypesReadingAmbientClock();

        AssertOracleAgreement(loadBearing, archUnit, "MyApp.Web.HomeController");
    }

    /// <summary>
    ///     The oracle assertion: both substrates equal the pinned expected set (so a shared blind spot is
    ///     caught), and equal each other (the agreement claim). Sets are compared order-insensitively.
    /// </summary>
    private static void AssertOracleAgreement(
        IReadOnlySet<string> loadBearing, IReadOnlySet<string> archUnit, params string[] expected)
    {
        loadBearing.ShouldBe(expected, true);
        archUnit.ShouldBe(expected, true);
        loadBearing.ShouldBe(archUnit, true);
    }

    private IReadOnlySet<string> LoadBearingReferenceViolators(Action<Arch> define)
    {
        RuleResult result = Checker.Run(workspace.Model, define).Single();
        return result.Violations
            .Where(violation => violation.Kind == ViolationKind.Reference)
            .Select(violation => violation.Source!.FullName)
            .ToHashSet(StringComparer.Ordinal);
    }

    private IReadOnlySet<string> LoadBearingShapeViolators(Action<Arch> define)
    {
        RuleResult result = Checker.Run(workspace.Model, define).Single();
        return result.Violations
            .Where(violation => violation.Kind == ViolationKind.Shape)
            .Select(violation => violation.Subject!.FullName)
            .ToHashSet(StringComparer.Ordinal);
    }

    private IReadOnlySet<string> LoadBearingMemberUseViolators(Action<Arch> define)
    {
        RuleResult result = Checker.Run(workspace.Model, define).Single();
        return result.Violations
            .Where(violation => violation.Kind == ViolationKind.MemberUse)
            .Select(violation => violation.Source!.FullName)
            .ToHashSet(StringComparer.Ordinal);
    }
}