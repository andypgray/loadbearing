using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The spec-resolution convention core (ratified decision 1) over plain tuples — no workspace
///     needed: the unique project referencing <c>Zphil.LoadBearing.dll</c> wins; zero and many are
///     loud errors; a missing built output is a loud error pointing at <c>dotnet build</c>.
/// </summary>
public sealed class SpecResolverTests
{
    private const string CoreDll = "C:/pkgs/Zphil.LoadBearing.dll";

    [Fact]
    public void ResolveConventionProject_UniqueReferencingProject_IsChosen()
    {
        SpecProjectCandidate chosen = SpecResolver.ResolveConventionProject([
            Candidate("MyApp.Web", "C:/pkgs/Newtonsoft.Json.dll"),
            Candidate("MyApp.Arch", CoreDll)
        ]);

        chosen.Name.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveConventionProject_ProjectReferenceOutputPath_IsChosen()
    {
        // The source-checkout shape: the contract library arrives as a ProjectReference, so the
        // candidate's reference paths carry the referenced project's OUTPUT path rather than a package
        // DLL path (candidate construction concatenates both shapes — the Phase 8 derive walk caught
        // the P2P blind spot).
        SpecProjectCandidate chosen = SpecResolver.ResolveConventionProject([
            Candidate("MyApp.Web", "C:/pkgs/Newtonsoft.Json.dll"),
            Candidate("MyApp.Arch", "C:/repo/src/Zphil.LoadBearing/bin/Debug/netstandard2.0/Zphil.LoadBearing.dll")
        ]);

        chosen.Name.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveConventionProject_NoReferencingProject_ThrowsUserError()
    {
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.ResolveConventionProject([Candidate("MyApp.Web", "C:/pkgs/Newtonsoft.Json.dll")]));

        error.Message.ShouldContain("No spec project found");
    }

    [Fact]
    public void ResolveConventionProject_MultipleReferencingProjects_ThrowsUserErrorListingThem()
    {
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.ResolveConventionProject([
                Candidate("Arch.One", CoreDll),
                Candidate("Arch.Two", CoreDll)
            ]));

        error.Message.ShouldContain("Arch.One");
        error.Message.ShouldContain("Arch.Two");
    }

    [Fact]
    public void RequireBuiltOutput_MissingFile_ThrowsUserErrorPointingAtBuild()
    {
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.RequireBuiltOutput("MyApp.Arch", "C:/nope/does-not-exist.dll"));

        error.Message.ShouldContain("Build the solution first");
    }

    [Fact]
    public void RequireBuiltOutput_NullOutput_ThrowsUserError()
    {
        Should.Throw<UserErrorException>(() => SpecResolver.RequireBuiltOutput("MyApp.Arch", null));
    }

    private static SpecProjectCandidate Candidate(string name, string referencePath)
    {
        return new SpecProjectCandidate(name, [referencePath], $"C:/out/{name}.dll");
    }
}