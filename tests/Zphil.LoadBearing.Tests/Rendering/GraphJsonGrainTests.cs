using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The survey document's floor rung, driven through <see cref="GraphJsonRenderer.Document" /> directly
///     rather than through the runner — because the slot under test is the load's own diagnostic stream, and
///     the MyApp fixture loads cleanly, so no end-to-end row can populate it.
/// </summary>
/// <remarks>
///     <see cref="Cli.GraphCommandTests" /> pins the rungs over the real fixture, including the goldens. What is
///     left over is the case that fixture cannot show: a survey of a solution whose load complained. That
///     array is the one thing on this document that scales with neither the codebase nor the solution but
///     with the load's troubles — one entry per project per framework per complaint — so it is what decides
///     whether the ladder has a floor at all.
/// </remarks>
public sealed class GraphJsonGrainTests
{
    private const string Diagnostic =
        "MassTransit/MassTransit.csproj : warning NU1900: Error occurred while getting package vulnerability data.";

    [Fact]
    public void Document_IndexGrain_ElidesTheDiagnosticStreamToItsCount()
    {
        // On a field bed whose package-audit feed was unreachable this array was 98% of the survey and rode
        // every rung untouched, so the roster rung alone still overran the channel (measured). It is the
        // last thing the floor drops.
        string index = Render(DocumentGrain.Index, [Diagnostic, Diagnostic]);

        index.ShouldNotContain("\"workspaceDiagnostics\"");
        index.ShouldContain("\"workspaceDiagnosticCount\": 2");
    }

    [Fact]
    public void Document_IndexGrain_KeepsEveryTrustStampBesideTheCount()
    {
        // What makes the elision safe rather than merely smaller: the actionable half of the stream is
        // already keyed, and stays keyed at the one grain a reader reaches when nothing else fits. A survey
        // that dropped both would be a wrong map wearing a clean one's clothes.
        string index = Render(DocumentGrain.Index, [Diagnostic]);

        index.ShouldContain("\"modelIncomplete\": true");
        index.ShouldContain("\"failedProjects\"");
        index.ShouldContain("\"restoreFailedProjects\"");
    }

    [Fact]
    public void Document_EveryGrainAboveTheFloor_KeepsTheDiagnosticStreamWhole()
    {
        // The elision belongs to the floor rung alone: a reader who can hold a skeleton gets MSBuild's own
        // words in full, which is the only place the load's account of itself survives.
        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview, DocumentGrain.Skeleton })
        {
            string document = Render(grain, [Diagnostic]);

            document.ShouldContain("\"workspaceDiagnostics\"");
            document.ShouldNotContain("\"workspaceDiagnosticCount\"");
        }
    }

    [Fact]
    public void Document_IndexGrainWithACleanLoad_CarriesNeitherTheStreamNorABareZero()
    {
        // Absent when there is nothing to say, which is the rule every <key>Count on these documents takes:
        // an elision reports what it dropped, so dropping nothing reports nothing.
        string index = Render(DocumentGrain.Index, []);

        index.ShouldNotContain("\"workspaceDiagnostic");
    }

    private static string Render(DocumentGrain grain, IReadOnlyList<string> workspaceDiagnostics)
    {
        ProjectSummary project = new("Acme.App", ["Acme.Core"], 3, 0, [new NamespaceCount("Acme.App", 3, 0)]);
        GraphSummary summary = new([project], [], [], [], []);

        return GraphJsonRenderer.Document(
            summary,
            Directory.GetCurrentDirectory(),
            "Acme.sln",
            workspaceDiagnostics,
            // Named, because five of the seven slots are IReadOnlyList<string> and this test's whole subject
            // is which of them survive the floor rung.
            new WorkspaceDiagnostics(
                LoadFailures: [],
                MergeNotes: [],
                FailedProjects: ["Acme.App/Acme.App.csproj"],
                UncheckedProjects: [],
                RestoreFailedProjects: ["Acme.Core/Acme.Core.csproj"],
                UnsupportedProjects: [],
                MultiTargetedProjects: []),
            grain,
            []);
    }
}
