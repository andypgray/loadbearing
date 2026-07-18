// A *.cs that lives inside MyApp.Domain's project cone on disk but is excluded from compilation by the
// <Compile Remove="Snippets/**/*.cs" /> in MyApp.Domain.csproj. It exists to reproduce the H1 defect: a
// file the cone scan finds but that never enters the compiled model. Its type is deliberately given a loud,
// unmistakable name so that if the exclusion ever regressed and it leaked into extraction, the many MyApp
// goldens that pin the model would fail on sight rather than drift silently.
namespace MyApp.Domain.Snippets;

public sealed class ExcludedScratchTypeMustNeverAppearInTheModel
{
    public int Answer => 42;
}
