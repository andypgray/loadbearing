using Shouldly;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     The Shouldly surface over total-fact equality of two <see cref="CodebaseModel" />s: the claim that
///     <em>this model carries the same facts as that one</em>, rather than the two
///     <see cref="ModelDump" /> renders that establish it.
/// </summary>
/// <remarks>
///     <para>
///         A bare <c>ModelDump.Render(a).ShouldBe(ModelDump.Render(b))</c> reds with two multi-kilobyte
///         strings and names no differing fact, leaving the reader to diff them by eye. This finds the first
///         differing line instead, hands Shouldly that one line as the actual/expected pair, and puts the
///         <c>== SECTION ==</c> it falls under plus a few lines of context from each dump in the message —
///         which is the first thing such a red raises.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the same reason
///         <c>Checking/RuleResultAssertions</c> is not: Shouldly derives the "actual" half of its message
///         from the first stack frame after the last attributed one and reads that line off disk, so leaving
///         the attribute off makes the failure name the caller's own model rather than a local in here.
///     </para>
/// </remarks>
internal static class CodebaseModelAssertions
{
    private const int ContextLines = 3;

    /// <summary>
    ///     Asserts <paramref name="actual" /> carries every fact <paramref name="expected" /> does and no
    ///     others, via the total dump — a fact <see cref="ModelDump" /> does not render is pinned by nothing
    ///     here, which is the contract that file states.
    /// </summary>
    internal static CodebaseModel ShouldModelTheSameAs(this CodebaseModel actual, CodebaseModel expected)
    {
        string actualDump = ModelDump.Render(actual);
        string expectedDump = ModelDump.Render(expected);
        if (string.Equals(actualDump, expectedDump, StringComparison.Ordinal)) return actual;

        string[] actualLines = Lines(actualDump);
        string[] expectedLines = Lines(expectedDump);
        int index = FirstDifferingIndex(actualLines, expectedLines);

        // Defensive: two dumps that differ only in line endings would leave nothing for the line comparison
        // below to red on, and a silently-green equality assertion is worse than an unreadable one.
        if (index >= actualLines.Length && index >= expectedLines.Length)
        {
            actualDump.ShouldBe(expectedDump);
            return actual;
        }

        LineAt(actualLines, index)
            .ShouldBe(LineAt(expectedLines, index), Describe(actualLines, expectedLines, index));

        return actual;
    }

    private static string Describe(string[] actualLines, string[] expectedLines, int index)
    {
        List<string> report =
        [
            $"The two model dumps first differ at line {index + 1}, under {SectionAt(actualLines, expectedLines, index)}.",
            "expected:",
            .. Window(expectedLines, index),
            "actual:",
            .. Window(actualLines, index)
        ];

        return string.Join(Environment.NewLine, report);
    }

    // The nearest section heading at or above the differing line, read off whichever dump still has one
    // there — the one that ran out of lines has no heading to read.
    private static string SectionAt(string[] actualLines, string[] expectedLines, int index)
    {
        string[] lines = index < expectedLines.Length ? expectedLines : actualLines;
        for (int number = Math.Min(index, lines.Length - 1); number >= 0; number--)
            if (lines[number]
                .StartsWith("== ", StringComparison.Ordinal))
                return lines[number];

        return "no section heading";
    }

    private static IEnumerable<string> Window(string[] lines, int index)
    {
        int from = Math.Max(0, index - ContextLines);
        int to = Math.Min(lines.Length - 1, index + ContextLines);

        return Enumerable.Range(from, Math.Max(0, to - from + 1))
            .Select(number => $"  {number + 1,6}{(number == index ? " >" : "  ")} {lines[number]}");
    }

    private static int FirstDifferingIndex(string[] actualLines, string[] expectedLines)
    {
        int shared = Math.Min(actualLines.Length, expectedLines.Length);
        for (var index = 0; index < shared; index++)
            if (!string.Equals(actualLines[index], expectedLines[index], StringComparison.Ordinal))
                return index;

        return shared;
    }

    private static string LineAt(string[] lines, int index)
    {
        return index < lines.Length ? lines[index] : "(the dump ends here)";
    }

    // Split for display only — the equality above is over the raw dumps, so folding line endings here
    // cannot hide a difference.
    private static string[] Lines(string dump)
    {
        return dump.NormalizedLines()
            .Split('\n');
    }
}
