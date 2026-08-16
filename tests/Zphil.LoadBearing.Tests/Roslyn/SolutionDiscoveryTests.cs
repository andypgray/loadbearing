using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Discovery tests. Each runs against a fresh <see cref="TestTempRoot" /> root and drives the walk-up
///     via the <c>workingDirectory</c> parameter — the current directory is never mutated. That root is
///     canonicalized (like <see cref="PathCanonicalizer" /> does at the seam) so the
///     <c>Path.GetFullPath</c> expectations hold even on macOS, where the temp dir sits under a
///     <c>/var</c> → <c>/private/var</c> symlink. The <see cref="LoadBearingEnvVars.SolutionPath" />
///     env var is cleared per test (ctor) and restored (Dispose); xUnit runs methods in one class
///     serially, so the one test that sets it cannot race the others.
/// </summary>
public sealed class SolutionDiscoveryTests : IDisposable
{
    /// <summary>
    ///     What a polluted environment would cost the rows that need the walk-up to find nothing: a stray
    ///     solution file in any ancestor makes discovery find one and not throw, so the row would pass — or
    ///     throw — for the wrong reason.
    /// </summary>
    private const string StrayMakesItMeaningless = "makes this test meaningless — clean it.";

    private readonly ScopedEnvironmentVariable _solutionPathVariable = new(LoadBearingEnvVars.SolutionPath, null);

    private readonly TempDirectory _temp = TestTempRoot.Fresh("discovery");

    public void Dispose()
    {
        _solutionPathVariable.Dispose();
        _temp.Dispose();
    }

    private string CreateDir(params string[] segments)
    {
        string dir = Path.Combine([_temp.Path, .. segments]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void DiscoverSolution_ExplicitPath_ReturnsFullPath()
    {
        string slnPath = SolutionPaths.CreateSln(_temp.Path, "Explicit.sln");

        string result = SolutionDiscovery.DiscoverSolution(slnPath);

        result.ShouldBe(Path.GetFullPath(slnPath));
    }

    [Fact]
    public void DiscoverSolution_ExplicitPathMissing_Throws()
    {
        string missing = _temp.PathOf("DoesNotExist.sln");

        var ex = Should.Throw<FileNotFoundException>(() => SolutionDiscovery.DiscoverSolution(missing));

        ex.Message.ShouldContain("not found");
    }

    [Fact]
    public void DiscoverSolution_EnvVarSet_TakesPrecedenceOverWalkUp()
    {
        string envSln = SolutionPaths.CreateSln(_temp.Path, "FromEnvVar.sln");
        string cwdDir = CreateDir("has-its-own-sln");
        SolutionPaths.CreateSln(cwdDir, "WalkUpWouldFindThis.sln");
        Environment.SetEnvironmentVariable(LoadBearingEnvVars.SolutionPath, envSln);

        string result = SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir);

        result.ShouldBe(Path.GetFullPath(envSln));
    }

    [Fact]
    public void DiscoverSolution_EnvVarPointsToMissingFile_Throws()
    {
        // The env var is set but names a file that does not exist: discovery throws with the env-var-specific
        // message rather than falling through to the walk-up.
        string missing = _temp.PathOf("EnvVarGhost.slnx");
        Environment.SetEnvironmentVariable(LoadBearingEnvVars.SolutionPath, missing);

        var ex = Should.Throw<FileNotFoundException>(() => SolutionDiscovery.DiscoverSolution());

        ex.Message.ShouldContain("points to a file that does not exist");
    }

    [Fact]
    public void DiscoverSolution_SingleSolutionInAncestor_FoundByWalkUp()
    {
        string ancestorDir = CreateDir("ancestor");
        string ancestorSln = SolutionPaths.CreateSln(ancestorDir, "Found.sln");
        string cwdDir = CreateDir("ancestor", "src", "app");

        string result = SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir);

        result.ShouldBe(ancestorSln);
    }

    [Fact]
    public void DiscoverSolution_MultipleSolutionsInDirectory_ThrowsListingFiles()
    {
        string cwdDir = CreateDir("ambiguous");
        SolutionPaths.CreateSln(cwdDir, "Alpha.sln");
        SolutionPaths.CreateSln(cwdDir, "Beta.slnx");

        var ex = Should.Throw<InvalidOperationException>(() => SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir));

        ex.Message.ShouldContain("Multiple solution files found");
        ex.Message.ShouldContain("Alpha.sln");
        ex.Message.ShouldContain("Beta.slnx");
        // The argument comes first, and the MCP client config is named beside it: on that surface the
        // environment variable would have to be set for whatever process launches the client, so the args
        // array is the only fix the reader can actually apply where they are reading.
        ex.Message.ShouldContain("Pass the solution as the argument");
        ex.Message.ShouldContain("MCP client");
        ex.Message.ShouldContain(LoadBearingEnvVars.SolutionPath);
    }

    [Fact]
    public void DiscoverSolution_AFilterBesideItsSolution_ResolvesTheSolution()
    {
        // The layout a filter normally arrives in. It is a view of the solution standing next to it, not a
        // rival candidate, so this directory is not ambiguous — and a refusal naming the filter would have
        // invited deleting it, which would not have helped.
        string cwdDir = CreateDir("filter-beside-its-solution");
        string solution = SolutionPaths.CreateSln(cwdDir, "Alpha.sln");
        SolutionPaths.CreateSln(cwdDir, "BillingOnly.slnf");

        SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir)
            .ShouldBe(solution);
    }

    [Fact]
    public void DiscoverSolution_SeveralSolutionsAndAFilter_RefusesNamingOnlyTheSurvivors()
    {
        // Demotion settles which candidates compete; it never turns two solutions into one answer. The
        // filter is gone from the message because acting on it could not resolve anything.
        string cwdDir = CreateDir("ambiguous-with-a-filter");
        SolutionPaths.CreateSln(cwdDir, "Alpha.sln");
        SolutionPaths.CreateSln(cwdDir, "Beta.slnf");
        SolutionPaths.CreateSln(cwdDir, "Gamma.slnx");

        var ex = Should.Throw<InvalidOperationException>(() => SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir));

        ex.Message.ShouldContain("Alpha.sln");
        ex.Message.ShouldContain("Gamma.slnx");
        ex.Message.ShouldNotContain("Beta.slnf");
    }

    [Fact]
    public void DiscoverSolution_NoSolutionAnywhere_ThrowsNamingBothFixes()
    {
        string cwdDir = CreateDir("empty", "deep", "nested");
        SolutionPaths.ShouldHaveNoSolutionInAnyAncestor(cwdDir, StrayMakesItMeaningless);

        var ex = Should.Throw<InvalidOperationException>(() => SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir));

        ex.Message.ShouldContain("No .sln, .slnf or .slnx file found");
        ex.Message.ShouldContain("Pass the solution as the argument");
        ex.Message.ShouldContain(LoadBearingEnvVars.SolutionPath);
        ex.Message.ShouldNotContain("Solution files one level down"); // nothing was seen, so nothing is listed
    }

    [Fact]
    public void DiscoverSolution_SolutionOnlyOneLevelDown_RefusesButNamesIt()
    {
        // The shape a repository whose solution lives under src\ presents: nothing at the root, so the
        // walk-up climbs past the repository entirely. Discovery still refuses — it never widens the
        // search into a guess — but naming the file turns a dead end into one copy-paste.
        string root = CreateDir("repo");
        SolutionPaths.ShouldHaveNoSolutionInAnyAncestor(root, StrayMakesItMeaningless);
        SolutionPaths.CreateSln(CreateDir("repo", "src"), "Storefront.sln");
        SolutionPaths.CreateSln(CreateDir("repo", "bin"), "StaleBuildOutput.sln");

        var ex = Should.Throw<InvalidOperationException>(() => SolutionDiscovery.DiscoverSolution(workingDirectory: root));

        ex.Message.ShouldContain("No .sln, .slnf or .slnx file found");
        ex.Message.ShouldContain("Solution files one level down:");
        ex.Message.ShouldContain(Path.Combine("src", "Storefront.sln"));
        // Build output is skipped: a solution there is an artefact or a copy, and naming it would send the
        // reader somewhere wrong.
        ex.Message.ShouldNotContain("StaleBuildOutput.sln");
    }

    [Fact]
    public void NearMisses_UnreadableStartDirectory_YieldsNothingRatherThanThrowing()
    {
        // The scan is a courtesy on a path that has already failed, so an I/O error in it must not replace
        // the refusal with an unhandled exception: the reader would lose the message the whole change exists
        // to deliver. A directory that does not exist is the reachable form of that failure.
        string vanished = _temp.PathOf("was-deleted-underneath-us");

        SolutionDiscovery.NearMisses(vanished)
            .ShouldBeEmpty();
    }

    [Fact]
    public void NotFoundMessage_MoreNearMissesThanTheCap_ListsTheCapThenCountsTheRest()
    {
        // Pure over the message static, so the cap and its tail pin without a filesystem to build first.
        string[] nearMisses = Enumerable.Range(1, 7)
            .Select(n => _temp.PathOf("src", $"App{n}.sln"))
            .ToArray();

        string message = SolutionDiscovery.NotFoundMessage(_temp.Path, nearMisses);

        message.ShouldContain(Path.Combine("src", "App5.sln"));
        message.ShouldNotContain("App6.sln");
        message.ShouldContain("... and 2 more.");
    }

    [Fact]
    public void DiscoverSolution_ThroughSymlinkedDirectory_ReturnsCanonicalPath()
    {
        // A real directory with a solution, and a symlink pointing at it. Discovery through the link
        // must return the canonical (symlink-free) path, so the workspace agrees with git's toplevel.
        string realDir = CreateDir("real");
        string slnPath = SolutionPaths.CreateSln(realDir, "Linked.slnx");
        string link = _temp.PathOf("link");
        SymlinkSupport.CreateDirectorySymlink(link, realDir);

        string result = SolutionDiscovery.DiscoverSolution(Path.Combine(link, "Linked.slnx"));

        result.ShouldBe(slnPath);
    }

    [Fact]
    public void DiscoverSolution_SlnxAndSlnfExtensions_AreMatched()
    {
        string slnxDir = CreateDir("slnx-only");
        string slnxPath = SolutionPaths.CreateSln(slnxDir, "Modern.slnx");
        SolutionDiscovery.DiscoverSolution(workingDirectory: slnxDir)
            .ShouldBe(slnxPath);

        // Alone in its directory, so nothing demotes it: a filter is still a solution the walk-up can land on.
        string slnfDir = CreateDir("slnf-only");
        string slnfPath = SolutionPaths.CreateSln(slnfDir, "Filtered.slnf");
        SolutionDiscovery.DiscoverSolution(workingDirectory: slnfDir)
            .ShouldBe(slnfPath);
    }
}
