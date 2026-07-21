using Shouldly;
using Xunit;
using Xunit.Sdk;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;
using Zphil.LoadBearing.Xunit;

namespace Zphil.LoadBearing.Tests.Xunit;

/// <summary>
///     The adapter's mechanics, isolated from the dogfood run: discovery uses rule IDs as display names, a
///     failed rule's <c>Assert.Fail</c> body is byte-identical (after normalization) to the CLI human
///     block, and a Freeze tripwire without a diff is reported as skipped with the pinned reason. The two
///     inline specs replicate fixture rules verbatim because tests cannot reference the fixture spec
///     assemblies by design (<c>ReferenceOutputAssembly=false</c>); their driver classes are non-public, so
///     the test runner never discovers them — they are invoked directly.
/// </summary>
[Collection("Serial")]
public sealed class AdapterTests
{
    [Fact]
    public void RuleRows_UsesRuleIdsAsDisplayNames()
    {
        // The dogfood spec exercises all three postures, so discovery must surface each post-desugar rule
        // ID as its own display name — including the Freeze scope's containment + tripwire children.
        IReadOnlyList<ITheoryDataRow> rows = ArchRuleTests<LoadBearingArchSpec>.RuleRows().ToList();

        rows.Select(row => row.TestDisplayName).ShouldBe(
        [
            "layering/core-no-roslyn",
            "cli/no-stdout",
            "di/no-captive-dependencies",
            "mcp/env-through-seam",
            "roslyn/msbuild-bootstrap/containment",
            "roslyn/msbuild-bootstrap/tripwire"
        ], true);
    }

    [Fact]
    public void RuleRows_SpecBuildFailure_CollapsesToSingleSentinelRow()
    {
        // A spec that fails validation at build time (a rule with no .Because) cannot enumerate its rules, so
        // discovery collapses to one sentinel row that lands red at run time — where the pipeline rebuild
        // rethrows the real SpecValidationException — rather than vanishing as a silent discovery diagnostic.
        IReadOnlyList<ITheoryDataRow> rows = ArchRuleTests<SpecBuildFailsSpec>.RuleRows().ToList();

        ITheoryDataRow row = rows.ShouldHaveSingleItem();
        row.TestDisplayName.ShouldBe($"{nameof(SpecBuildFailsSpec)}: spec build failed");
    }

    [Fact]
    public async Task FailureText_MatchesCliHumanBlock()
    {
        // The CLI human block for the same rule + same solution is the oracle.
        CliResult cli = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        string expectedBlock = ExtractBlock(cli.Out, "layering/domain-independent");

        Exception? exception = await Record.ExceptionAsync(() => new InlineViolatedArchTests().Rule_Holds("layering/domain-independent"));

        var failure = exception.ShouldBeOfType<FailException>();
        Normalize(failure.Message).ShouldBe(expectedBlock);
    }

    [Fact]
    public async Task Tripwire_WithoutDiff_Skips()
    {
        Exception? exception = await Record.ExceptionAsync(() => new InlineFrozenArchTests().Rule_Holds("legacy/billing/tripwire"));

        var skip = exception.ShouldBeOfType<SkipException>();
        // SkipException.ForSkip prefixes the reason with an internal dynamic-skip marker; the reason is the suffix.
        skip.Message.ShouldEndWith(ArchChecker.TripwireSkipReason);
    }

    [Fact]
    public void FindSolutionUp_FromTestOutput_ResolvesRepoSolution()
    {
        HelperAccessor.Call("Zphil.LoadBearing.slnx").ShouldBe(RepoRoot.Solution);
    }

    [Fact]
    public void FindSolutionUp_MissingFile_ThrowsNamingFileAndOrigin()
    {
        var exception = Should.Throw<FileNotFoundException>(() => HelperAccessor.Call("no-such-file-f2.slnx"));
        exception.Message.ShouldBe($"Could not locate 'no-such-file-f2.slnx' walking up from '{AppContext.BaseDirectory}'.");
    }

    // The "FAIL <ruleId> …" block from the CLI human output: the header line plus its indented lines.
    private static string ExtractBlock(string humanOutput, string ruleId)
    {
        string[] lines = humanOutput.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, line => line.StartsWith($"FAIL {ruleId}", StringComparison.Ordinal));
        if (start < 0) throw new InvalidOperationException($"No FAIL block for '{ruleId}' in:\n{humanOutput}");

        var block = new List<string> { lines[start] };
        for (int i = start + 1; i < lines.Length && lines[i].StartsWith("  ", StringComparison.Ordinal); i++)
            block.Add(lines[i]);

        return string.Join("\n", block);
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }

    // A verbatim inline copy of the fixture's layering/domain-independent rule, checked against the real
    // MyApp solution with no project excluded (as the CLI does for a by-path DLL spec), so its one rule
    // produces the identical violation block.
    private sealed class MyAppViolatedInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
            Layer web = arch.Layer("Web", "MyApp.Web.*");

            arch.Rule("layering/domain-independent")
                .Enforce(domain.MustNotReference(web))
                .Because("Domain is UI-agnostic; transaction boundaries live in services.")
                .Fix("Define an abstraction in Domain and implement it in Web.");
        }
    }

    private sealed class InlineViolatedArchTests : ArchRuleTests<MyAppViolatedInlineSpec>
    {
        protected override string SolutionPath => CliRunner.MyAppSolution;
        protected override string? ExcludeProjectName => null;
    }

    // Reaches the protected FindSolutionUp helper without discovering a real test case: SolutionPath is
    // never read (the runner never sees this private class), so it throws if the pipeline ever touched it.
    private sealed class HelperAccessor : ArchRuleTests<MyAppViolatedInlineSpec>
    {
        protected override string SolutionPath => throw new NotSupportedException();

        public static string Call(string fileName)
        {
            return FindSolutionUp(fileName);
        }
    }

    // A frozen scope over MyApp.Legacy.Billing — its desugared tripwire skips without a --diff-base.
    private sealed class MyAppFrozenInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing")
                .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.")
                .Because("Replacement scheduled; not worth stabilizing.");
        }
    }

    private sealed class InlineFrozenArchTests : ArchRuleTests<MyAppFrozenInlineSpec>
    {
        protected override string SolutionPath => CliRunner.MyAppSolution;
        protected override string? ExcludeProjectName => null;
    }

    // A spec that fails build-time validation: the rule carries a posture but no required .Because(...), so
    // ArchModelBuilder.Build throws SpecValidationException and RuleRows() takes its spec-build-failure branch.
    private sealed class SpecBuildFailsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I"));
        }
    }
}