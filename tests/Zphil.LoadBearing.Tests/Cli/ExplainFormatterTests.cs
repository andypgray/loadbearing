using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The <c>explain</c> field dump (R4), pinned over the canonical sample's rule shapes in-process:
///     the <c>&lt;id&gt; (&lt;posture&gt;)</c> header (with the Freeze role), each present field once,
///     the posture payloads (Migrate <c>from</c>/<c>policy</c>/<c>baseline</c>; Freeze scope/boundary/
///     baseline/dragons), and the tripwire's sentence-less, boundary-less form. <c>Fix</c> renders here
///     even though it stays out of the always-on block.
/// </summary>
public sealed class ExplainFormatterTests
{
    private static readonly ArchitectureModel Canonical = ArchModelBuilder.Build(new ArchSpec());

    private static string Dump(string id)
    {
        return string.Join("\n", ExplainFormatter.Lines(Canonical.Rules.Single(rule => rule.Id == id)));
    }

    [Fact]
    public void Enforce_WithFix_DumpsHeaderSentenceBecauseFix()
    {
        Dump("layering/domain-independent").ShouldBe(
            "layering/domain-independent (enforce)\n" +
            "  sentence: The Domain layer must not reference the Web layer.\n" +
            "  because: Domain is UI-agnostic; transaction boundaries live in services.\n" +
            "  fix: Define an abstraction in Domain and implement it in Web.");
    }

    [Fact]
    public void Enforce_WithoutFix_OmitsTheFixLine()
    {
        Dump("naming/interfaces").ShouldBe(
            "naming/interfaces (enforce)\n" +
            "  sentence: Interfaces in `MyApp.*` must be named `I*`.\n" +
            "  because: House naming convention; agents grep by I-prefix.");
    }

    [Fact]
    public void Migrate_DumpsFromPolicyAndBaseline()
    {
        Dump("data-access/no-inline-sql").ShouldBe(
            "data-access/no-inline-sql (migrate)\n" +
            "  sentence: Types in the Web layer named `*Controller` must not reference `SqlConnection`.\n" +
            "  because: Repository pattern for testability — ADR-012.\n" +
            "  fix: Inject the repository; see OrdersRepository for the pattern.\n" +
            "  from: Controllers open SqlConnection directly (legacy Active Record style).\n" +
            "  policy: MigrateIfSmall\n" +
            "  baseline: arch/baseline.json");
    }

    [Fact]
    public void FreezeContainment_DumpsRoleScopeBoundaryBaselineAndDragons()
    {
        Dump("legacy/billing/containment").ShouldBe(
            "legacy/billing/containment (freeze/containment)\n" +
            "  sentence: Types in `MyApp.Legacy.Billing.*`, except `IBillingFacade` or `BillingFacade` " +
            "must be referenced only by types in `MyApp.Legacy.Billing.*`, `IBillingFacade` or `BillingFacade`.\n" +
            "  because: Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
            "  fix: use `IBillingFacade`\n" +
            "  scope: legacy/billing\n" +
            "  boundary: `IBillingFacade`, `BillingFacade`\n" +
            "  baseline: arch/baseline.json\n" +
            "  dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
            "Nightly reconciliation depends on this. Do not normalize.");
    }

    [Fact]
    public void FreezeTripwire_DumpsRoleScopeAndDragons_NoSentenceOrBoundary()
    {
        Dump("legacy/billing/tripwire").ShouldBe(
            "legacy/billing/tripwire (freeze/tripwire)\n" +
            "  because: Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
            "  scope: legacy/billing\n" +
            "  dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
            "Nightly reconciliation depends on this. Do not normalize.");
    }

    [Fact]
    public void FreezeWithDragonsDoc_PrintsThePathOnly_NotDragonsOrBoundary()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new DragonsDocSpec());
        string dump = string.Join("\n", ExplainFormatter.Lines(model.Rules.Single(rule => rule.Id == "legacy/billing/containment")));

        dump.ShouldContain("  dragons-doc: arch/billing-dragons.md");
        dump.ShouldNotContain("  dragons:");
        dump.ShouldNotContain("  boundary:"); // hermetic freeze — no sanctioned surface
    }

    // A hermetic frozen scope documented via a linked file rather than inline dragons prose.
    private sealed class DragonsDocSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing")
                .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .DragonsDoc("arch/billing-dragons.md")
                .Because("Replacement scheduled; see the linked doc.");
        }
    }
}
