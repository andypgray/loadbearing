using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     <see cref="MultiTargetedProjects.Of" /> over the MSBuild-free fast path: which projects a merged model
///     says arrived as several compilations, their frameworks, and the one whose facts the types they share
///     ended up carrying.
/// </summary>
/// <remarks>
///     It is pinned here rather than at a composer because here is where the logic lives. The slot it fills
///     is documented as the machine-readable half of the merge notes — both from one merge, on one read — and
///     a projection each composer wrote for itself is exactly how that promise came apart: the CLI kept it
///     and the xUnit adapter passed an empty list while holding the model the answer comes off.
/// </remarks>
public sealed class MultiTargetedProjectsTests
{
    [Fact]
    public void Of_AProjectThatCompiledTwice_NamesItsFrameworksAndTheWinner()
    {
        // Arrange — one project name, two compilations, both declaring the same type: the collapse the slot
        // exists to state, and the one case the merge also raises a note for.
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([Framework("net10.0"), Framework("netstandard2.0")]);

        // Act
        IReadOnlyList<MultiTargetedProject> projects = MultiTargetedProjects.Of(model);

        // Assert
        MultiTargetedProject shared = projects.ShouldHaveSingleItem();
        shared.Project.ShouldBe("Shared");
        shared.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        shared.FactsFollow.ShouldBe("net10.0");
    }

    [Fact]
    public void Of_AProjectWhoseFrameworksShareNoType_NamesThemAndNoWinner()
    {
        // Arrange — two frameworks of one project declaring disjoint types, the shape a #if-guarded class
        // makes. Nothing collapsed, so nothing was displaced and naming a winner would be false about both
        // of them. This is the entry that is wider than the notes: the merge raises none here, and the
        // project still compiled more than once.
        CompilationInput legacy = CompilationFactory.Compile("Shared", ("Legacy.cs", """
                                                                                     namespace Shared { public class LegacyOnly {} }
                                                                                     """)) with
        {
            TargetFramework = "netstandard2.0"
        };
        CompilationInput modern = CompilationFactory.Compile("Shared", ("Modern.cs", """
                                                                                     namespace Shared { public class ModernOnly {} }
                                                                                     """)) with
        {
            TargetFramework = "net10.0"
        };
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([legacy, modern]);

        // Act
        IReadOnlyList<MultiTargetedProject> projects = MultiTargetedProjects.Of(model);

        // Assert
        MultiTargetedProject shared = projects.ShouldHaveSingleItem();
        shared.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        shared.FactsFollow.ShouldBeNull();
    }

    [Fact]
    public void Of_ASingleTargetedProject_SaysNothingAtAll()
    {
        // The case every project of most solutions is in, and the one the omit-when-empty rendering rests
        // on: a quiet solution's documents stay byte-identical to the ones they were.
        CodebaseModel model = CompilationFactory.Extract("Plain", ("Plain.cs", """
                                                                               namespace Plain { public class Thing {} }
                                                                               """));

        MultiTargetedProjects.Of(model)
            .ShouldBeEmpty();
    }

    private static CompilationInput Framework(string targetFramework)
    {
        return CompilationFactory.Compile("Shared", ("Widget.cs", """
                                                                  namespace Shared { public class Widget {} }
                                                                  """)) with
        {
            TargetFramework = targetFramework
        };
    }
}
