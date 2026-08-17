using Shouldly;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The assertion every gate in this folder ends on: the findings it collected must be empty, and the
///     header says what a non-empty list means. Composes <c>"{header}:\n{one finding per line}"</c> as the
///     failure reason, so a red opens with the drift itself rather than with a count.
/// </summary>
/// <remarks>
///     <para>
///         Attributed <c>[ShouldlyMethods]</c>, which inverts the reason the suite's other assertion
///         helpers leave it off — and the inversion is the point rather than an oversight. Shouldly derives
///         the "actual" half of its message from the first stack frame after the last attributed one and
///         reads that line off disk. In those helpers the frame left showing is a property read worth
///         naming, so the attribute stays off and a red reads <c>violation.Target!.FullName should be …</c>.
///         Here the frame inside this file is <c>findings.ShouldBeEmpty(…)</c>, whose subject is this
///         method's own parameter; attributed, the reader gets the caller's line instead and the red names
///         the list the gate actually built — <c>drift</c>, <c>unregistered</c>, <c>dead</c>.
///     </para>
///     <para>
///         Takes a materialized collection rather than a sequence, because the findings are enumerated
///         twice — once to compose the message and once by Shouldly — and every caller has already built a
///         list.
///     </para>
/// </remarks>
[ShouldlyMethods]
internal static class DocGateAssertions
{
    /// <summary>
    ///     Asserts <paramref name="findings" /> is empty, failing with <paramref name="header" /> followed
    ///     by every finding on its own line.
    /// </summary>
    internal static void ShouldReportNothing(this IReadOnlyCollection<string> findings, string header)
    {
        findings.ShouldBeEmpty($"{header}:\n{string.Join("\n", findings)}");
    }
}
