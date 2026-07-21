using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Top-level statements (a synthesized <c>Program</c> entry point) on the extraction fast path.
///     The investigation hypothesis — that the synthesized <c>Program</c> never enters
///     extraction because <c>FragmentExtractor.Declare</c> drops implicitly-declared types — is
///     <em>disconfirmed</em> on this Roslyn: the simple-program <c>Program</c> is NOT implicitly declared,
///     so it is admitted like any solution type, its top-level-statement references bind to it, and the
///     <c>ReferenceWalker</c> descent boundary already keeps a sibling type declared in the same file from
///     being misattributed to it. These pins hold that passing behavior in place.
/// </summary>
public sealed class TopLevelProgramExtractionTests
{
    // A single file: top-level statements calling Worker.Start(), then two sibling types in the same file.
    // Worker.Start() in turn calls Gadget.Spin() — an edge that must stay Worker's, never Program's.
    private static readonly CodebaseModel Model = CompilationFactory.ExtractConsoleApp(("Program.cs", """
                                                                                                      using N;

                                                                                                      Worker.Start();

                                                                                                      namespace N
                                                                                                      {
                                                                                                          public static class Worker
                                                                                                          {
                                                                                                              public static void Start()
                                                                                                              {
                                                                                                                  Gadget.Spin();
                                                                                                              }
                                                                                                          }

                                                                                                          public static class Gadget
                                                                                                          {
                                                                                                              public static void Spin()
                                                                                                              {
                                                                                                              }
                                                                                                          }
                                                                                                      }
                                                                                                      """));

    [Fact]
    public void TopLevelStatements_SynthesizedProgram_ExtractsAsSolutionType()
    {
        TypeNode program = Model.Type("Program");

        program.IsExternal.ShouldBeFalse();
        program.ProjectName.ShouldBe("TestProject");
        program.Kind.ShouldBe(TypeKind.Class);
    }

    [Fact]
    public void TopLevelStatements_ReferenceFromTopLevelCode_MintsAnEdgeFromProgram()
    {
        // `Worker.Start();` is a top-level statement, so the edge is attributed to the synthesized Program.
        Model.HasEdge("Program", "N.Worker").ShouldBeTrue();
        Model.Edge("Program", "N.Worker").Lines().ShouldBe([3]);
    }

    [Fact]
    public void TopLevelStatements_SiblingTypeInSameFile_KeepsItsOwnEdgesNotProgramS()
    {
        // Gadget.Spin() is called from Worker.Start(), not from top-level code: the edge is Worker's, and
        // Program must NOT pick it up even though Worker is declared in Program's own compilation unit.
        Model.HasEdge("N.Worker", "N.Gadget").ShouldBeTrue();
        Model.HasEdge("Program", "N.Gadget").ShouldBeFalse();
    }

    [Fact]
    public void TopLevelStatements_SynthesizedProgram_HasEmptyDeclarationSites()
    {
        // Current behavior, pinned so a future change is deliberate: Program's declaring syntax
        // is the CompilationUnitSyntax, which carries no type identifier, so no declaration site is recorded
        // (unlike the synthesized <Main>$ member, which falls back to the compilation-unit location). The
        // hypothesized fix was not applied — Program already extracts, and re-siting it would perturb the
        // already-green dogfood model and the MyApp goldens.
        TypeNode program = Model.Type("Program");
        program.DeclarationSites.ShouldBeEmpty();
        program.FilePaths.ShouldBeEmpty();
    }
}