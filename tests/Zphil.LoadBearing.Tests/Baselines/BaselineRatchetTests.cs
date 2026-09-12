using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Tests.Baselines;

/// <summary>
///     The four-way ratchet call (GRAMMAR §4.3) at its owner: how many sites a run observed under a
///     <see cref="BaselineEntry" />, against how many that entry grandfathers. One row per arm —
///     uncounted, shrunk, held, grown, and the two routes to nothing-to-measure. The checker's verdict and
///     <c>loadbearing baseline --accept-reductions</c> both read this, so the arms are pinned here once
///     rather than once per caller, which is the whole point of there being an owner.
/// </summary>
/// <remarks>
///     <para>
///         Both routes to "nothing to measure" are rows because the two callers used to disagree about the
///         second of them, and the disagreement was invisible precisely because neither side's tests could
///         see the other's reading.
///     </para>
///     <para>
///         The expected arm is spelled in each row's name rather than passed to it: the states are internal
///         to Core, and a public test signature cannot name one.
///     </para>
/// </remarks>
public sealed class BaselineRatchetTests
{
    private static BaselineEntry Edge => BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db");

    [Fact]
    public void Classify_EdgeEntryRecordingNoCount_IsUncounted()
    {
        // The state a write can clear, and the only one it can: the entry holds its pair at any size until
        // something records what the pair actually measures.
        BaselineRatchet.Classify(Edge, observedSites: 2)
            .ShouldBe(RatchetState.Uncounted);
    }

    [Fact]
    public void Classify_FewerSitesThanTheEntryRecords_IsShrunk()
    {
        BaselineRatchet.Classify(Edge.WithSiteCount(3), observedSites: 2)
            .ShouldBe(RatchetState.Shrunk);
    }

    [Fact]
    public void Classify_AsManySitesAsTheEntryRecords_IsHeld()
    {
        BaselineRatchet.Classify(Edge.WithSiteCount(2), observedSites: 2)
            .ShouldBe(RatchetState.Held);
    }

    [Fact]
    public void Classify_MoreSitesThanTheEntryRecords_IsGrown()
    {
        BaselineRatchet.Classify(Edge.WithSiteCount(2), observedSites: 3)
            .ShouldBe(RatchetState.Grown);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(null)]
    public void Classify_EdgeSeenWithNoSitedEvidence_IsNotMeasured(int? allowance)
    {
        // The row the ordering inside Classify exists for, and only the first of the two reaches it: read
        // behind the allowance compare, an edge with no evidence against an entry recording sites is a
        // reduction, and the write path then lowers the count to zero — which WithSiteCount refuses, so the
        // mode throws on the very file it was pointed at.
        BaselineEntry stored = allowance is { } recorded ? Edge.WithSiteCount(recorded) : Edge;

        BaselineRatchet.Classify(stored, observedSites: 0)
            .ShouldBe(RatchetState.NotMeasured);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Classify_SubjectEntry_IsNotMeasuredHoweverManySitesTheRunSaw(int observedSites)
    {
        // A subject entry's sites are declarations, so there is no measure to take and nothing a write could
        // record. Reading it as uncounted would leave a naming rule's whole section reporting a state no
        // author could clear.
        BaselineRatchet.Classify(BaselineEntry.ForSubject("T:App.BadThing"), observedSites)
            .ShouldBe(RatchetState.NotMeasured);
    }
}
