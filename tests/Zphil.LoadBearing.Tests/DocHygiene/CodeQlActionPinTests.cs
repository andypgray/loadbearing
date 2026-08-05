using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps the <c>github/codeql-action</c> steps within one workflow pinned to a
///     single revision.
/// </summary>
/// <remarks>
///     <para>
///         <b>What goes wrong without it.</b> The analyze step reads the configuration the init step
///         wrote and refuses one written by a different version, so a workflow whose two steps sit on
///         different revisions fails on a configuration-version mismatch. That ends the whole
///         code-scanning run rather than one query, and nothing before it complains: both pins are
///         valid SHAs, the YAML is well formed, and the split stays silent until the next analysis.
///     </para>
///     <para>
///         <b>Why the pair splits on its own.</b> Dependabot treats each action path as a separate
///         dependency, so an ungrouped bump raises one PR for <c>init</c> and another for
///         <c>analyze</c>, each carrying half the move. <c>.github/dependabot.yml</c> groups them for
///         that reason; this is what notices when the grouping stops working or a hand edit lands one
///         side of it.
///     </para>
///     <para>
///         <b>Agreement is per workflow, not repository-wide.</b> What the action requires is that the
///         steps sharing a run share a version, and the <c>upload-sarif</c> steps in other workflows
///         upload a file rather than reading that configuration. Pinning those to the same revision
///         too would fail a standalone bump that is genuinely fine.
///     </para>
/// </remarks>
public sealed class CodeQlActionPinTests
{
    private const string WorkflowDirectory = ".github/workflows/";

    // A pinned use of the action: the path under it, the revision, and the version comment that
    // trails the pin. Only the revision is asserted on — it is what actually runs — but the comment
    // is what makes a failure readable. The comment is matched without crossing a line ending, so a
    // pin that carries none reports its revision rather than borrowing the next line's comment.
    private static readonly Regex PinnedUse = new(
        @"github/codeql-action(?<path>/[\w.-]+)?@(?<sha>[0-9a-f]{40})([^\S\r\n]*#[^\S\r\n]*(?<version>\S+))?");

    [Fact]
    public void CodeQlActionPins_WithinAWorkflow_NameOneRevision()
    {
        // Arrange
        var byWorkflow = ReadPins().GroupBy(static pin => pin.Workflow);

        // Act
        var split = byWorkflow
            .Where(static workflow => workflow.Select(static pin => pin.Sha).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        split.ShouldBeEmpty(
            "Workflow(s) pin github/codeql-action steps to more than one revision, which fails the "
            + $"analysis outright:\n{string.Join("\n", split)}");
    }

    [Fact]
    public void CodeQlActionPins_AreReadable()
    {
        // Act
        var pins = ReadPins();

        // Assert: the arm above passes over a workflow it cannot read, so the pins are held non-empty
        // rather than trusted. A rewrite into a form this scan cannot follow fails here.
        pins.ShouldNotBeEmpty($"No pinned github/codeql-action steps were found under {WorkflowDirectory}.");
    }

    private static string Describe(IGrouping<string, Pin> workflow)
    {
        var steps = workflow
            .Select(static pin => $"    {pin.Path} -> {pin.Version}")
            .Order(StringComparer.Ordinal);

        return $"  {workflow.Key}:\n{string.Join("\n", steps)}";
    }

    // Every pinned use of the action across the tracked workflows, tagged with the file it sits in.
    private static IReadOnlyList<Pin> ReadPins()
    {
        return TrackedFiles.All
            .Where(static path => path.StartsWith(WorkflowDirectory, StringComparison.Ordinal))
            .SelectMany(PinsIn)
            .ToArray();
    }

    private static IEnumerable<Pin> PinsIn(string workflow)
    {
        string text = File.ReadAllText(TrackedFiles.Absolute(workflow));

        return PinnedUse.Matches(text).Select(match => ToPin(workflow, match));
    }

    private static Pin ToPin(string workflow, Match match)
    {
        string path = match.Groups["path"].Success ? match.Groups["path"].Value : "";
        string version = match.Groups["version"].Success ? match.Groups["version"].Value : match.Groups["sha"].Value;

        return new Pin(workflow, $"github/codeql-action{path}", match.Groups["sha"].Value, version);
    }

    private sealed record Pin(string Workflow, string Path, string Sha, string Version);
}
