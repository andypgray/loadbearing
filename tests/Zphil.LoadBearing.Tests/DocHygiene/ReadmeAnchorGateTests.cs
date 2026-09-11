using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The walkthrough gate over quoted source anchors. The example walkthroughs and the root README
///     quote <c>check</c> violation output inside fenced code blocks, and each quoted line carries a
///     <c>file:line</c> anchor into a committed source: an example's own tree for the walkthroughs, this
///     repository's tree for the root README's dogfood spine. This gate lifts every anchor and holds it
///     to its source: a committed-source anchor must match the line it names, by content or by a landmark
///     pin that keys a documented walkthrough edit to the committed line it lands on; a demonstration
///     anchor — check output over a hypothetical edit that adds new code — must stay unmatched, so the
///     day that code lands the anchor graduates to a real one. A drifted anchor fails the suite instead
///     of publishing stale output.
/// </summary>
public sealed class ReadmeAnchorGateTests
{
    private const string BookingsController = "src/Meridian.Web/Controllers/BookingsController.cs";
    private const string CustomsController = "src/Meridian.Web/Controllers/CustomsController.cs";
    private const string ExpireQuotesHandler = "src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs";
    private const string InvoicePreview = "src/Meridian.Operations/Dispatch/InvoicePreview.cs";
    private const string RequestQuoteHandler = "src/Meridian.Quoting.Application/Handlers/RequestQuoteHandler.cs";
    private const string ProgressPrinter = "src/Zphil.LoadBearing.Cli/Rendering/ProgressPrinter.cs";

    private const string RootReadme = "README.md";
    private const string MeridianReadme = "examples/Meridian/README.md";
    private const string Storyboard = "examples/Meridian/hooks/storyboard.md";
    private const string QuotingReadme = "examples/Meridian.Quoting/README.md";
    private const string OperationsReadme = "examples/Meridian.Operations/README.md";

    private const string MeridianRoot = "examples/Meridian";
    private const string QuotingRoot = "examples/Meridian.Quoting";
    private const string OperationsRoot = "examples/Meridian.Operations";
    private const string InterchangeRoot = "examples/Meridian.Interchange";

    /// <summary>
    ///     This repository's own root, the anchor base for docs that quote its own sources rather than an
    ///     example's. Empty because such anchors are already repository-relative.
    /// </summary>
    private const string SelfRoot = "";

    /// <summary>The anchor-bearing docs, each paired with the root its anchors resolve against.</summary>
    private static readonly (string Doc, string ExampleRoot)[] AnchorDocs =
    [
        (MeridianReadme, MeridianRoot),
        ("examples/Meridian/ADOPTING.md", MeridianRoot),
        (Storyboard, MeridianRoot),
        (QuotingReadme, QuotingRoot),
        (OperationsReadme, OperationsRoot),
        ("examples/Meridian.Interchange/README.md", InterchangeRoot),
        // The root README's dogfood spine quotes a self-check over this solution, so its anchors name
        // this repository's own paths.
        (RootReadme, SelfRoot)
    ];

    private static readonly DocGate<SourceAnchor> Gate = new(
        AnchorDocs.Select(static entry => entry.Doc)
            .ToArray(),
        ExtractDoc,
        quotes: "anchors",
        docs: "anchor docs");

    /// <summary>
    ///     Landmark pins for anchors the content bucket cannot verify: the reported line holds no copy of
    ///     the derived token, so the committed line the anchor is keyed to is pinned directly. Each entry
    ///     names the reported line and pins the committed line and a snippet that line must still carry.
    /// </summary>
    private static readonly Dictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark> Landmarks = new()
    {
        // http/reuse-httpclient inserts `using var probe = new HttpClient();` as the first statement of
        // CarrierClient.SendAsync; committed line 15 is the statement that insertion lands in front of.
        [new SourceAnchors.AnchorKey(InterchangeRoot, "src/Meridian.Interchange/Partners/CarrierClient.cs", 15)] = new SourceAnchors.Landmark(15, "StringContent content"),

        // di/hosted-services-scope-their-work swaps this constructor parameter to IOptionsSnapshot; the
        // primary-constructor declaration is committed line 15.
        [new SourceAnchors.AnchorKey(InterchangeRoot, "src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs", 15)] = new SourceAnchors.Landmark(15, "IOptions<InterchangeOptions> options"),

        // di/no-captive-dependencies swaps the first constructor parameter to IOutboxStore; the
        // primary-constructor declaration is committed line 15.
        [new SourceAnchors.AnchorKey(InterchangeRoot, "src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs", 16)] = new SourceAnchors.Landmark(15, "OutboxDispatcher(ScopedDispatchRunner runner"),

        // exceptions/no-general-catch wraps the per-message loop body in a catch-all; the inserted catch
        // is keyed to the committed loop at line 24.
        [new SourceAnchors.AnchorKey(InterchangeRoot, "src/Meridian.Interchange/Processing/OutboxProcessor.cs", 32)] = new SourceAnchors.Landmark(24, "foreach (OutboxMessage message in pending)"),

        // persistence/no-mapping-attributes adds a using and a [Table] attribute above the record; the
        // committed record declaration is line 7.
        [new SourceAnchors.AnchorKey(InterchangeRoot, "src/Meridian.Interchange/Outbox/OutboxMessage.cs", 10)] = new SourceAnchors.Landmark(7, "public sealed record OutboxMessage"),

        // contracts/no-entity-exposure changes the contract to take OutboxMessage, adding a using that
        // shifts this signature down; the committed signature is line 13.
        [new SourceAnchors.AnchorKey(InterchangeRoot, "src/Meridian.Interchange/Partners/IPartnerClient.cs", 15)] = new SourceAnchors.Landmark(13, "Task SendAsync(PartnerEnvelope envelope"),

        // The derive-time evidence pass reports these SqlClient references at member-access lines, where
        // the type name is on the local's declaration rather than the reported line; the committed lines
        // are pinned directly.
        [new SourceAnchors.AnchorKey(MeridianRoot, CustomsController, 28)] = new SourceAnchors.Landmark(28, "command.Parameters.AddWithValue"),
        [new SourceAnchors.AnchorKey(MeridianRoot, CustomsController, 29)] = new SourceAnchors.Landmark(29, "connection.Open()"),
        [new SourceAnchors.AnchorKey(MeridianRoot, CustomsController, 32)] = new SourceAnchors.Landmark(32, "command.ExecuteReader()"),
        [new SourceAnchors.AnchorKey(MeridianRoot, CustomsController, 53)] = new SourceAnchors.Landmark(53, "validator.IsValid(number)"),

        // handlers/transactional deletes the [Transactional] attribute on committed line 13, shifting the
        // class declaration up to the reported line; the committed declaration is line 14.
        [new SourceAnchors.AnchorKey(QuotingRoot, RequestQuoteHandler, 13)] = new SourceAnchors.Landmark(14, "public sealed class RequestQuoteHandler")
    };

    /// <summary>
    ///     Demonstration anchors: check output over a hypothetical edit that adds new code — a whole
    ///     method appended to a file, or a whole file dropped into a project — so there is no committed
    ///     line to anchor against. They must stay unmatched against committed source; the day such code
    ///     lands, the matching anchor here fails and is moved to the content or landmark bucket.
    /// </summary>
    private static readonly HashSet<(string Doc, string File, int Line)> DemonstrationAnchors =
    [
        // The statistical-prior and agent-loop walkthroughs append a hypothetical inline-SQL method to
        // the migrated BookingsController (71 committed lines, no SqlClient); the quoted lines are that
        // method's check output.
        (MeridianReadme, BookingsController, 72),
        (MeridianReadme, BookingsController, 73),
        (MeridianReadme, BookingsController, 74),
        (MeridianReadme, BookingsController, 75),
        (MeridianReadme, BookingsController, 77),
        (MeridianReadme, BookingsController, 78),

        // Beat 2 of the storyboard writes a hypothetical inline-SQL method into BookingsController,
        // along with the using and constructor parameter it needs; the quoted lines are that method's
        // check output, captured by following the storyboard's own reproduce steps.
        (Storyboard, BookingsController, 85),
        (Storyboard, BookingsController, 86),
        (Storyboard, BookingsController, 87),
        (Storyboard, BookingsController, 88),
        (Storyboard, BookingsController, 90),

        // The root README's hook beat quotes the block a red self-check feeds an agent: the hypothetical
        // edit is a whole new ProgressPrinter file dropped into the CLI's Rendering directory, which is
        // not committed, because this repository ships its own self-check green.
        (RootReadme, ProgressPrinter, 10),
        (RootReadme, ProgressPrinter, 15),

        // The layering walkthrough drops a new ExpireQuotesHandler file into the Api project to breach the
        // Application boundary; that file is not committed, because the subsystem ships green.
        (QuotingReadme, ExpireQuotesHandler, 12),
        (QuotingReadme, ExpireQuotesHandler, 16),
        (QuotingReadme, ExpireQuotesHandler, 17),

        // The module-isolation walkthrough adds a new InvoicePreview file in Dispatch that constructs
        // Invoicing's internal assembler; that file is not committed, because the subsystem ships green.
        (OperationsReadme, InvoicePreview, 9)
    ];

    [Fact]
    public void CommittedSourceAnchors_MatchTheirCommittedLine()
    {
        // Arrange
        Func<string, IReadOnlyList<string>?> reader = SourceAnchors.DiskReader(RepoRoot.Directory);
        List<string> drift = new();

        // Act: every anchor that is not a documented demonstration must resolve against committed source.
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in Gate.Scan(doc))
        {
            if (DemonstrationAnchors.Contains((doc, anchor.File, anchor.Line))) continue;

            SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, exampleRoot, Landmarks, reader);
            if (result.Bucket == SourceAnchors.AnchorBucket.Unresolved) drift.Add(result.Failure!);
        }

        // Assert
        drift.ShouldReportNothing("Quoted source anchors no longer match their committed source");
    }

    [Fact]
    public void DemonstrationAnchors_DoNotResolveAgainstCommittedSource()
    {
        // Arrange
        Func<string, IReadOnlyList<string>?> reader = SourceAnchors.DiskReader(RepoRoot.Directory);
        List<string> promoted = new();

        // Act: a demonstration anchor quotes a hypothetical edit's output, so it must not match committed
        // source; if one starts matching, that code has landed and the anchor should become a real one.
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in Gate.Scan(doc))
        {
            if (!DemonstrationAnchors.Contains((doc, anchor.File, anchor.Line))) continue;

            SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, exampleRoot, Landmarks, reader);
            if (result.Bucket != SourceAnchors.AnchorBucket.Unresolved) promoted.Add($"{doc} -> {anchor.File}:{anchor.Line} now matches committed source via {result.Bucket}; move it to the content or landmark bucket.");
        }

        // Assert
        promoted.ShouldReportNothing("Demonstration anchors now match committed source");
    }

    [Fact]
    public void EachAnchorDoc_YieldsAtLeastOneAnchor()
    {
        // Act & Assert: guard against the scanner silently matching nothing if a doc's quoting style changes.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    [Fact]
    public void EveryTrackedDocQuotingAnchors_IsRegistered()
    {
        // Act & Assert: the registry above is hand-written, so the failure it cannot see is a doc that
        // quotes anchors and was never added to it — a whole walkthrough silently outside the gate.
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "source anchor(s)",
            swept: "source anchors",
            authority: "their committed source");
    }

    [Fact]
    public void LandmarkAndDemonstrationEntries_AllMatchAnExtractedAnchor()
    {
        // Arrange
        HashSet<(string ExampleRoot, string File, int Line)> byKey = new();
        HashSet<(string Doc, string File, int Line)> byDoc = new();
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in Gate.Scan(doc))
        {
            byKey.Add((exampleRoot, anchor.File, anchor.Line));
            byDoc.Add((doc, anchor.File, anchor.Line));
        }

        // Act
        List<string> dead = new();
        foreach (SourceAnchors.AnchorKey key in Landmarks.Keys)
            if (!byKey.Contains((key.ExampleRoot, key.File, key.Line)))
                dead.Add($"landmark {key.ExampleRoot}/{key.File}:{key.Line} matches no extracted anchor.");

        foreach ((string Doc, string File, int Line) demonstration in DemonstrationAnchors)
            if (!byDoc.Contains(demonstration))
                dead.Add($"demonstration {demonstration.Doc} -> {demonstration.File}:{demonstration.Line} matches no extracted anchor.");

        // Assert
        dead.ShouldReportNothing("These pin entries no longer correspond to any anchor and should be removed");
    }

    [Fact]
    public void BookingsControllerLength_HoldsTheAppendedMethodWalkthroughAnchors()
    {
        // Arrange: the appended-method walkthrough adds a method to the committed BookingsController, so
        // its quoted anchors begin at the line after the committed end of file (:72 onward, over 71
        // committed lines). This pin ties that demonstration cluster — in the Meridian README and the
        // hooks storyboard — to the committed length: change the length here and every quoted anchor
        // renumbers, so the quoted output and the committed file must move together.
        const int committedLength = 71;
        string path = Path.Combine(
            RepoRoot.Directory,
            $"{MeridianRoot}/{BookingsController}".Replace('/', Path.DirectorySeparatorChar));

        // Act
        int actual = File.ReadLines(path)
            .Count();

        // Assert
        actual.ShouldBe(
            committedLength,
            $"BookingsController.cs is {actual} lines but the appended-method walkthrough anchors assume {committedLength}; renumber the quoted output in {MeridianReadme} and {Storyboard}.");
    }

    private static IReadOnlyList<SourceAnchor> ExtractDoc(string doc)
    {
        string text = RepoRoot.ReadText(doc);
        return SourceAnchors.Extract(doc, text);
    }
}
