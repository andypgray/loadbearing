using System.Text.Json;
using Shouldly;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The Shouldly surface over <see cref="CliResult" />: each assertion names the exit contract it is
///     checking — 0 succeeded, 1 found violations, 2 refused the run — rather than the integer that
///     encodes it, and carries both output channels into its failure message.
/// </summary>
/// <remarks>
///     <para>
///         A bare <c>Exit.ShouldBe(0)</c> reds with <c>2 should be 0</c> and discards the stderr line that
///         says why. Every assertion here passes <see cref="Describe" /> as its Shouldly reason instead, so
///         the refusal the CLI actually printed is in front of the reader.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class CliResultAssertions
{
    /// <summary>Asserts the run succeeded, and that stdout contains every one of <paramref name="outFragments" />.</summary>
    internal static CliResult ShouldSucceed(this CliResult result, params string[] outFragments)
    {
        return ShouldExitWith(result, 0, result.Out, outFragments);
    }

    /// <summary>
    ///     Asserts the run completed and found violations — exit 1, the enforcement red, distinct from the
    ///     exit 2 that means the run never got as far as checking.
    /// </summary>
    internal static CliResult ShouldReportViolations(this CliResult result, params string[] outFragments)
    {
        return ShouldExitWith(result, 1, result.Out, outFragments);
    }

    /// <summary>
    ///     Asserts the CLI refused the run — exit 2, a usage or tool error — and said so on stderr, which
    ///     carries every one of <paramref name="errFragments" />.
    /// </summary>
    internal static CliResult ShouldRefuseWith(this CliResult result, params string[] errFragments)
    {
        return ShouldExitWith(result, 2, result.Err, errFragments);
    }

    /// <summary>
    ///     Asserts this run came out byte for byte as <paramref name="expected" /> did — same exit code, same
    ///     stdout, same stderr.
    /// </summary>
    /// <remarks>
    ///     The one verb here that names no exit contract. The others each pin a constant — 0, 1, 2 — and read
    ///     fragments out of one channel; this one pins whole channels against another run computed at
    ///     runtime, and takes whatever exit code that run reached. That is the shape of the guarantee it
    ///     serves: a replayed run is indistinguishable from the cold one, whatever the cold one did.
    /// </remarks>
    internal static CliResult ShouldMatchTheOutputOf(this CliResult actual, CliResult expected)
    {
        string report = $"This run:{Environment.NewLine}{Describe(actual)}{Environment.NewLine}"
                        + $"The run it should match:{Environment.NewLine}{Describe(expected)}";

        actual.ShouldSatisfyAllConditions(
            () => actual.Exit.ShouldBe(expected.Exit, report),
            () => actual.Out.ShouldBe(expected.Out, report),
            () => actual.Err.ShouldBe(expected.Err, report));

        return actual;
    }

    /// <summary>
    ///     Asserts stdout is one parseable JSON document and hands it back. The caller owns the
    ///     <see cref="JsonDocument" />.
    /// </summary>
    /// <remarks>
    ///     Parsing rather than sniffing the first character: a truncated report still starts with
    ///     <c>{</c>, and a report cut in half is exactly the failure a stdout-purity guard exists to catch.
    /// </remarks>
    internal static JsonDocument ShouldHaveJsonStdout(this CliResult result)
    {
        try
        {
            return JsonDocument.Parse(result.Out);
        }
        catch (JsonException exception)
        {
            throw new ShouldAssertException(
                $"stdout should have been one JSON document but did not parse: {exception.Message}"
                + Environment.NewLine + Describe(result));
        }
    }

    /// <summary>
    ///     Asserts stdout is the one hook document and hands back its <c>additionalContext</c> — the text the
    ///     wrapper passes through to Claude Code.
    /// </summary>
    internal static string ShouldHaveHookAdditionalContext(this CliResult result)
    {
        using JsonDocument document = result.ShouldHaveJsonStdout();
        return document.RootElement.GetProperty("hookSpecificOutput")
            .GetProperty("additionalContext")
            .GetString()!;
    }

    private static CliResult ShouldExitWith(CliResult result, int exit, string channel, string[] fragments)
    {
        string report = Describe(result);
        result.Exit.ShouldBe(exit, report);

        Action[] checks = fragments
            .Select<string, Action>(fragment => () => channel.ShouldContain(fragment, customMessage: report))
            .ToArray();
        result.ShouldSatisfyAllConditions(checks);

        return result;
    }

    /// <summary>The exit code and both output channels — what the run actually did.</summary>
    private static string Describe(CliResult result)
    {
        return $"The CLI exited {result.Exit}.{Environment.NewLine}"
               + $"stderr:{Environment.NewLine}{result.Err.TrimEnd()}{Environment.NewLine}"
               + $"stdout:{Environment.NewLine}{result.Out.TrimEnd()}";
    }
}
