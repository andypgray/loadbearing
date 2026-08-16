using Shouldly;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Assertions over <see cref="ProjectLoadReport" /> — the load's two answers, failed and unchecked,
///     asserted together so neither can be pinned while the other drifts.
/// </summary>
/// <remarks>
///     Every message carries the whole report, both lists, because the defect this type exists to prevent is
///     precisely reading one list and forgetting the other: a message quoting only the list asserted on would
///     hide the half that moved. Deliberately not attributed <c>[ShouldlyMethods]</c> — leaving it off is what
///     makes an inner failure name the list under test rather than this file.
/// </remarks>
internal static class ProjectLoadReportAssertions
{
    /// <summary>Asserts the load blamed nothing and left nothing out — the whole solution was checked.</summary>
    internal static void ShouldHaveLoadedEverything(this ProjectLoadReport report)
    {
        report.Failed.ShouldBeEmpty(Describe(report));
        report.Unchecked.ShouldBeEmpty(Describe(report));
    }

    /// <summary>
    ///     Asserts exactly <paramref name="expected" /> failed to load and nothing was narrowed away — the
    ///     shape of an unfiltered solution that loaded partially.
    /// </summary>
    internal static void ShouldHaveFailed(this ProjectLoadReport report, params string[] expected)
    {
        report.Failed.ShouldBe(expected, Describe(report));
        report.Unchecked.ShouldBeEmpty(Describe(report));
    }

    /// <summary>
    ///     Asserts exactly <paramref name="expected" /> were left unchecked and nothing failed — the shape of
    ///     a filtered run that loaded cleanly over a narrowed universe.
    /// </summary>
    internal static void ShouldHaveLeftUnchecked(this ProjectLoadReport report, params string[] expected)
    {
        report.Unchecked.ShouldBe(expected, Describe(report));
        report.Failed.ShouldBeEmpty(Describe(report));
    }

    private static string Describe(ProjectLoadReport report)
    {
        return $"failed: [{string.Join(", ", report.Failed)}]\n"
               + $"unchecked: [{string.Join(", ", report.Unchecked)}]";
    }
}
