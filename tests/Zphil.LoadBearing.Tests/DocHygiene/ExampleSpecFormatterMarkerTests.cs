using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps every example spec holding the formatter off itself, from above its class
///     declaration to the end of the file. Each example walkthrough quotes rules from its spec as they
///     are written, continuation columns included, and the compiled spec is the same whichever way
///     those lines are laid out — so a reformat that moves them ships a quote that no longer matches
///     its source while every rule still passes. The marker is what holds the layout; this gate is
///     what notices a spec shipping without it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why an inventory rather than four paths.</b> A pin on the four specs that exist today
///         says nothing about a fifth. Phrased over every tracked spec under an example's <c>arch</c>
///         tree, the next one reds here on the day it lands unprotected, and the rule stays one arm
///         rather than a list somebody has to remember to extend — the same reasoning as the
///         membership inventory in <see cref="ExampleProjectMembershipTests" />.
///     </para>
///     <para>
///         <b>Why the marker must sit above the class and close nowhere.</b> The quoted blocks sit
///         throughout <c>Define</c>, so the only placement that covers all of them is one above the
///         declaration, and the only closing marker that cannot re-expose one is none. The sample
///         spec the grammar quotes carries the same unbalanced marker for the same reason.
///     </para>
///     <para>
///         <b>What this does not prove.</b> That a quote still matches its source is
///         <see cref="QuoteSyncTests" />'s job, for the fences that are verbatim. The marker covers
///         the fence that gate cannot register — a quote that inlines a local the spec declares is
///         not a substring of it — and the doc comments and the rest of the file besides.
///     </para>
/// </remarks>
public sealed class ExampleSpecFormatterMarkerTests
{
    private const string Marker = "// @formatter:off";
    private const string ClosingMarker = "@formatter:on";

    private static readonly Regex SpecDeclaration = new(@"\bclass\s+\w+\s*:\s*IArchitectureSpec\b");

    [Fact]
    public void EveryExampleSpec_HoldsTheFormatterOffAboveItsClass()
    {
        // Act
        List<string> exposed = new();
        foreach (string spec in ExampleSpecs())
        {
            string[] lines = RepoRoot.ReadLines(spec);
            int marker = Array.FindIndex(lines, static line => line.Trim() == Marker);
            int declaration = Array.FindIndex(lines, static line => SpecDeclaration.IsMatch(line));

            if (declaration < 0)
                exposed.Add($"{spec}: no IArchitectureSpec class declaration found.");
            else if (marker < 0)
                exposed.Add($"{spec}: carries no {Marker} line.");
            else if (marker > declaration)
                exposed.Add($"{spec}: {Marker} sits at line {marker + 1}, below the class declaration at line {declaration + 1}, so the rules between them are exposed.");
        }

        // Assert
        exposed.ShouldReportNothing(
            "Example spec(s) a reformat can reach: their walkthroughs quote these rules as written, "
            + "continuation columns included, and nothing in the compiled spec reds when a reformat moves them");
    }

    [Fact]
    public void EveryExampleSpec_LeavesTheMarkerUnbalanced()
    {
        // Act
        List<string> closed = ExampleSpecs()
            .Where(static spec => RepoRoot.ReadText(spec)
                .Contains(ClosingMarker, StringComparison.Ordinal))
            .ToList();

        // Assert: the quoted blocks sit throughout Define, so a closing marker anywhere re-exposes
        // whatever follows it.
        closed.ShouldReportNothing("Example spec(s) closing the marker, which re-exposes every rule below the close");
    }

    [Fact]
    public void ExampleSpecInventory_IsNotEmpty()
    {
        // Act & Assert: both arms above pass over nothing if the enumerator matches nothing, so the
        // inventory is held non-empty rather than trusted.
        ExampleSpecs()
            .ShouldNotBeEmpty("No tracked spec was found under an example's arch tree; the arms above passed over nothing.");
    }

    // Every tracked spec source under an example's arch tree, repository-relative.
    private static IReadOnlyList<string> ExampleSpecs()
    {
        return TrackedFiles.All
            .Where(static path => path.StartsWith("examples/", StringComparison.Ordinal))
            .Where(static path => path.Contains("/arch/", StringComparison.Ordinal))
            .Where(static path => path.EndsWith("ArchSpec.cs", StringComparison.Ordinal))
            .ToList();
    }
}
