using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The hook document's wire shape: the one <c>hookSpecificOutput</c> object a Claude Code
///     <c>PostToolUse</c> hook is parsed for, carrying the check report as <c>additionalContext</c>.
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
    [Fact]
    public void Document_CarriesThePostToolUseEventAndTheReport()
    {
        const string report = "warn legacy/billing/tripwire\n  warning: Changed file 'Billing/Calc.cs' is inside …";

        string document = HookReportRenderer.Document(report);

        JsonElement output = Parsed(document);
        output.GetProperty("hookEventName")
            .GetString()
            // PostToolUse is the one event whose additional context reaches the agent, so the name is not an
            // implementation detail of the caller: a hook wired to another event gets nothing from this.
            .ShouldBe("PostToolUse");
        output.GetProperty("additionalContext")
            .GetString()
            .ShouldBe(report);
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

        string document = HookReportRenderer.Document(report);

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
        string document = HookReportRenderer.Document(string.Empty);

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
