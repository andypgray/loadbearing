using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end scope tripwires against a real git repo (<see cref="TempGitRepo" />).
///     <c>check --diff-base HEAD</c> warns for changed files inside a scope and
///     never gates on those warnings: an untracked new file in dragon territory (the agent-hook case,
///     found via <c>git ls-files --others</c>) warns and exits 0, while a tracked change combined with a
///     new interior reference exits 1 — the exit code is containment-driven, the tripwire only warns.
///     Both postures are here because they warn in different words about different questions, and a
///     Caution has no containment at all: its rows touch a Domain file neither quarantine covers, so the
///     exit 0 they report is the whole verdict rather than a red rule declining to fire.
/// </summary>
/// <remarks>
///     The <c>--hook-json</c> rows are the other end of that same warning: a rule that only ever warns has
///     only exit 0 to travel on, and until this mode the wrapper discarded it. They belong here rather than
///     with the rest of the check e2e rows because they need what this class already builds — a real
///     repository with a real touched file in a real quarantined scope — and a mode that shapes the exit-0
///     channel cannot be measured on a run that has nothing to say.
/// </remarks>
[Collection("Serial")]
public sealed class TripwireDiffE2ETests
{
    // The caution rows all touch the same tracked file and read back the same two lines, on three channels.
    // Held as constants because the claim is that the three channels carry one message, and three inline
    // copies would let two of them drift apart while every row stayed green.
    private const string CautionTouch = "\n// touched by the caution tripwire test\n";

    private const string CautionWarning =
        "Changed file 'MyApp.Domain/RetryPolicy.cs' is inside cautioned scope 'domain/retry-budget' — " +
        "read the dragons before editing: loadbearing explain domain/retry-budget/tripwire.";

    private const string CautionDragons =
        "  dragons: RetryPolicy's broad catch is filtered on purpose: the `when` clause is what keeps it " +
        "green under the unfiltered-catch rule, and it is the fixture's one sanctioned broad handler. " +
        "Keep the filter; add cases beside it, never inside it.";

    // The one touched Domain file every caution row runs against, read on three channels. The rows differ
    // only in the flags they add and none of them mutates further, so the leased tree, its reset and its git
    // commit are the class's rather than each row's. The repo is disposed inside the lambda, so the lease is
    // released before the class's other rows ask for one.
    private static readonly Lazy<Task<(CliResult Human, CliResult Json, CliResult Hook)>> Caution = new(async () =>
    {
        using var repo = new TempGitRepo();
        File.AppendAllText(repo.PathOf("MyApp.Domain", "RetryPolicy.cs"), CautionTouch);

        string[] args =
            ["check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD"];
        return (
            await CliRunner.InvokeAsync(args),
            await CliRunner.InvokeAsync([.. args, "--json"]),
            await CliRunner.InvokeAsync([.. args, "--hook-json"]));
    });

    private const string QuarantineWarning =
        "Changed file 'MyApp.Legacy.Billing/LegacyNote.cs' is inside quarantined scope 'legacy/billing'";

    private const string QuarantineDragons =
        "  dragons: Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.";

    // One untracked file in dragon territory, read on two channels. The rows differ only in the flag they
    // add and neither mutates further, so the leased tree — and the wholesale workspace reload a new source
    // file forces on it — is paid once for the pair rather than once per row.
    private static readonly Lazy<Task<(CliResult Human, CliResult Hook)>> Untracked = new(async () =>
    {
        using var repo = new TempGitRepo();
        // A brand-new, still-untracked file in the quarantined billing project — SDK globs compile it in.
        repo.WriteQuarantineNote();

        string[] args =
            ["check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD"];
        return (await CliRunner.InvokeAsync(args), await CliRunner.InvokeAsync([.. args, "--hook-json"]));
    });

    [Fact]
    public async Task CheckDiffBase_UntrackedFileInQuarantinedScope_WarnsAndExitsZero()
    {
        CliResult result = (await Untracked.Value).Human;

        result.ShouldSucceed("warn legacy/billing/tripwire");
        result.Out.ShouldContain(
            "warning: Changed file 'MyApp.Legacy.Billing/LegacyNote.cs' is inside quarantined scope 'legacy/billing' — " +
            "does the task actually require editing dragon territory? Dragons: loadbearing explain legacy/billing/tripwire.");
        // The dragons ride under the warning that fired, so the agent mid-edit reads them here rather than
        // paying a round trip to `explain` for prose the rule already carries.
        result.Out.ShouldContain(QuarantineDragons);
    }

    [Fact]
    public async Task CheckDiffBase_ContainmentRedPlusTouch_ExitsOneWithBoth()
    {
        using var repo = new TempGitRepo();
        // Touch a tracked file inside the quarantined scope (tripwire warning) ...
        File.AppendAllText(repo.PathOf("MyApp.Legacy.Billing", "BillingCalculator.cs"), "\n// touched by the tripwire test\n");
        // ... and add a NEW interior reference from outside the scope (containment red).
        FixtureEdits.AppendMemberLine(
            repo.PathOf("MyApp.Web", "HomeController.cs"), "    public BillingCalculator NewCalculator() => new BillingCalculator();");

        CliResult result = await CliRunner.InvokeAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD");

        // Exit code is containment-driven only; the tripwire warning rides alongside.
        result.ShouldReportViolations("FAIL legacy/billing/containment");
        result.Out.ShouldContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.BillingCalculator");
        result.Out.ShouldContain("warn legacy/billing/tripwire");
        result.Out.ShouldContain("Changed file 'MyApp.Legacy.Billing/BillingCalculator.cs' is inside quarantined scope 'legacy/billing'");
        result.Out.ShouldContain(QuarantineDragons);
    }

    [Fact]
    public async Task CheckDiffBase_TouchedFileInCautionedScope_WarnsWithItsDragonsAndExitsZero()
    {
        // The quarantine row's twin for the posture with no red state at all. The exit code above is
        // containment-driven and this spec's containment holds, so exit 0 here is not "the warning did not
        // gate" but "there was never anything else to gate on" — which is what makes a caution's whole
        // verdict a warning, and this the only spec in the suite that can prove it.
        CliResult result = (await Caution.Value).Human;

        result.ShouldSucceed("warn domain/retry-budget/tripwire");
        // A different question from the quarantine's wording: not whether the task belongs here at all, only
        // that the weirdness gets read before it is edited away.
        result.Out.ShouldContain($"warning: {CautionWarning}");
        result.Out.ShouldContain(CautionDragons);
        // Domain is outside both quarantined scopes, so this run's one warning is the caution's own.
        result.Out.ShouldNotContain("warn legacy/billing/tripwire");
        result.Out.ShouldNotContain("warn legacy/web-shell/tripwire");
    }

    [Fact]
    public async Task CheckDiffBaseJson_TouchedFileInCautionedScope_CarriesTheKindAndTheSameMessage()
    {
        CliResult result = (await Caution.Value).Json;

        // The machine channel names the posture in the warning's own kind rather than leaving a reader to
        // parse the prose for it — an additive enum value within schemaVersion 3, beside the quarantine's.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement warning = CheckJson.Rule(document, "domain/retry-budget/tripwire")
            .GetProperty("warnings")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        warning.GetProperty("kind")
            .GetString()
            .ShouldBe("cautionedScopeTouched");
        warning.GetProperty("message")
            .GetString()
            .ShouldBe(CautionWarning);
    }

    [Fact]
    public async Task CheckHookJson_CleanRunWithATripwireWarning_WritesTheHookDocumentAndNothingElse()
    {
        CliResult result = (await Untracked.Value).Hook;

        // Exit 0 exactly as without the flag — a warning never moves the verdict — and stdout is the one
        // document, whole: the parse below rejects trailing content, so a leaked report line cannot hide
        // behind it. That purity is the whole contract, because the wrapper passes stdout through verbatim
        // and Claude Code parses it.
        result.ShouldSucceed();
        string context = result.ShouldHaveHookAdditionalContext();
        context.ShouldContain("warn legacy/billing/tripwire");
        context.ShouldContain(QuarantineWarning);
        context.ShouldContain(QuarantineDragons);
    }

    [Fact]
    public async Task CheckHookJson_CleanRunWithACautionWarning_CarriesTheSameTextIntoAdditionalContext()
    {
        // The caution's third channel, and the one it was built for: a rule that only ever warns has only
        // exit 0 to travel on, and an every-edit agent hook is exactly the reader the dragons are addressed
        // to. Same two lines as the human run, inside the document the wrapper hands to Claude Code.
        CliResult result = (await Caution.Value).Hook;

        result.ShouldSucceed();
        string context = result.ShouldHaveHookAdditionalContext();
        context.ShouldContain("warn domain/retry-budget/tripwire");
        context.ShouldContain(CautionWarning);
        context.ShouldContain(CautionDragons);
    }

    [Fact]
    public async Task CheckHookJson_CleanRunWithNoWarnings_WritesNothingAtAll()
    {
        // The same repository untouched: the tripwire finds no changed file, and a hook with nothing to say
        // must say nothing rather than post an empty document. Silence is what keeps the channel bearable on
        // an every-edit hook, so it is pinned as tightly as the document itself.
        using var repo = new TempGitRepo();

        CliResult result = await CliRunner.InvokeAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD",
            "--hook-json");

        result.ShouldSucceed();
        result.Out.ShouldBeEmpty();
        result.Err.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckHookJson_RedRule_WritesTheHumanReportItAlwaysWrote()
    {
        // Hook mode shapes the exit-0 channel and nothing else: on a red rule the wrapper needs the report to
        // put on stderr and block with, so this run must be byte-identical to the same run without the flag.
        using var repo = new TempGitRepo();
        FixtureEdits.AppendMemberLine(
            repo.PathOf("MyApp.Web", "HomeController.cs"),
            "    public BillingCalculator NewCalculator() => new BillingCalculator();");

        CliResult hook = await CliRunner.InvokeAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD",
            "--hook-json");
        CliResult plain = await CliRunner.InvokeAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD");

        hook.ShouldReportViolations("FAIL legacy/billing/containment");
        hook.ShouldMatchTheOutputOf(plain);
    }

    [Fact]
    public async Task CheckHookJsonWithJson_IsRefused()
    {
        // Two documents, one stdout. Refused rather than silently ranked, because either winner would hand a
        // caller the other one's shape on a flag they did pass. Decided on the flags alone, before the
        // solution is opened, so this row takes the shared fixture path and copies no tree.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.QuarantinedSpecDll, "--json", "--hook-json");

        result.ShouldRefuseWith("--json and --hook-json both own stdout");
    }

    [Fact]
    public async Task CheckDiffBase_SolutionOpenedThroughSymlinkedRoot_StillWarns()
    {
        using var repo = new TempGitRepo();

        // A symlink whose target is the repo root, living beside the repo (outside its working tree, so
        // it is never itself a changed file). Opening the solution through it hands the workspace a
        // symlink-spelled path while `git rev-parse --show-toplevel` returns the canonical one — the exact
        // divergence that silently defeated the tripwire's prefix match before the discovery-seam
        // canonicalization. This is the product-side proof of the fix; it would have caught the original bug.
        string linkRoot = Path.Combine(Path.GetDirectoryName(repo.Root)!, "link-" + Guid.NewGuid()
            .ToString("N"));
        SymlinkSupport.CreateDirectorySymlink(linkRoot, repo.Root);
        try
        {
            // A brand-new untracked file in dragon territory (the agent-hook case).
            repo.WriteQuarantineNote();

            CliResult result = await CliRunner.InvokeAsync(
                "check", Path.Combine(linkRoot, "MyApp.sln"), "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD");

            result.ShouldSucceed("warn legacy/billing/tripwire");
            result.Out.ShouldContain(QuarantineWarning);
        }
        finally
        {
            // Delete only the symlink (non-recursive), never through it into the real repo.
            if (Directory.Exists(linkRoot)) Directory.Delete(linkRoot);
        }
    }
}
