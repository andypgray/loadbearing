using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The human survey's project roster, over a universe the MyApp fixture cannot supply: one project the
///     solution declares, one passenger a <c>ProjectReference</c> dragged in, and one nothing was read
///     about. <see cref="GraphCommandTests" /> pins the whole document against the real fixture; this pins
///     the one line shape that fixture has no example of, because all three of its projects are declared.
/// </summary>
public sealed class GraphFormatterTests
{
    [Fact]
    public void Lines_MixedSolutionMembership_AnnotatesOnlyThePassenger()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — the annotation sits between the name and the em-dash, so the rest of the line reads
        // exactly as every other project's does and the roster still scans as one column of names. The
        // unread project reads as an ordinary one: the fail-open contract at the surface, since an
        // unreadable solution file must not turn every project into a reported passenger.
        Roster(lines)
            .ShouldBe([
                "  Acme.App — 1 type; references: Acme.Passenger",
                "  Acme.Passenger (not a solution member) — 1 type; references: (none)",
                "  Acme.Unread — 1 type; references: (none)"
            ]);
    }

    [Fact]
    public void Lines_NoUnsupportedProjects_KeepsTheSectionReadingNone()
    {
        // The formatter's standing rule, applied to the section whose absence WAS the defect: a section
        // that appears only when it has content is one a reader never learns to look for, so an all-C#
        // solution says outright that nothing went unread rather than saying nothing at all.
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full, []);

        Section(lines, "Projects the solution declares that this survey could not read:")
            .ShouldBe(["  (none)"]);
    }

    [Fact]
    public void Lines_UnsupportedProjects_ReadTheReasonTheDocumentCarries()
    {
        // The reason is composed once, on the shared trust stamp, and arrives here already paired with its
        // path — so this line and the document's own entry cannot disagree about what the run could read.
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full,
            [new UnsupportedProjectStamp("Acme.Signals/Acme.Signals.fsproj", "not a C# project")]);

        Section(lines, "Projects the solution declares that this survey could not read:")
            .ShouldBe(["  Acme.Signals/Acme.Signals.fsproj — not a C# project"]);
    }

    [Fact]
    public void Lines_GeneratedTypes_QualifyTheProjectCountAndEachNamespace()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            GeneratedTypeSummary(), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — three namespace shapes in one inventory. A wholly generated namespace reads "all
        // generated" rather than repeating the count it just gave, because that is the shape a reader must
        // catch: it is the one namespace here that must never become a layer glob. A namespace with none
        // says nothing at all, so the qualifier's presence is itself the signal.
        Roster(lines)
            .ShouldBe(["  Acme.Web — 5 types (3 generated); references: (none)"]);
        Section(lines, "Namespaces:")
            .ShouldBe(["  Acme.Web: Acme.Views (2, all generated), Acme.Web (2, 1 generated), Acme.Plain (1)"]);
    }

    [Fact]
    public void Lines_NoGeneratedTypes_LeaveEveryLineExactlyAsItWas()
    {
        // The negative that keeps every existing golden byte-identical: a solution with no generator output
        // must render the survey it rendered before the qualifier existed.
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full, []);

        Roster(lines)
            .ShouldAllBe(line => !line.Contains("generated", StringComparison.Ordinal));
        Section(lines, "Namespaces:")
            .ShouldAllBe(line => !line.Contains("generated", StringComparison.Ordinal));
    }

    // Hand-built rather than extracted: the subject here is the LINE, and the three namespace shapes it has
    // to spell are easier to read as literals than as the generator run that would produce them.
    // GraphSummarizerTests covers the counting itself, over a real generator.
    private static GraphSummary GeneratedTypeSummary()
    {
        ProjectSummary web = new(
            "Acme.Web",
            [],
            5,
            3,
            [
                new NamespaceCount("Acme.Views", 2, 2),
                new NamespaceCount("Acme.Web", 2, 1),
                new NamespaceCount("Acme.Plain", 1, 0)
            ]);

        return new GraphSummary([web], [], [], [], []);
    }

    // One section: the lines between its heading and the blank line closing it.
    private static IReadOnlyList<string> Section(IReadOnlyList<string> lines, string heading)
    {
        return lines
            .SkipWhile(line => !line.Equals(heading, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.Length > 0)
            .ToList();
    }

    // The roster block alone: the lines between the "Projects (n):" header and the blank line closing it.
    private static IReadOnlyList<string> Roster(IReadOnlyList<string> lines)
    {
        return lines
            .SkipWhile(line => !line.StartsWith("Projects (", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.Length > 0)
            .ToList();
    }

    // App (declared) references Passenger (undeclared); Unread was extracted with no membership at all.
    private static GraphSummary MixedMembershipSummary()
    {
        CompilationInput passenger = CompilationFactory.Compile("Acme.Passenger", ("Passenger.cs", """
                                                                                                   namespace Acme.Passenger;
                                                                                                   public class Service {}
                                                                                                   """)) with
        {
            SolutionMember = false
        };
        CompilationInput app = CompilationFactory.CompileReferencing(
                "Acme.App", passenger.Compilation, "Acme.Passenger", ("App.cs", """
                                                                                namespace Acme.App;
                                                                                public class Client {}
                                                                                """)) with
            {
                SolutionMember = true
            };
        CompilationInput unread = CompilationFactory.Compile("Acme.Unread", ("Unread.cs", """
                                                                                          namespace Acme.Unread;
                                                                                          public class Thing {}
                                                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([app, passenger, unread]);
        return GraphSummarizer.Summarize(model);
    }
}
