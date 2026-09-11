using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     <see cref="BaselineComposer.ComposeLegacy(ValueTuple{string, BaselineEntry[]}[])" />, the one helper
///     here that mints a file shape the product no longer writes: it downgrades a composed file rather than
///     hand-writing one, so what it produces stays derived from the product even as the current format moves
///     away from it. Pinned because a silent drift would leave the legacy read path exercised against
///     nothing — the rows that read a v1 file would be reading a file no v1 tool ever wrote.
/// </summary>
public sealed class BaselineComposerTests
{
    [Fact]
    public void ComposeLegacy_UncountedEntries_CarryAWholeFileDigestAndNoSeals()
    {
        BaselineEntry edge = BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db");
        BaselineEntry subject = BaselineEntry.ForSubject("T:App.Thing");
        string expectedDigest = BaselineFormat.LegacyDigest(BaselineComposer.Rules(("data/x", [edge, subject])));

        string legacy = BaselineComposer.ComposeLegacy("data/x", edge, subject);

        legacy.ShouldContain($"  \"schemaVersion\": {BaselineFormat.LegacySchemaVersion},\n");
        legacy.ShouldContain($"  \"digest\": \"{expectedDigest}\",\n");
        legacy.ShouldNotContain("\"seal\"");
    }

    [Fact]
    public void ComposeLegacy_ACountedEntry_IsRefused()
    {
        // A legacy file has no measure, so downgrading a counted entry would mint a shape no write ever
        // produced — and one the reader refuses as malformed, which would red as a puzzle about the reader
        // rather than about the arrangement that asked for it.
        BaselineEntry counted = BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
            .WithSiteCount(2);

        Should.Throw<ArgumentException>(() => BaselineComposer.ComposeLegacy("data/x", counted))
            .Message.ShouldContain("A legacy baseline carries no site count");
    }
}
