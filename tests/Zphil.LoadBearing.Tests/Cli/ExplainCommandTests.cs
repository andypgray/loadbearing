using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>explain</c> through the real command tree, all on the DLL fast path (no workspace,
///     so fast): a rule dumps its fields and exits 0; an unknown ID exits 2 with the ordinal-sorted list
///     of available (post-desugar) IDs; a missing <c>rule-id</c> argument is a parse error remapped to
///     exit 2.
/// </summary>
public sealed class ExplainCommandTests
{
    [Fact]
    public async Task Explain_EnforceRule_DumpsFieldsAndExitsZero()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "layering/domain-independent", "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldSucceed();
        result.Out.NormalizedTrimmed().ShouldBe(
            ("layering/domain-independent (enforce)\n" +
             "  sentence: The Domain layer must not reference the Web layer.\n" +
             "  because: Domain is UI-agnostic; transaction boundaries live in services.\n" +
             "  fix: Define an abstraction in Domain and implement it in Web.").NormalizedTrimmed());
    }

    [Fact]
    public async Task Explain_MigrateRuleWithoutBaseline_DumpsConventionalBaselinePath()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "data-access/no-inline-sql", "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldSucceed("data-access/no-inline-sql (migrate)");
        result.Out.ShouldContain("  from: Some controllers open database connections directly.");
        result.Out.ShouldContain("  policy: MigrateIfSmall");
        // .Baseline omitted ⇒ the conventional default path is filled at model build (GRAMMAR §4.4).
        result.Out.ShouldContain("  baseline: arch/baselines/data-access/no-inline-sql.json");
    }

    [Fact]
    public async Task Explain_UnknownRuleId_ExitsTwoWithSortedListing()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "no/such/rule", "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldRefuseWith();
        // The post-desugar ID set includes the quarantined scope's containment + tripwire children (GRAMMAR §7).
        result.Err.NormalizedTrimmed().ShouldContain(
            "Unknown rule ID 'no/such/rule'. Available rule IDs:\n" +
            "  api/return-dtos\n" +
            "  async/accept-cancellation\n" +
            "  data-access/no-inline-sql\n" +
            "  di/handlers-via-registry\n" +
            "  di/no-captive-dependencies\n" +
            "  exceptions/domain-throws-domain\n" +
            "  exceptions/no-bare-bcl-throw\n" +
            "  exceptions/no-general-catch\n" +
            "  exceptions/no-swallowed-catch\n" +
            "  exceptions/no-unfiltered-catch\n" +
            "  layering/billing-independent\n" +
            "  layering/domain-independent\n" +
            "  layering/no-ghost\n" +
            "  legacy/billing/containment\n" +
            "  legacy/billing/tripwire\n" +
            "  naming/async-suffix\n" +
            "  naming/nonexistent");
    }

    [Fact]
    public async Task Explain_MissingRuleId_ExitsTwo()
    {
        CliResult result = await CliRunner.InvokeAsync("explain", "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldRefuseWith();
    }
}
