using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The one rule <see cref="EvidenceBlock" /> enforces rather than assembles: a lede ending in a colon
///     promises the block beneath it, so composing one over nothing is a defect at the call site rather than
///     a shorter message. The assembly itself is pinned by its consumers — which is why the type had no test
///     file of its own until the dangling colon reached the field.
/// </summary>
public sealed class EvidenceBlockTests
{
    private const string ColonLede = "The projects that failed to load:";

    [Fact]
    public void Compose_ColonLedeOverNoEvidence_ThrowsRatherThanDanglingTheColon()
    {
        // The shape measured on a published build: the model was incomplete solely because a restore had
        // failed, the refusal quoted the load failures, and that list was empty — so the remedy line landed
        // where the evidence should have been and the whole message read as truncated output. The guard makes
        // that unreachable at every call site rather than at the one it was seen at.
        var error = Should.Throw<ArgumentException>(() =>
            EvidenceBlock.Compose(ColonLede, [], "Build the solution first (dotnet build)."));

        error.ParamName.ShouldBe("evidence");
        error.Message.ShouldContain(ColonLede);
        error.Message.ShouldContain("end the lede in a full stop");
    }

    [Fact]
    public void Compose_FullStopLedeOverNoEvidence_ComposesTheLedeAndTheTailAlone()
    {
        // SolutionDiscovery.NotFoundMessage's shape, and the one call site that legitimately composes over
        // nothing: it withholds its "Solution files one level down:" heading when the walk-up saw none, and
        // ends in a full stop instead. So this is that call site's regression guard as much as this type's —
        // a guard that fired here would be refusing a message that is already right.
        string block = EvidenceBlock.Compose(
            "No .sln, .slnf or .slnx file found in 'C:/repo' or any parent directory.", [],
            "Pass a solution path, or run from a directory beneath one.");

        block.ShouldBe(
            "No .sln, .slnf or .slnx file found in 'C:/repo' or any parent directory.\n"
            + "Pass a solution path, or run from a directory beneath one.");
    }

    [Fact]
    public void Compose_ColonLedeWithEvidence_IndentsItBeneathTheLede()
    {
        // The ordinary shape, unaffected: the guard reads the lede's last character and the evidence count,
        // and nothing else.
        string block = EvidenceBlock.Compose(
            ColonLede, ["C:/repo/one.csproj", "C:/repo/two.csproj"], "Build the solution first (dotnet build).");

        block.ShouldBe(
            ColonLede + "\n"
                      + "  C:/repo/one.csproj\n"
                      + "  C:/repo/two.csproj\n"
                      + "Build the solution first (dotnet build).");
    }
}
