using System.Collections.Concurrent;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The link gate over cited pages. A rule that carries a <c>Citation</c> renders its page into the
///     always-on managed block, where an agent reads it as the provenance of the law the bullet states —
///     so a page that has been moved or retired does not merely dangle, it hands every agent working in
///     this repository a dead reference as the authority for a rule it is being asked to follow, and
///     nothing else in the suite would say so. This gate holds every rendered citation to a page that
///     still answers.
/// </summary>
/// <remarks>
///     <para>
///         <b>The render is the authority, not the spec.</b> The four example specs are not reachable in
///         this process, and reading a spec's source for its citations would go back to matching text
///         against a shape the model now carries. What every spec has in common is the committed block it
///         renders, held equal to its model by the dogfood tests here and by the examples being
///         re-rendered and diffed. So the corpus is the rendered bullet, scanned out of committed bytes.
///     </para>
///     <para>
///         <b>Opt-in, because it is the one gate that needs the network.</b> Every other gate in this
///         folder is a pure function of the checkout; this one cannot be. Unset, it skips before any I/O,
///         so a clone with no network and every CI leg that has not asked for it stay green and honest
///         rather than red for a reason that is not about this repository.
///     </para>
///     <para>
///         <b>Measured 2026-09-08:</b> the pages this corpus cites answer <c>HEAD</c> honestly after one
///         redirect — a live path 200 and a dead path 404 — so a <c>HEAD</c> is what the probe sends, and
///         a server that refuses the verb is retried once with a <c>GET</c>.
///     </para>
/// </remarks>
public sealed class CitationLinkTests
{
    /// <summary>Set to a non-blank value to run the network probe. Unset (the default) skips it.</summary>
    internal const string LinkCheckVariable = "LOADBEARING_LINK_CHECK";

    private const string AgentsFileName = "AGENTS.md";

    private const string UserAgent = "loadbearing-link-check";

    /// <summary>How many pages are asked at once — courtesy to the host, not a throughput target.</summary>
    private const int MaxInFlight = 4;

    /// <summary>The committed context files whose managed block renders at least one citation.</summary>
    private static readonly string[] Registry =
    [
        "AGENTS.md",
        "src/Zphil.LoadBearing.Cli/AGENTS.md",
        "examples/Meridian/AGENTS.md",
        "examples/Meridian.Quoting/AGENTS.md",
        "examples/Meridian.Interchange/AGENTS.md"
    ];

    private static readonly DocGate<RenderedCitation> Gate = new(
        Registry,
        ExtractDoc,
        quotes: "citations",
        docs: "context files",
        scanner: "autolink scanner");

    private static bool LinkCheckRequested =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LinkCheckVariable));

    [Fact]
    public void EveryRegisteredContextFile_YieldsACitation()
    {
        // Act & Assert: guard against the scanner silently matching nothing if the rendered bullet's
        // citation clause changes shape.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    [Fact]
    public void EveryContextFileRenderingACitation_IsRegistered()
    {
        // Act & Assert: the registry above is hand-written, so the failure it cannot see is a context
        // file that starts rendering a citation and was never added to it — a page nothing probes.
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "citation(s)",
            swept: "citations",
            authority: "a live page");
    }

    [Fact]
    public async Task EveryCitedPage_IsLive()
    {
        Assert.SkipUnless(
            LinkCheckRequested,
            $"Cited-page link checking is opt-in and needs the network: set {LinkCheckVariable} to a non-blank value to run it.");

        // Arrange
        List<RenderedCitation> cited = Registry
            .SelectMany(Gate.Scan)
            .ToList();
        List<IGrouping<string, RenderedCitation>> pages = cited
            .GroupBy(citation => citation.Url, StringComparer.Ordinal)
            .OrderBy(page => page.Key, StringComparer.Ordinal)
            .ToList();

        // Act: one request per distinct page, however many bullets render it.
        using HttpClient client = new();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        CitationProbe probe = new(client);
        ConcurrentDictionary<string, CitationProbe.PageStatus> statuses = new(StringComparer.Ordinal);
        ParallelOptions options = new() { MaxDegreeOfParallelism = MaxInFlight, CancellationToken = Ct };

        await Parallel.ForEachAsync(
            pages,
            options,
            async (page, token) => statuses[page.Key] = await probe.ProbeAsync(page.Key, token));

        // Assert
        List<string> dead = SitesIn(pages, statuses, CitationProbe.PageBucket.Dead);
        dead.ShouldReportNothing(
            "Cited pages answered 404 or 410; fix the Citation in the spec that renders them and re-render");

        List<string> unverifiable = SitesIn(pages, statuses, CitationProbe.PageBucket.Unverifiable);
        if (unverifiable.Count == 0) return;

        string unreached = string.Join("\n", unverifiable);
        Assert.Skip(
            $"These cited pages could not be reached on this run, so it cannot say whether they are live:\n{unreached}");
    }

    // Every site behind a page that fell in `bucket`, one line each: a failure names the file and line an
    // agent reads the citation at, and the rule whose Citation to fix in the spec behind it.
    private static List<string> SitesIn(
        IReadOnlyList<IGrouping<string, RenderedCitation>> pages,
        IReadOnlyDictionary<string, CitationProbe.PageStatus> statuses,
        CitationProbe.PageBucket bucket)
    {
        List<string> sites = new();

        foreach (IGrouping<string, RenderedCitation> page in pages)
        {
            CitationProbe.PageStatus status = statuses[page.Key];
            if (status.Bucket != bucket) continue;

            foreach (RenderedCitation citation in page)
                sites.Add($"{citation.Doc}:{citation.DocLine} {citation.RuleId} {citation.Url} → {status.Detail}");
        }

        return sites;
    }

    // The file-name filter lives here rather than in the scanner: a README quotes a whole block, markers
    // included, inside a fence, and two marker pairs are a malformed block rather than a doc with no
    // citations. Only a generated context file is vouched for, and a README's copy of a bullet is
    // commentary the quote gates already hold to the block it came from.
    private static IReadOnlyList<RenderedCitation> ExtractDoc(string doc)
    {
        if (Path.GetFileName(doc) != AgentsFileName) return [];

        string text = RepoRoot.ReadText(doc);

        return RenderedCitations.Extract(doc, text);
    }
}
