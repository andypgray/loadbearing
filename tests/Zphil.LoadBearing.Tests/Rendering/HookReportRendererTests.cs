using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The hook document's wire shape: the one <c>hookSpecificOutput</c> object a Claude Code hook is
///     parsed for, carrying the check report as <c>additionalContext</c> for the event that fired.
/// </summary>
/// <remarks>
///     The escaping rows are the reason this renderer exists at all. A check report is multi-line and
///     carries backticks, em-dashes and Windows paths, and the alternative to owning the escaping here was
///     two hand-rolled escapers — one in POSIX <c>sh</c>, which has no JSON, and one in PowerShell — kept
///     in step by nothing. Each row below therefore asserts on the <em>parsed</em> document rather than on
///     the text, because a round trip is the only claim worth making about escaping.
/// </remarks>
public sealed class HookReportRendererTests
{
    [Theory]
    [InlineData("PostToolUse")]
    [InlineData("Stop")]
    [InlineData("SubagentStop")]
    public void Document_CarriesTheEventItWasRenderedForAndTheReport(string hookEvent)
    {
        const string report = "warn legacy/billing/tripwire\n  warning: Changed file 'Billing/Calc.cs' is inside …";

        string document = HookReportRenderer.Document(report, hookEvent);

        JsonElement output = Parsed(document);
        output.GetProperty("hookEventName")
            .GetString()
            // Claude Code reads additionalContext only from a document naming the event it fired, so the name
            // is not decoration: a Stop hook handed the per-edit envelope reaches the agent with nothing. The
            // three rows are the whole set of events whose context arrives at all.
            .ShouldBe(hookEvent);
        output.GetProperty("additionalContext")
            .GetString()
            .ShouldBe(report);
    }

    [Fact]
    public void Events_AreTheThreeTheRunnerWillRenderFor()
    {
        // The renderer owns the set the runner refuses against, so the two cannot drift into a CLI that
        // accepts an event this document may not name.
        HookReportRenderer.Events.ShouldBe(["PostToolUse", "Stop", "SubagentStop"]);
        HookReportRenderer.DefaultEvent.ShouldBe("PostToolUse");
    }

    [Fact]
    public void Document_MultiLineReportWithQuotesAndBackslashes_RoundTripsUnchanged()
    {
        // Every character a report can carry that a hand-rolled escaper gets wrong: the newlines that make it
        // multi-line, the single quotes the tripwire message wraps a path in, the double quotes and
        // backslashes a Windows path brings, a tab, and the backticks and em-dash a rendered law sentence
        // carries. What comes back has to be the same string, not a legible approximation of it.
        const string report = """
                              warn legacy/billing/tripwire
                                warning: Changed file 'src\Legacy\"Billing".cs' is inside quarantined scope
                              	— `BillingCalculator` must not be tidied. Dragons: loadbearing explain.
                              """;

        string document = HookReportRenderer.Document(report, HookReportRenderer.DefaultEvent);

        Parsed(document)
            .GetProperty("additionalContext")
            .GetString()
            .ShouldBe(report);
    }

    [Fact]
    public void Document_EmptyReport_IsStillOneWellFormedObject()
    {
        // Nothing calls it this way — the runner writes no document at all when there is nothing to say — but
        // the renderer must not be the thing that decides that, and a document that parses is the floor.
        string document = HookReportRenderer.Document(string.Empty, HookReportRenderer.DefaultEvent);

        Parsed(document)
            .GetProperty("additionalContext")
            .GetString()
            .ShouldBeEmpty();
    }

    /// <summary>
    ///     The document's single <c>hookSpecificOutput</c> object, parsed. Parsing is itself the assertion
    ///     that the text is one whole JSON document: a trailing anything would throw here.
    /// </summary>
    private static JsonElement Parsed(string document)
    {
        using JsonDocument parsed = JsonDocument.Parse(document);
        return parsed.RootElement.GetProperty("hookSpecificOutput")
            .Clone();
    }
}
