using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     <see cref="DiagramComposer" /> facts: the two-fence body shape, the survey half staying
///     byte-identical to what <see cref="GraphDiagramRenderer" /> produces on its own, and the scope
///     options reaching the survey and nothing else. This is the seam the <c>render</c> command and the
///     committed-artifact drift gate share, so what it composes is what both of them mean.
/// </summary>
public sealed class DiagramComposerTests
{
    private const string SolutionName = "Test.slnx";

    private const string SpecName = "Test.ArchSpec";

    [Fact]
    public void Compose_ASurveyAndALaw_IsTwoFencesEachWithItsOwnCaption()
    {
        // Arrange
        GraphSummary summary = TwoProjectSummary();
        ArchitectureModel model = LawModel();

        // Act
        string body = DiagramComposer.Compose(summary, SolutionName, model, SpecName);

        // Assert — the codebase question first, then the spec's answer to it, each fenced and each
        // announcing its own source in an accessible title.
        Fences(body)
            .ShouldBe(2);
        body.ShouldContain($"    accTitle: Codebase survey: {SolutionName}");
        body.ShouldContain($"    accTitle: Architecture law: {SpecName}");
        body.IndexOf("Codebase survey:", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("Architecture law:", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_TheSurveyHalf_IsByteIdenticalToTheSurveyRenderedAlone()
    {
        // Arrange
        GraphSummary summary = TwoProjectSummary();
        ArchitectureModel model = LawModel();

        // Act
        string body = DiagramComposer.Compose(summary, SolutionName, model, SpecName);

        // Assert — the law arrives beside the survey and never inside it, so a codebase that has not
        // changed re-renders its survey byte for byte.
        string survey = GraphDiagramRenderer.Block(summary, SolutionName);
        string law = LawDiagramRenderer.Block(model, SpecName);
        body.ShouldStartWith(survey);
        body.ShouldBe(survey + "\n\n" + law);
    }

    [Fact]
    public void Compose_AScope_NarrowsTheSurveyAndLeavesTheLawWhole()
    {
        // Arrange
        GraphSummary summary = TwoProjectSummary();
        ArchitectureModel model = LawModel();

        // Act
        string body = DiagramComposer.Compose(summary, SolutionName, model, SpecName, new DiagramScope(["App"], []));

        // Assert — the survey loses the project the scope dropped; the law fence is drawn from the spec
        // rather than from the project graph, so a project-name filter has nothing to say about it, and
        // the unscoped fence can name places the survey above it no longer shows.
        body.ShouldNotContain("p_Lib");
        body.ShouldContain("p_App[\"App\"]");
        body.ShouldEndWith(LawDiagramRenderer.Block(model, SpecName));
    }

    [Fact]
    public void Compose_APassenger_DropsFromTheSurveyAndLeavesTheLawWhole()
    {
        // Arrange — the same independence as the scoped case, on the fence's own default rather than a
        // flag: Lib is a passenger, and the law is written about Lib.
        GraphSummary summary = TwoProjectSummary(libMembership: false, appMembership: true);
        ArchitectureModel model = LawModel();

        // Act — no scope argument at all.
        string body = DiagramComposer.Compose(summary, SolutionName, model, SpecName);

        // Assert — the survey drops it, and the law fence still names it, because LawDiagramRenderer takes
        // no scope and draws from the spec rather than from the project graph.
        body.ShouldNotContain("p_Lib[");
        body.ShouldContain("p_App[\"App\"]");
        body.ShouldEndWith(LawDiagramRenderer.Block(model, SpecName));
    }

    // App references Lib and touches it — the smallest survey with an edge in it. Membership defaults to
    // unread, which draws, so the cases predating the declared-members fence are unaffected by it.
    private static GraphSummary TwoProjectSummary(bool? libMembership = null, bool? appMembership = null)
    {
        CompilationInput lib = CompilationFactory.Compile("Lib", ("Lib.cs", """
                                                                            namespace Lib;
                                                                            public class Service {}
                                                                            """)) with
        {
            SolutionMember = libMembership
        };
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib", ("App.cs", """
                                                                                                               namespace App;
                                                                                                               public class Client { public Lib.Service S; }
                                                                                                               """)) with
        {
            SolutionMember = appMembership
        };

        return GraphSummarizer.Summarize(CodebaseExtractor.ExtractFromCompilations([lib, app]));
    }

    // A law naming a place the scoped survey drops, so the two halves are visibly independent.
    private static ArchitectureModel LawModel()
    {
        return Checker.Model(arch =>
            arch.Rule("layering/lib-is-a-leaf")
                .Enforce(arch.Namespace("Lib.*").MustNotReference(arch.Namespace("App.*")))
                .Because("A library that reaches back into its caller is not a library."));
    }

    private static int Fences(string body)
    {
        return body.Split('\n')
            .Count(line => line == "```mermaid");
    }
}
