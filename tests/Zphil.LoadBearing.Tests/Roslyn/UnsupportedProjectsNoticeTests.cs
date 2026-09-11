using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The wording of the coverage statement every verb writes when the solution declares projects no
///     extractor reaches: the per-kind reason, the entry line that joins it to its project, the two verb
///     stamps and the adapter's skip — pinned pure, no workspace and no disk.
///     <see cref="Zphil.LoadBearing.Tests.Cli.PolyglotSurveyE2ETests" /> pins the same bytes coming out of a
///     real polyglot load; what is spec here is the half a fixture solution cannot reach, since the suite
///     holds no <c>.shproj</c> bed and deliberately does not need one.
/// </summary>
/// <remarks>
///     A kind rides all the way from its producer because one string stamped onto every path at the composing
///     edge cannot describe them all: the <c>.shproj</c> — a container whose <c>.projitems</c> compile into
///     the projects that import it — would be reported as a language this product cannot read, on every
///     surface at once.
/// </remarks>
public sealed class UnsupportedProjectsNoticeTests
{
    // The entries every stamp below shares, so each test reads as its own tail and nothing else. Both kinds,
    // because a stamp's lede states no cause and each entry has to carry its own.
    private static readonly string[] TwoProjects =
    [
        "PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project",
        "Shared/Shared.shproj — a shared project, compiled into the projects that import it"
    ];

    [Fact]
    public void Reason_ASharedProject_DescribesItsShapeRatherThanItsLanguage()
    {
        // The extension cannot tell us the language of a shared project and does not need to: what a reader
        // has to know is that nothing loads this file and its sources reach the model through the importers.
        UnsupportedProjectsNotice.Reason(UnsupportedProjectKind.SharedProject)
            .ShouldBe("a shared project, compiled into the projects that import it");
    }

    [Fact]
    public void Reason_ANonCsharpProject_KeepsTheSentenceItAlwaysHad()
    {
        // Unchanged text, deliberately: the taxonomy exists because one kind needed different words, not
        // because this one's were wrong. Every document over an .fsproj reads exactly as it did.
        UnsupportedProjectsNotice.Reason(UnsupportedProjectKind.NotCsharp)
            .ShouldBe("not a C# project");
    }

    [Fact]
    public void Reason_EveryDeclaredKind_ReadsDifferentlyFromTheOthers()
    {
        // The guard on the switch's fallback arm, which is NotCsharp — the right default, and also the way a
        // kind added without wording would be described as the wrong thing, which is the defect the taxonomy
        // exists to retire. The compiler cannot catch it: dropping the arm to make a missing member a warning
        // makes every unnamed value one instead, permanently. So the omission is caught here, by the
        // collision it produces.
        UnsupportedProjectKind[] kinds = Enum.GetValues<UnsupportedProjectKind>();

        kinds.Select(UnsupportedProjectsNotice.Reason)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(
                kinds.Length,
                "two UnsupportedProjectKinds read the same, which means one of them fell through Reason's "
                + "fallback arm and is being described as another kind of project on every surface. Give the "
                + "new kind its own sentence in UnsupportedProjectsNotice.Reason.");
    }

    [Fact]
    public void Reason_EveryDeclaredKind_CarriesNoEmDashOfItsOwn()
    {
        // Entry joins on an em dash, so a reason carrying a second would put two in one line. Over every kind
        // rather than the two named above, because the next kind added is where it would happen.
        foreach (UnsupportedProjectKind kind in Enum.GetValues<UnsupportedProjectKind>())
            UnsupportedProjectsNotice.Reason(kind)
                .ShouldNotContain("—", $"{kind}'s reason carries an em dash, and Entry already joins on one");
    }

    [Fact]
    public void Entry_AProjectAndItsReason_JoinsThemTheOneWayEverySurfaceShowsThem()
    {
        UnsupportedProjectsNotice.Entry("Shared/Shared.shproj", "a shared project")
            .ShouldBe("Shared/Shared.shproj — a shared project");
    }

    [Fact]
    public void Relative_ProjectsInsideAndOutsideTheSolutionDirectory_AreShownRelativeAndComposedWithTheirReasons()
    {
        // A machine path in a skip reason is a path no golden can pin and no reader can copy, so every
        // project is shown from the solution directory — including one above it, which a solution declaring a
        // project beside rather than under itself produces. The reason arrives already attached: a caller
        // that had to add it would be a second author for this file's sentence.
        string solutionDirectory = Path.Combine(Path.GetTempPath(), "unsupported-projects-tests", "solution");
        UnsupportedProject[] projects =
        [
            new(
                Path.Combine(solutionDirectory, "PolyglotApp.Fs", "PolyglotApp.Fs.fsproj"),
                UnsupportedProjectKind.NotCsharp),
            new(Path.Combine(solutionDirectory, "..", "Shared", "Shared.shproj"), UnsupportedProjectKind.SharedProject)
        ];

        IReadOnlyList<string> shown = UnsupportedProjectsNotice.Relative(projects, solutionDirectory);

        shown.ShouldBe([
            "PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project",
            "../Shared/Shared.shproj — a shared project, compiled into the projects that import it"
        ]);
    }

    [Fact]
    public void CheckStamp_TwoProjects_LedeStatesNoCauseAndEachEntryStatesItsOwn()
    {
        // The lede scopes and states no cause: a cause stated there would duplicate what every entry beneath
        // it says, and be false about the second one.
        string stamp = UnsupportedProjectsNotice.CheckStamp(TwoProjects);

        stamp.ShouldBe(
            "2 projects the solution declares were not surveyed:\n"
            + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
            + "  Shared/Shared.shproj — a shared project, compiled into the projects that import it\n"
            + "The verdict below covers only the projects this product can read, so a clean result here is "
            + "not a clean solution.");
    }

    [Fact]
    public void StatusStamp_TwoProjects_SaysTheCountsReadLowAndAlwaysWill()
    {
        // Unlike a filter's narrowing this zero is permanent, which is the whole difference between the two
        // tails: no later run of this product will find debt in these projects to burn down.
        string stamp = UnsupportedProjectsNotice.StatusStamp(TwoProjects);

        stamp.ShouldBe(
            "2 projects the solution declares were not surveyed:\n"
            + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
            + "  Shared/Shared.shproj — a shared project, compiled into the projects that import it\n"
            + "The burndown below counts only the projects this product can read: these contribute no "
            + "violations and never will, so every count reads low by whatever they hold.");
    }

    [Fact]
    public void AdapterSkip_TwoProjects_KeepsTheVerdictsAndOffersNoRemedy()
    {
        // The narrowing twin ends by naming what to run instead. This one cannot, and must not invent one:
        // no argument to this run would have widened it.
        string skip = UnsupportedProjectsNotice.AdapterSkip(TwoProjects);

        skip.ShouldBe(
            "2 projects the solution declares were not surveyed:\n"
            + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
            + "  Shared/Shared.shproj — a shared project, compiled into the projects that import it\n"
            + "Rule verdicts come from the projects this product can read, but a test by this name cannot "
            + "pass while the solution declares projects the model never held.");
    }

    [Fact]
    public void CheckStamp_OneProject_SwitchesTheSubjectToTheSingular()
    {
        // One unsupported project is the commonest case there is — a polyglot solution usually holds a
        // single .fsproj — and "1 projects ... were not surveyed" is the sentence a reader stops trusting.
        UnsupportedProjectsNotice.CheckStamp(["PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project"])
            .ShouldStartWith("1 project the solution declares was not surveyed:\n");
    }

    [Fact]
    public void AdapterSkip_BesideANarrowingOne_ComposesAsTwoBlocksTheAdapterCanJoin()
    {
        // A solution can be both filtered and polyglot, and the adapter states both causes rather than the
        // first one it finds. Pinned here rather than on a bed, because a fixture that is both would be a
        // whole second solution to state what these two strings already do.
        string narrowed = NarrowedUniverseNotice.AdapterSkip("BillingOnly.slnf", ["MyApp.Web/MyApp.Web.csproj"]);
        string unsupported = UnsupportedProjectsNotice.AdapterSkip(
            ["PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project"]);

        string composed = string.Join("\n", narrowed, unsupported);

        composed.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 1 project the solution declares was not checked.\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "Rule verdicts come from the projects that loaded, but a test by this name cannot pass while "
            + "declared projects went unchecked; run the solution the filter references for the whole "
            + "answer.\n"
            + "1 project the solution declares was not surveyed:\n"
            + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
            + "Rule verdicts come from the projects this product can read, but a test by this name cannot "
            + "pass while the solution declares projects the model never held.");
    }
}
