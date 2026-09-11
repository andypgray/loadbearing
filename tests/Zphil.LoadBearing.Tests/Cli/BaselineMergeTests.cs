using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;
using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     What a baseline file does when two branches burn it down at once, put through a real three-way text
///     merge (<c>git merge-file</c>, which needs no repository): two branches paying off different entries
///     of one rule merge clean and the merged file verifies with nothing to re-run; the same two payoffs in
///     the legacy whole-file-digest format merge their <em>entries</em> clean and conflict on the one line
///     that covers all of them, after which neither resolution verifies; and where line adjacency does
///     force a conflict, keeping whole entry lines from either side yields a file the reader accepts.
/// </summary>
/// <remarks>
///     These rows hold the claim the per-entry seal exists to make, so they assert through
///     <see cref="BaselineStore.TryReadDocument" /> rather than by reading the merged text: what matters is
///     not that the bytes look plausible but that the tool accepts them without asking anyone to re-run a
///     valve. The claim is narrow on purpose — a conflict over one rule is now <em>resolvable by hand</em>,
///     not impossible — which is why the third row stands beside the first. Resolutions here are performed
///     on git's own output by choosing whole lines, never by recomputing anything: that is the property
///     under test, and a helper that composed a fresh file would assert nothing about it.
/// </remarks>
public sealed class BaselineMergeTests : IDisposable
{
    private const string Rule = "data-access/no-inline-sql";
    private const string Target = "T:System.Data.DataTable";
    private const string EntryIndent = "        { ";

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-merge");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void MergeFile_TwoBranchesPayingOffDifferentEntries_MergeCleanAndTheResultVerifies()
    {
        // Five captured entries in one rule's section; one branch pays off B, the other D. Nothing the two
        // sides both touch, so there is nothing to conflict on — which is the whole change, because the
        // whole-file digest was a line both sides always rewrote.
        MergeResult merged = Merge(
            Compose("A", "B", "C", "D", "E"),
            Compose("A", "C", "D", "E"),
            Compose("A", "B", "C", "E"));

        merged.Conflicts.ShouldBe(0);
        ShouldVerifyWith("merged.json", merged.Text, Entry("A"), Entry("C"), Entry("E"));
    }

    [Fact]
    public void MergeFile_TheSamePayoffsInTheLegacyFormat_ConflictOnTheDigestAndNeitherSideResolves()
    {
        // The counterexample the seal answers, as a fact rather than as prose. One entry per line was
        // genuinely merge-friendly: the entry lines below come through as A, C, E, the right answer. The
        // digest line is not, because both sides rewrote it — and it is unresolvable rather than merely
        // conflicted, since the surviving set is neither side's, so whichever digest a human keeps was
        // computed over entries the file no longer holds and the file is refused as tampered. Restoring
        // from version control cannot help: both committed sides are equally stale.
        MergeResult merged = Merge(
            ComposeLegacy("A", "B", "C", "D", "E"),
            ComposeLegacy("A", "C", "D", "E"),
            ComposeLegacy("A", "B", "C", "E"));

        merged.Conflicts.ShouldBeGreaterThan(0);
        merged.Text.ShouldContain("\"digest\"");
        merged.Text.ShouldContain("T:MyApp.Web.AController");
        merged.Text.ShouldContain("T:MyApp.Web.CController");
        merged.Text.ShouldContain("T:MyApp.Web.EController");

        ShouldBeRefusedAsTampered("resolved-ours-v1.json", KeepOneSide(merged.Text, takeOurs: true));
        ShouldBeRefusedAsTampered("resolved-theirs-v1.json", KeepOneSide(merged.Text, takeOurs: false));
    }

    [Fact]
    public void MergeFile_AdjacentPayoffsResolvedByKeepingWholeEntryLines_Verifies()
    {
        // The narrow claim, held honestly. Paying off the last entry closes the line before it, so a branch
        // that pays off D collides with one that pays off E — adjacent lines, and git cannot merge them.
        // What the seal buys is that every line either side committed is a complete, self-sealed entry, so
        // the resolution below chooses whole lines and closes the new last one. A trailing comma is JSON
        // punctuation, not sealed content; nothing is recomputed, and no valve is re-run.
        MergeResult merged = Merge(
            Compose("A", "B", "C", "D", "E"),
            Compose("A", "B", "C", "E"),
            Compose("A", "B", "C", "D"));

        merged.Conflicts.ShouldBeGreaterThan(0);
        ShouldVerifyWith(
            "resolved.json", KeepWhatBothSidesKept(merged.Text), Entry("A"), Entry("B"), Entry("C"));
    }

    private static BaselineEntry Entry(string name)
    {
        return BaselineEntry.ForEdge($"T:MyApp.Web.{name}Controller", Target);
    }

    private static string Compose(params string[] names)
    {
        return BaselineComposer.Compose(Rule, names.Select(Entry).ToArray());
    }

    private static string ComposeLegacy(params string[] names)
    {
        return BaselineComposer.ComposeLegacy(Rule, names.Select(Entry).ToArray());
    }

    /// <summary>
    ///     <paramref name="merged" /> with one side of every conflict kept whole and the markers dropped —
    ///     the resolution <c>git checkout --ours</c> and <c>--theirs</c> perform, and the one a human
    ///     reaches for over a single conflicted line. Nothing is edited.
    /// </summary>
    private static string KeepOneSide(string merged, bool takeOurs)
    {
        var kept = new List<string>();
        bool? side = null; // null outside a conflict, true in ours, false in theirs
        foreach (string line in merged.Split('\n'))
        {
            if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                side = true;
                continue;
            }

            if (line.StartsWith("=======", StringComparison.Ordinal))
            {
                side = false;
                continue;
            }

            if (line.StartsWith(">>>>>>>", StringComparison.Ordinal))
            {
                side = null;
                continue;
            }

            if (side is null || side == takeOurs) kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    /// <summary>
    ///     <paramref name="merged" /> resolved the way two payoffs of one rule resolve: inside each
    ///     conflict keep the lines <em>both</em> sides kept — an entry either branch paid off is absent from
    ///     that branch's side, so the intersection is what neither paid off — then close the last entry's
    ///     line. Choosing whole lines and one comma is the whole of the hand resolution.
    /// </summary>
    private static string KeepWhatBothSidesKept(string merged)
    {
        var kept = new List<string>();
        var ours = new List<string>();
        var theirs = new List<string>();
        bool? side = null;
        foreach (string line in merged.Split('\n'))
        {
            if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                side = true;
                continue;
            }

            if (line.StartsWith("=======", StringComparison.Ordinal))
            {
                side = false;
                continue;
            }

            if (line.StartsWith(">>>>>>>", StringComparison.Ordinal))
            {
                kept.AddRange(ours.Where(theirs.Contains));
                ours.Clear();
                theirs.Clear();
                side = null;
                continue;
            }

            if (side is null) kept.Add(line);
            else if (side.Value) ours.Add(line);
            else theirs.Add(line);
        }

        int last = kept.FindLastIndex(line => line.StartsWith(EntryIndent, StringComparison.Ordinal));
        kept[last] = kept[last]
            .TrimEnd(',');
        return string.Join("\n", kept);
    }

    /// <summary>
    ///     A real three-way text merge of the three versions, through <c>git merge-file -p</c>: git's own
    ///     merge over files, with no repository and no index — the same algorithm a branch merge runs over
    ///     these bytes.
    /// </summary>
    private MergeResult Merge(string @base, string ours, string theirs)
    {
        _temp.WriteFile(["base.json"], @base);
        _temp.WriteFile(["ours.json"], ours);
        _temp.WriteFile(["theirs.json"], theirs);

        ChildProcess.ProcessResult result = GitCommand.Attempt(
            _temp.Path, "merge-file", "-p", "ours.json", "base.json", "theirs.json");
        // merge-file reports the number of conflicts as its exit code and an error as a negative one, so a
        // run that never started would otherwise read here as a very conflicted merge.
        result.ExitCode.ShouldBeInRange(0, 100);
        return new MergeResult(result.ExitCode, result.StandardOutput);
    }

    private void ShouldVerifyWith(string fileName, string text, params BaselineEntry[] expected)
    {
        string path = _temp.WriteFile([fileName], text);
        BaselineStore.TryReadDocument(path)
            .ShouldNotBeNull()
            .Sections[Rule]
            .ShouldBe(expected);
    }

    private void ShouldBeRefusedAsTampered(string fileName, string text)
    {
        string path = _temp.WriteFile([fileName], text);
        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("failed its integrity check");
    }

    private readonly record struct MergeResult(int Conflicts, string Text);
}
