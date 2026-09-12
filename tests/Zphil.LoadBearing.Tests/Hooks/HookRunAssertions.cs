using Shouldly;

namespace Zphil.LoadBearing.Tests.Hooks;

/// <summary>
///     The Shouldly surface over <see cref="HookRun" />: each assertion names what the wrapper was supposed
///     to do — exit under the contract a Claude Code hook reads, spend a check on a directory, or leave one
///     alone — and carries the whole run into its failure message.
/// </summary>
/// <remarks>
///     <para>
///         These are the most expensive rows in the suite and they had the thinnest reds. A bare
///         <c>Result.ExitCode.ShouldBe(2)</c> says <c>1 should be 2</c> and hides the stderr line that
///         explains it; a bare sentinel check says <c>False should be True</c> and not one word about
///         whether the wrapper even got as far as invoking the stub. Every assertion here passes
///         <see cref="Describe" /> as its Shouldly reason instead, so the exit code, both output channels
///         and the two directories the run was pointed at are in front of the reader.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class HookRunAssertions
{
    /// <summary>
    ///     Asserts the wrapper exited <paramref name="expected" /> — 0 proceed, 1 LoadBearing's own error or
    ///     a stand-down at the round cap, 2 block — with <paramref name="because" /> naming what that code
    ///     meant here, where a row has something to add beyond the code itself.
    /// </summary>
    internal static HookRun ShouldExitWith(this HookRun run, int expected, string? because = null)
    {
        run.Result.ExitCode.ShouldBe(expected, Explain(run, because));

        return run;
    }

    /// <summary>
    ///     Asserts the wrapper spent a check on <paramref name="directory" />. The stub records that it ran
    ///     by writing its sentinel into the directory it was invoked from, so the file is there only if the
    ///     wrapper both invoked the tool and changed to that directory first.
    /// </summary>
    internal static HookRun ShouldHaveRunTheCheckIn(this HookRun run, string directory, string because)
    {
        string sentinel = Path.Combine(directory, HookWrapperTests.Sentinel);
        File.Exists(sentinel)
            .ShouldBeTrue(Explain(run, because));

        return run;
    }

    /// <summary>
    ///     The other half: asserts no check was spent on <paramref name="directory" /> — the sentinel the
    ///     stub would have written is not there.
    /// </summary>
    internal static HookRun ShouldNotHaveRunTheCheckIn(this HookRun run, string directory, string because)
    {
        string sentinel = Path.Combine(directory, HookWrapperTests.Sentinel);
        File.Exists(sentinel)
            .ShouldBeFalse(Explain(run, because));

        return run;
    }

    /// <summary>The row's own reason with the run appended, so a red carries both rather than either.</summary>
    private static string Explain(HookRun run, string? because)
    {
        string report = Describe(run);

        return because is null ? report : $"{because}{Environment.NewLine}{report}";
    }

    /// <summary>
    ///     The exit code, both output channels, and the two directories a run is pointed at — what the
    ///     wrapper did, and where it was standing when it did it.
    /// </summary>
    private static string Describe(HookRun run)
    {
        return $"The wrapper exited {run.Result.ExitCode}, launched in {run.WorkingDirectory}, "
               + $"against the sandbox project directory {run.ProjectDirectory}.{Environment.NewLine}"
               + $"stderr:{Environment.NewLine}{run.Result.StandardError.TrimEnd()}{Environment.NewLine}"
               + $"stdout:{Environment.NewLine}{run.Result.StandardOutput.TrimEnd()}";
    }
}
