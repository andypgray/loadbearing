using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Discovery tests. Each runs against a fresh <see cref="Directory.CreateTempSubdirectory(string)" />
///     root and drives the walk-up via the <c>workingDirectory</c> parameter — the current directory
///     is never mutated. The <see cref="LoadBearingEnvVars.SolutionPath" /> env var is cleared per
///     test (ctor) and restored (Dispose); xUnit runs methods in one class serially, so the one test
///     that sets it cannot race the others.
/// </summary>
public sealed class SolutionDiscoveryTests : IDisposable
{
    private readonly string? _originalEnvValue;
    private readonly string _tempRoot;

    public SolutionDiscoveryTests()
    {
        _tempRoot = Directory.CreateTempSubdirectory("loadbearing-discovery-").FullName;
        _originalEnvValue = Environment.GetEnvironmentVariable(LoadBearingEnvVars.SolutionPath);
        Environment.SetEnvironmentVariable(LoadBearingEnvVars.SolutionPath, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(LoadBearingEnvVars.SolutionPath, _originalEnvValue);
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
    }

    private string CreateDir(params string[] segments)
    {
        string dir = Path.Combine([_tempRoot, .. segments]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateSln(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void DiscoverSolution_ExplicitPath_ReturnsFullPath()
    {
        string slnPath = CreateSln(_tempRoot, "Explicit.sln");

        string result = SolutionDiscovery.DiscoverSolution(slnPath);

        result.ShouldBe(Path.GetFullPath(slnPath));
    }

    [Fact]
    public void DiscoverSolution_ExplicitPathMissing_Throws()
    {
        string missing = Path.Combine(_tempRoot, "DoesNotExist.sln");

        var ex = Should.Throw<FileNotFoundException>(() => SolutionDiscovery.DiscoverSolution(missing));

        ex.Message.ShouldContain("not found");
    }

    [Fact]
    public void DiscoverSolution_EnvVarSet_TakesPrecedenceOverWalkUp()
    {
        string envSln = CreateSln(_tempRoot, "FromEnvVar.sln");
        string cwdDir = CreateDir("has-its-own-sln");
        CreateSln(cwdDir, "WalkUpWouldFindThis.sln");
        Environment.SetEnvironmentVariable(LoadBearingEnvVars.SolutionPath, envSln);

        string result = SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir);

        result.ShouldBe(Path.GetFullPath(envSln));
    }

    [Fact]
    public void DiscoverSolution_SingleSolutionInAncestor_FoundByWalkUp()
    {
        string ancestorDir = CreateDir("ancestor");
        string ancestorSln = CreateSln(ancestorDir, "Found.sln");
        string cwdDir = CreateDir("ancestor", "src", "app");

        string result = SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir);

        result.ShouldBe(ancestorSln);
    }

    [Fact]
    public void DiscoverSolution_MultipleSolutionsInDirectory_ThrowsListingFiles()
    {
        string cwdDir = CreateDir("ambiguous");
        CreateSln(cwdDir, "Alpha.sln");
        CreateSln(cwdDir, "Beta.slnx");

        var ex = Should.Throw<InvalidOperationException>(() => SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir));

        ex.Message.ShouldContain("Multiple solution files found");
        ex.Message.ShouldContain("Alpha.sln");
        ex.Message.ShouldContain("Beta.slnx");
    }

    [Fact]
    public void DiscoverSolution_NoSolutionAnywhere_ThrowsWithEnvVarHint()
    {
        string cwdDir = CreateDir("empty", "deep", "nested");

        // Guard: discovery walks parents to the drive root, so a stray solution file in ANY ancestor
        // of the temp dir would make this find one (and not throw). Fail loudly on a polluted
        // environment rather than passing — or throwing — for the wrong reason.
        for (DirectoryInfo? dir = new(cwdDir); dir is not null; dir = dir.Parent)
        {
            string[] solutionFiles;
            try
            {
                solutionFiles = Directory.EnumerateFiles(dir.FullName, "*.sln")
                    .Concat(Directory.EnumerateFiles(dir.FullName, "*.slnf"))
                    .Concat(Directory.EnumerateFiles(dir.FullName, "*.slnx"))
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            solutionFiles.ShouldBeEmpty($"Stray solution file under ancestor '{dir.FullName}' makes this test meaningless — clean it.");
        }

        var ex = Should.Throw<InvalidOperationException>(() => SolutionDiscovery.DiscoverSolution(workingDirectory: cwdDir));

        ex.Message.ShouldContain("No .sln, .slnf or .slnx file found");
        ex.Message.ShouldContain(LoadBearingEnvVars.SolutionPath);
    }

    [Fact]
    public void DiscoverSolution_SlnxAndSlnfExtensions_AreMatched()
    {
        string slnxDir = CreateDir("slnx-only");
        string slnxPath = CreateSln(slnxDir, "Modern.slnx");
        SolutionDiscovery.DiscoverSolution(workingDirectory: slnxDir).ShouldBe(slnxPath);

        string slnfDir = CreateDir("slnf-only");
        string slnfPath = CreateSln(slnfDir, "Filtered.slnf");
        SolutionDiscovery.DiscoverSolution(workingDirectory: slnfDir).ShouldBe(slnfPath);
    }
}