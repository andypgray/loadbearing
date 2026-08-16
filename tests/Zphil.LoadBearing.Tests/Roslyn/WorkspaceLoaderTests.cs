using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The one piece of <see cref="WorkspaceLoader" /> that pins without a workspace: the refusal a filter
///     that cannot be read reaches the user as. Roslyn signals every filter fault the same way — a bare
///     exception carrying "Failed to load solution filter" and nothing else — so this sentence is the entire
///     difference between a stack trace and an actionable answer, and it has to name both the file and what
///     a well-formed filter looks like. The end-to-end route to it is
///     <see cref="Zphil.LoadBearing.Tests.Cli.FilteredSolutionE2ETests" />'s malformed-filter row.
/// </summary>
public sealed class WorkspaceLoaderTests
{
    [Fact]
    public void UnreadableFilterMessage_AFilterPath_NamesTheFileThenWhatAWellFormedFilterIs()
    {
        string message = WorkspaceLoader.UnreadableFilterMessage("/repo/Broken.slnf");

        message.ShouldBe(
            "Could not read the solution filter '/repo/Broken.slnf'.\n"
            + "A filter must be well-formed JSON whose 'solution' path resolves to a .sln or .slnx, and "
            + "every project it lists must be a member of that solution.");
    }
}
