using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Solutions;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The exclusion walk over synthetic project graphs — no workspace, no MSBuild. The pins are the two
///     shapes the product actually has (a spec that references the app it governs; a spec that references
///     the libraries it dogfoods) plus the fallback that keeps an unreadable solution file from emptying the
///     checked universe. Membership tests hit the disk only through
///     <see cref="SpecExclusion.TryReadDeclaredMembers" />, which has its own cases at the bottom.
/// </summary>
public sealed class SpecExclusionTests : IDisposable
{
    // Canonical by construction — TestTempRoot resolves the temp base through PathCanonicalizer once per
    // run — which is what lets the declared-member assertions compare against a path built from it.
    private readonly TempDirectory _temp = TestTempRoot.Fresh("spec-exclusion");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Compute_SpecReferencesDeclaredAppAndUndeclaredContract_ExcludesSpecAndContractOnly()
    {
        // The example shape: the ArchSpec references the application project (a declared member — and the
        // subject matter) and the contract library from outside the solution (a passenger MSBuild loaded).
        var projects = new SpecExclusionProject[]
        {
            new("Meridian.Interchange.ArchSpec", "/repo/arch/ArchSpec.csproj", ["Meridian.Interchange", "Zphil.LoadBearing"]),
            new("Meridian.Interchange", "/repo/src/App.csproj", []),
            new("Zphil.LoadBearing", "/elsewhere/Core.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects,
            Declared("/repo/arch/ArchSpec.csproj", "/repo/src/App.csproj"),
            "Meridian.Interchange.ArchSpec");

        excluded.ShouldBe(["Meridian.Interchange.ArchSpec", "Zphil.LoadBearing"]);
    }

    [Fact]
    public void Compute_SpecReferencesOnlyDeclaredMembers_ExcludesJustTheSpecProject()
    {
        // The self-check shape, and the regression pin: this repo's spec references the two projects it
        // dogfoods. Both are declared members, so the subtraction leaves them under law.
        var projects = new SpecExclusionProject[]
        {
            new("Zphil.LoadBearing.ArchSpec", "/repo/arch/ArchSpec.csproj", ["Zphil.LoadBearing", "Zphil.LoadBearing.Roslyn"]),
            new("Zphil.LoadBearing", "/repo/src/Core.csproj", []),
            new("Zphil.LoadBearing.Roslyn", "/repo/src/Roslyn.csproj", ["Zphil.LoadBearing"])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects,
            Declared("/repo/arch/ArchSpec.csproj", "/repo/src/Core.csproj", "/repo/src/Roslyn.csproj"),
            "Zphil.LoadBearing.ArchSpec");

        excluded.ShouldBe(["Zphil.LoadBearing.ArchSpec"]);
    }

    [Fact]
    public void Compute_UndeclaredPlumbingBehindUndeclaredPlumbing_ExcludesTheWholeChain()
    {
        // The rule-pack composition shape: the spec references a rule pack, which references the
        // contract. Neither is declared, and the walk is transitive, so both leave the universe.
        var projects = new SpecExclusionProject[]
        {
            new("App.ArchSpec", "/repo/arch/ArchSpec.csproj", ["Guidance.Pack", "App"]),
            new("Guidance.Pack", "/elsewhere/Pack.csproj", ["Zphil.LoadBearing"]),
            new("Zphil.LoadBearing", "/elsewhere/Core.csproj", []),
            new("App", "/repo/src/App.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects, Declared("/repo/arch/ArchSpec.csproj", "/repo/src/App.csproj"), "App.ArchSpec");

        excluded.ShouldBe(["App.ArchSpec", "Guidance.Pack", "Zphil.LoadBearing"]);
    }

    [Fact]
    public void Compute_ProjectOutsideTheSpecsClosure_StaysInTheUniverse()
    {
        // Only what the spec reaches is plumbing. An undeclared project nobody in the closure references is
        // some other project's passenger and none of this walk's business.
        var projects = new SpecExclusionProject[]
        {
            new("App.ArchSpec", "/repo/arch/ArchSpec.csproj", ["App"]),
            new("App", "/repo/src/App.csproj", []),
            new("Somebody.Elses.Passenger", "/elsewhere/Other.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects, Declared("/repo/arch/ArchSpec.csproj", "/repo/src/App.csproj"), "App.ArchSpec");

        excluded.ShouldBe(["App.ArchSpec"]);
    }

    [Fact]
    public void Compute_UnknownProjectPath_CountsAsDeclared()
    {
        // Nothing disproves membership, and a false negative silently shrinks the codebase under law — so
        // the unknown-path case errs towards keeping the project.
        var projects = new SpecExclusionProject[]
        {
            new("App.ArchSpec", "/repo/arch/ArchSpec.csproj", ["Mystery"]),
            new("Mystery", null, [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(projects, Declared("/repo/arch/ArchSpec.csproj"), "App.ArchSpec");

        excluded.ShouldBe(["App.ArchSpec"]);
    }

    [Fact]
    public void Compute_UnreadableMembership_FallsBackToTheSpecProjectAlone()
    {
        // Never fail open into an empty universe: with no membership to subtract, the answer is the
        // behaviour that predates the walk. Note what the alternative would be — every one of these
        // projects is undeclared under a null set.
        var projects = new SpecExclusionProject[]
        {
            new("App.ArchSpec", "/repo/arch/ArchSpec.csproj", ["App", "Zphil.LoadBearing"]),
            new("App", "/repo/src/App.csproj", []),
            new("Zphil.LoadBearing", "/elsewhere/Core.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(projects, null, "App.ArchSpec");

        excluded.ShouldBe(["App.ArchSpec"]);
    }

    [Fact]
    public void Compute_ReferenceCycle_Terminates()
    {
        // MSBuild forbids project-reference cycles, but the walk must not depend on that to halt.
        var projects = new SpecExclusionProject[]
        {
            new("App.ArchSpec", "/repo/arch/ArchSpec.csproj", ["Left"]),
            new("Left", "/elsewhere/Left.csproj", ["Right"]),
            new("Right", "/elsewhere/Right.csproj", ["Left", "App.ArchSpec"])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(projects, Declared("/repo/arch/ArchSpec.csproj"), "App.ArchSpec");

        excluded.ShouldBe(["App.ArchSpec", "Left", "Right"]);
    }

    [Fact]
    public void Compute_MultiTargetedProjectDeclaredUnderOneEntry_IsDeclaredForAll()
    {
        // A multi-targeted project arrives once per framework under one name; declared by any entry is
        // declared, and the reference edges union across entries.
        var projects = new SpecExclusionProject[]
        {
            new("App.ArchSpec", "/repo/arch/ArchSpec.csproj", ["App"]),
            new("App", "/repo/src/App.csproj", ["Zphil.LoadBearing"]),
            new("App", null, []),
            new("Zphil.LoadBearing", "/elsewhere/Core.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects, Declared("/repo/arch/ArchSpec.csproj", "/repo/src/App.csproj"), "App.ArchSpec");

        excluded.ShouldBe(["App.ArchSpec", "Zphil.LoadBearing"]);
    }

    // ── membership reading: what the parser owns, and what it must refuse ──────────────────────────────────

    [Fact]
    public void TryReadDeclaredMembers_Slnx_ReturnsCanonicalizedDeclaredMembers()
    {
        string solutionPath = _temp.PathOf("App.slnx");
        File.WriteAllText(solutionPath, """
                                        <Solution>
                                          <Project Path="src/App/App.csproj" />
                                        </Solution>
                                        """);

        IReadOnlySet<string>? members = SpecExclusion.TryReadDeclaredMembers(solutionPath);

        members.ShouldNotBeNull();
        members.ShouldContain(_temp.PathOf("src", "App", "App.csproj"));
    }

    [Fact]
    public void TryReadDeclaredMembers_SolutionFilter_ReadsThroughToTheFilteredSolutionsWholeMembership()
    {
        // A filter used to degrade to null here, which subtracted the spec's whole closure. It now resolves
        // to the solution it points at — and to that solution's WHOLE membership, not the filter's
        // selection: a member left out of the selection is still solution material, so excluding it would
        // silently shrink the codebase under law.
        File.WriteAllText(_temp.PathOf("App.slnx"), """
                                                    <Solution>
                                                      <Project Path="src/App/App.csproj" />
                                                      <Project Path="src/Unselected/Unselected.csproj" />
                                                    </Solution>
                                                    """);
        string solutionPath = _temp.PathOf("Filtered.slnf");
        File.WriteAllText(
            solutionPath,
            """{ "solution": { "path": "App.slnx", "projects": [ "src/App/App.csproj" ] } }""");

        IReadOnlySet<string>? members = SpecExclusion.TryReadDeclaredMembers(solutionPath);

        members.ShouldNotBeNull();
        members.ShouldContain(_temp.PathOf("src", "App", "App.csproj"));
        members.ShouldContain(_temp.PathOf("src", "Unselected", "Unselected.csproj"));
    }

    [Fact]
    public void TryReadDeclaredMembers_MalformedSolutionFilter_ReturnsNullRatherThanAnEmptySet()
    {
        // The "never fail open" pin, kept alive at the one site that can still reach it. Reporting "nothing
        // is declared" for an unreadable filter would subtract the spec's whole closure; null is the fallback.
        string solutionPath = _temp.PathOf("Malformed.slnf");
        File.WriteAllText(solutionPath, "{ this is not json");

        SpecExclusion.TryReadDeclaredMembers(solutionPath)
            .ShouldBeNull();
    }

    [Fact]
    public void TryReadDeclaredMembers_MissingFile_ReturnsNull()
    {
        SpecExclusion.TryReadDeclaredMembers(_temp.PathOf("does-not-exist.slnx"))
            .ShouldBeNull();
    }

    [Fact]
    public void TryReadDeclaredMembers_MalformedSlnx_ReturnsNull()
    {
        // Unparseable XML is unreadable membership, not zero membership.
        string solutionPath = _temp.PathOf("Broken.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"a.csproj\">");

        SpecExclusion.TryReadDeclaredMembers(solutionPath)
            .ShouldBeNull();
    }

    [Fact]
    public void IsDeclaredMember_UnreadableMembershipOrUnknownPath_IsTrue()
    {
        SpecExclusion.IsDeclaredMember(null, "/repo/src/App.csproj")
            .ShouldBeTrue();
        SpecExclusion.IsDeclaredMember(SpecExclusion.CanonicalMemberSet([]), null)
            .ShouldBeTrue();
        SpecExclusion.IsDeclaredMember(SpecExclusion.CanonicalMemberSet([]), "/repo/src/App.csproj")
            .ShouldBeFalse();
    }

    [Fact]
    public void SolutionMembershipOf_ReadMembership_SeparatesDeclaredMembersFromPassengers()
    {
        IReadOnlySet<string> declared = Declared("/repo/src/App.csproj");

        SpecExclusion.SolutionMembershipOf(declared, "/repo/src/App.csproj")
            .ShouldBe(true);
        SpecExclusion.SolutionMembershipOf(declared, "/elsewhere/Core.csproj")
            .ShouldBe(false);
    }

    [Fact]
    public void SolutionMembershipOf_UnreadMembershipOrUnknownPath_IsNullWhereIsDeclaredMemberSaysTrue()
    {
        // The inversion this method exists for, asserted against its sibling on the same inputs: true is the
        // safe answer when the question is what to exclude, and an invented fact when the question is what
        // to report.
        SpecExclusion.SolutionMembershipOf(null, "/repo/src/App.csproj")
            .ShouldBeNull();
        SpecExclusion.SolutionMembershipOf(Declared("/repo/src/App.csproj"), null)
            .ShouldBeNull();
    }

    [Fact]
    public void SolutionMembershipOf_UnownedFormatOrUnparseableSolution_DegradesToNull()
    {
        // Both disk-side fallbacks reach this method as a null set, so the chain from a bad solution file
        // through to an unlabeled project is pinned end to end rather than as two halves nothing joins.
        string unowned = _temp.PathOf("MyApp.csproj");
        File.WriteAllText(unowned, "<Project />");
        string malformed = _temp.PathOf("Unparseable.slnx");
        File.WriteAllText(malformed, "<Solution><Project Path=\"a.csproj\">");

        SpecExclusion.SolutionMembershipOf(SpecExclusion.TryReadDeclaredMembers(unowned), "/repo/src/App.csproj")
            .ShouldBeNull();
        SpecExclusion.SolutionMembershipOf(SpecExclusion.TryReadDeclaredMembers(malformed), "/repo/src/App.csproj")
            .ShouldBeNull();
    }

    private static IReadOnlySet<string> Declared(params string[] csprojPaths)
    {
        return SpecExclusion.CanonicalMemberSet(csprojPaths);
    }
}
