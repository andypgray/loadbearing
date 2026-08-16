using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
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
        IReadOnlyList<string> lines = GraphFormatter.Lines(MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full);

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
