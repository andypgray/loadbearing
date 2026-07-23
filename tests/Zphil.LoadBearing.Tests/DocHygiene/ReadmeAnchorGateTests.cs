using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The walkthrough gate over quoted source anchors. The example walkthroughs quote <c>check</c>
///     violation output inside fenced code blocks, and each quoted line carries a <c>file:line</c>
///     anchor into a committed example source. This gate lifts every anchor and holds it to its
///     source: a committed-source anchor must match the line it names, by content or by a landmark pin
///     that keys a documented walkthrough edit to the committed line it lands on; a demonstration
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

    private const string MeridianReadme = "examples/Meridian/README.md";
    private const string Storyboard = "examples/Meridian/hooks/storyboard.md";
    private const string QuotingReadme = "examples/Meridian.Quoting/README.md";
    private const string OperationsReadme = "examples/Meridian.Operations/README.md";

    private const string MeridianRoot = "examples/Meridian";
    private const string QuotingRoot = "examples/Meridian.Quoting";
    private const string OperationsRoot = "examples/Meridian.Operations";
    private const string InterchangeRoot = "examples/Meridian.Interchange";

    /// <summary>The anchor-bearing docs, each paired with the example root its anchors resolve against.</summary>
    private static readonly (string Doc, string ExampleRoot)[] AnchorDocs =
    [
        (MeridianReadme, MeridianRoot),
        ("examples/Meridian/ADOPTING.md", MeridianRoot),
        (Storyboard, MeridianRoot),
        (QuotingReadme, QuotingRoot),
        (OperationsReadme, OperationsRoot),
        ("examples/Meridian.Interchange/README.md", InterchangeRoot)
    ];

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

        // Beat 2 of the storyboard writes the same hypothetical inline-SQL method into BookingsController.
        (Storyboard, BookingsController, 72),
        (Storyboard, BookingsController, 73),
        (Storyboard, BookingsController, 74),
        (Storyboard, BookingsController, 75),
        (Storyboard, BookingsController, 77),

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
        var reader = SourceAnchors.DiskReader(RepoRoot.Directory);
        List<string> drift = new();

        // Act: every anchor that is not a documented demonstration must resolve against committed source.
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in ExtractDoc(doc))
        {
            if (DemonstrationAnchors.Contains((doc, anchor.File, anchor.Line))) continue;

            SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, exampleRoot, Landmarks, reader);
            if (result.Bucket == SourceAnchors.AnchorBucket.Unresolved) drift.Add(result.Failure!);
        }

        // Assert
        drift.ShouldBeEmpty(
            $"Quoted source anchors no longer match their committed source:\n{string.Join("\n", drift)}");
    }

    [Fact]
    public void DemonstrationAnchors_DoNotResolveAgainstCommittedSource()
    {
        // Arrange
        var reader = SourceAnchors.DiskReader(RepoRoot.Directory);
        List<string> promoted = new();

        // Act: a demonstration anchor quotes a hypothetical edit's output, so it must not match committed
        // source; if one starts matching, that code has landed and the anchor should become a real one.
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in ExtractDoc(doc))
        {
            if (!DemonstrationAnchors.Contains((doc, anchor.File, anchor.Line))) continue;

            SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, exampleRoot, Landmarks, reader);
            if (result.Bucket != SourceAnchors.AnchorBucket.Unresolved) promoted.Add($"{doc} -> {anchor.File}:{anchor.Line} now matches committed source via {result.Bucket}; move it to the content or landmark bucket.");
        }

        // Assert
        promoted.ShouldBeEmpty(
            $"Demonstration anchors now match committed source:\n{string.Join("\n", promoted)}");
    }

    [Fact]
    public void EachAnchorDoc_YieldsAtLeastOneAnchor()
    {
        // Arrange
        List<string> empty = new();

        // Act: guard against the scanner silently matching nothing if a doc's quoting style changes.
        foreach ((string doc, string _) in AnchorDocs)
            if (ExtractDoc(doc).Count == 0)
                empty.Add(doc);

        // Assert
        empty.ShouldBeEmpty(
            $"These anchor docs yielded no anchors; the scanner may be silently matching nothing:\n{string.Join("\n", empty)}");
    }

    [Fact]
    public void LandmarkAndDemonstrationEntries_AllMatchAnExtractedAnchor()
    {
        // Arrange
        HashSet<(string ExampleRoot, string File, int Line)> byKey = new();
        HashSet<(string Doc, string File, int Line)> byDoc = new();
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in ExtractDoc(doc))
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
        dead.ShouldBeEmpty($"These pin entries no longer correspond to any anchor and should be removed:\n{string.Join("\n", dead)}");
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
        int actual = File.ReadLines(path).Count();

        // Assert
        actual.ShouldBe(
            committedLength,
            $"BookingsController.cs is {actual} lines but the appended-method walkthrough anchors assume {committedLength}; renumber the quoted output in {MeridianReadme} and {Storyboard}.");
    }

    private static IReadOnlyList<SourceAnchor> ExtractDoc(string doc)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot.Directory, doc.Replace('/', Path.DirectorySeparatorChar)));
        return SourceAnchors.Extract(doc, text);
    }
}