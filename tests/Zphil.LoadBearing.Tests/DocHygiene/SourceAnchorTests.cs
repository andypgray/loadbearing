using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="SourceAnchors" />. They pin the fenced-anchor scanner and
///     the token derivation, and prove that a drifted anchor — one whose committed line no longer
///     carries its content, whose landmark points at the wrong line, or whose file or line is gone —
///     is reported by the same classifier the walkthrough gate runs on.
/// </summary>
public sealed class SourceAnchorTests
{
    private const char EmDash = (char)0x2014;

    private static readonly IReadOnlyDictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark> NoLandmarks =
        new Dictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark>();

    [Fact]
    public void Extract_FencedAnchors_CapturedWithLocationAndParts()
    {
        // Arrange
        string[] lines =
        [
            "intro prose",
            "```text",
            $"  src/Foo/Bar.cs:12 {EmDash} Ns.A references Ns.B.Baz",
            "  a plain line that is not an anchor",
            "```",
            $"src/Outside.cs:1 {EmDash} Ns.C references Ns.D"
        ];
        string doc = string.Join("\n", lines);

        // Act
        var anchors = SourceAnchors.Extract("d.md", doc);

        // Assert
        SourceAnchor anchor = anchors.ShouldHaveSingleItem();
        anchor.Doc.ShouldBe("d.md");
        anchor.DocLine.ShouldBe(3);
        anchor.File.ShouldBe("src/Foo/Bar.cs");
        anchor.Line.ShouldBe(12);
        anchor.Message.ShouldBe("Ns.A references Ns.B.Baz");
    }

    [Fact]
    public void Extract_MultipleFencedBlocks_AllAnchorsCaptured()
    {
        // Arrange
        string[] lines =
        [
            "```text",
            $"  src/One.cs:3 {EmDash} Ns.T constructs System.Net.Http.HttpClient",
            "```",
            "prose between blocks",
            "~~~",
            $"src/Two.cs:9 {EmDash} Ns.U catches System.Exception",
            "~~~"
        ];
        string doc = string.Join("\n", lines);

        // Act
        var anchors = SourceAnchors.Extract("d.md", doc);

        // Assert
        anchors.Count.ShouldBe(2);
        anchors[0].File.ShouldBe("src/One.cs");
        anchors[0].DocLine.ShouldBe(2);
        anchors[1].File.ShouldBe("src/Two.cs");
        anchors[1].DocLine.ShouldBe(6);
    }

    [Theory]
    [InlineData("Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlConnection", "SqlConnection")]
    [InlineData("Meridian.Interchange.Partners.CarrierClient constructs System.Net.Http.HttpClient", "HttpClient")]
    [InlineData("Meridian.Interchange.Dispatch.OutboxDispatcher injects Meridian.Interchange.Outbox.IOutboxStore", "IOutboxStore")]
    [InlineData("Meridian.Interchange.Partners.IPartnerClient exposes Meridian.Interchange.Outbox.OutboxMessage", "OutboxMessage")]
    [InlineData("Meridian.Interchange.Processing.OutboxProcessor catches System.Exception", "Exception")]
    [InlineData("OutboxDispatcher references Microsoft.Extensions.Options.IOptionsSnapshot<TOptions>", "IOptionsSnapshot")]
    [InlineData("Meridian.Web.Controllers.CustomsController uses System.DateTime.UtcNow", "DateTime.UtcNow")]
    [InlineData("Meridian.Web.Controllers.DriversController uses System.DateTime.Now", "DateTime.Now")]
    [InlineData("Meridian.Interchange.Host.ScopedDispatchRunner.RunPendingAsync()", "RunPendingAsync")]
    [InlineData("Meridian.Interchange.Processing.IOutboxProcessor.ProcessPending()", "ProcessPending")]
    [InlineData("Meridian.Quoting.Application.Handlers.RequestQuoteHandler", "RequestQuoteHandler")]
    [InlineData("Meridian.Interchange.Outbox.OutboxMessage", "OutboxMessage")]
    public void DeriveToken_MessageShape_YieldsExpectedToken(string message, string expected)
    {
        // Act
        string token = SourceAnchors.DeriveToken(message);

        // Assert
        token.ShouldBe(expected);
    }

    [Fact]
    public void Classify_TokenOnReportedLine_Content()
    {
        // Arrange
        SourceAnchor anchor = new("d.md", 5, "src/A.cs", 3, "Ns.T references Ns.Pkg.Foo");
        var reader = Reader("ex/src/A.cs", "one", "two", "here is Foo on line three");

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", NoLandmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Content);
    }

    [Fact]
    public void Classify_TokenShiftedByOneLine_Unresolved()
    {
        // Arrange: the token sits on line 3, but the anchor claims line 4.
        SourceAnchor anchor = new("d.md", 5, "src/A.cs", 4, "Ns.T references Ns.Pkg.Foo");
        var reader = Reader("ex/src/A.cs", "one", "two", "here is Foo", "four has no token");

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", NoLandmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Unresolved);
        result.Failure!.ShouldContain("Foo");
        result.Failure!.ShouldContain("line 4");
    }

    [Fact]
    public void Classify_TokenOnReportedLineWithLandmarkPresent_StaysContent()
    {
        // Arrange: content already matches, so the anchor stays in the content bucket even though a
        // landmark exists — fewer pins carry the weight.
        SourceAnchor anchor = new("d.md", 5, "src/A.cs", 3, "Ns.T references Ns.Pkg.Foo");
        var reader = Reader("ex/src/A.cs", "one", "two", "here is Foo");
        Dictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark> landmarks = new()
        {
            [new SourceAnchors.AnchorKey("ex", "src/A.cs", 3)] = new SourceAnchors.Landmark(1, "one")
        };

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", landmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Content);
    }

    [Fact]
    public void Classify_LandmarkPinsRightCommittedLine_Landmark()
    {
        // Arrange: the reported line carries no token, but the landmark pins the committed line it lands on.
        SourceAnchor anchor = new("d.md", 5, "src/A.cs", 4, "Ns.T references Ns.Pkg.Foo");
        var reader = Reader("ex/src/A.cs", "one", "two", "here is Foo", "four");
        Dictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark> landmarks = new()
        {
            [new SourceAnchors.AnchorKey("ex", "src/A.cs", 4)] = new SourceAnchors.Landmark(3, "here is Foo")
        };

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", landmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Landmark);
    }

    [Fact]
    public void Classify_LandmarkPinsWrongCommittedLine_Unresolved()
    {
        // Arrange: the landmark points at a committed line that does not carry its snippet.
        SourceAnchor anchor = new("d.md", 5, "src/A.cs", 4, "Ns.T references Ns.Pkg.Foo");
        var reader = Reader("ex/src/A.cs", "one", "two", "here is Foo", "four");
        Dictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark> landmarks = new()
        {
            [new SourceAnchors.AnchorKey("ex", "src/A.cs", 4)] = new SourceAnchors.Landmark(2, "here is Foo")
        };

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", landmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Unresolved);
        result.Failure!.ShouldContain("landmark snippet");
    }

    [Fact]
    public void Classify_LandmarkResolvesWhenReportedLineBeyondEof_Landmark()
    {
        // Arrange: the documented edit adds a using that pushes the signature past the committed end of
        // file; the landmark pins the committed signature line the anchor is keyed to.
        SourceAnchor anchor = new("d.md", 5, "src/A.cs", 6, "Ns.I exposes Ns.Pkg.Entity");
        var reader = Reader("ex/src/A.cs", "one", "two", "three", "four", "Task SendAsync here");
        Dictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark> landmarks = new()
        {
            [new SourceAnchors.AnchorKey("ex", "src/A.cs", 6)] = new SourceAnchors.Landmark(5, "Task SendAsync")
        };

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", landmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Landmark);
    }

    [Fact]
    public void Classify_MissingFile_Unresolved()
    {
        // Arrange
        SourceAnchor anchor = new("d.md", 5, "src/Gone.cs", 3, "Ns.T references Ns.Pkg.Foo");
        Func<string, IReadOnlyList<string>?> reader = _ => null;

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", NoLandmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Unresolved);
        result.Failure!.ShouldContain("not found");
    }

    [Fact]
    public void Classify_LineBeyondEndOfFile_Unresolved()
    {
        // Arrange
        SourceAnchor anchor = new("d.md", 5, "src/A.cs", 99, "Ns.T references Ns.Pkg.Foo");
        var reader = Reader("ex/src/A.cs", "one", "two", "three");

        // Act
        SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, "ex", NoLandmarks, reader);

        // Assert
        result.Bucket.ShouldBe(SourceAnchors.AnchorBucket.Unresolved);
        result.Failure!.ShouldContain("beyond end of file");
    }

    private static Func<string, IReadOnlyList<string>?> Reader(string path, params string[] lines)
    {
        return requested => requested == path ? lines : null;
    }
}