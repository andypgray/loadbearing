using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Catch edges (GRAMMAR §4.8), over the MSBuild-free fast path — the catch analog of
///     <see cref="CodebaseExtractorConstructorEdgeTests" />. One fact per <c>catch</c> shape: a typed catch
///     (catch-channel beside the reference edge its type-name mints, no double-mint), a bare catch
///     (synthesized <c>System.Exception</c>, no reference edge, plus the null-lookup defensive row), <c>when</c>
///     filters, a rethrowing catch, and the must-NOT-mint rows (type-parameter, self-catch, error type). Plus
///     the attribution rows (lambda/local-function → enclosing type; top-level statements → <c>Program</c>).
///     Line numbers are 1-based and count from the first content line of each raw source literal.
///     The <see cref="CatchEdge.UnfilteredSites" /> rows carry the filter fact: which sites spell no
///     <c>when</c> filter, including the two corners that decided the recorded subset's polarity — a
///     same-line collision and a filter that diverges across fragments.
/// </summary>
public sealed class CodebaseExtractorCatchEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_TypedCatch_MintsCatchEdgeBesideTheReferenceEdgeNoDoubleMint()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError) { }
                                                             }
                                                         }
                                                         """);

        // The `catch (MyError)` node mints the catch channel ONLY; the inner `MyError` name syntax mints the
        // §4.1 reference edge on its own visit. Both stand at line 8, exactly once (the explicit-`new` precedent).
        model.CatchEdge("N.Worker", "N.MyError").Lines().ShouldBe([8]);
        model.CatchEdges("N.Worker").Count.ShouldBe(1);
        model.HasEdge("N.Worker", "N.MyError").ShouldBeTrue();
        model.Edge("N.Worker", "N.MyError").Lines().ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_BareCatch_SynthesizesSystemExceptionCatchChannelOnly()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch { }
                                                             }
                                                         }
                                                         """);

        // A bare `catch` synthesizes System.Exception (external). Nothing in source names the type, so it mints
        // NO reference edge — the catch channel is the sole source of the fact.
        CatchEdge edge = model.CatchEdge("N.Worker", "System.Exception");
        edge.Caught.IsExternal.ShouldBeTrue();
        edge.Lines().ShouldBe([7]);
        model.HasEdge("N.Worker", "System.Exception").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_BareCatchWithoutSystemException_MintsNothing()
    {
        // Defensive row: a compilation with no metadata references cannot resolve System.Exception, so the bare
        // catch's synthesized lookup (Compilation.GetTypeByMetadataName) returns null and the arm mints nothing
        // — a null lookup is a silent no-op, never a throw. An interface (default method body) is the subject
        // because it has no base type, so extraction never has to resolve the equally-absent System.Object.
        CodebaseModel model = CompilationFactory.ExtractWithoutReferences(("Test.cs", """
                                                                                      namespace N;
                                                                                      public interface IWorker { void Run() { try { } catch { } } }
                                                                                      """));

        model.CatchEdges("N.IWorker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_WhenFilter_DoesNotSuppressAndFilterContentsMintOrdinaryEdges()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public static class Guard { public static bool Ok(System.Exception e) => true; }
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError e) when (Guard.Ok(e)) { }
                                                             }
                                                         }
                                                         """);

        // The `when` filter never suppresses the catch edge, and its contents mint their own ordinary edges.
        model.CatchEdge("N.Worker", "N.MyError").Lines().ShouldBe([9]);
        model.HasEdge("N.Worker", "N.Guard").ShouldBeTrue();
        model.MemberEdges("N.Worker").ShouldContain(e => e.Member.Name == "Ok");
    }

    [Fact]
    public void ExtractFromCompilations_RethrowingCatch_StillMintsCatchEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError) { throw; }
                                                             }
                                                         }
                                                         """);

        // Edges are facts: a rethrowing catch still mints its catch edge. The bare rethrow `throw;` mints nothing.
        model.HasCatchEdge("N.Worker", "N.MyError").ShouldBeTrue();
        model.ThrowEdges("N.Worker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_TypeParameterCatch_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker
                                                         {
                                                             public void Run<T>() where T : System.Exception
                                                             {
                                                                 try { }
                                                                 catch (T) { }
                                                             }
                                                         }
                                                         """);

        // A type-parameter caught type is not a named type, so it mints nothing (the reference-universe gate).
        model.CatchEdges("N.Worker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ConstructedGenericCaughtType_NormalizesToOpenDefinition()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Boom<T> : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (Boom<int>) { }
                                                             }
                                                         }
                                                         """);

        // catch (Boom<int>) records the OPEN definition N.Boom<T> (§4.1), like every other edge target.
        model.HasCatchEdge("N.Worker", "N.Boom<T>").ShouldBeTrue();
        model.CatchEdge("N.Worker", "N.Boom<T>").Lines().ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_SelfCatch_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Recursive : System.Exception
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (Recursive) { }
                                                             }
                                                         }
                                                         """);

        // Self-catch is dropped, mirroring the type-edge self-drop (§4.1).
        model.HasCatchEdge("N.Recursive", "N.Recursive").ShouldBeFalse();
        model.CatchEdges("N.Recursive").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ErrorCaughtType_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (Undefined) { }
                                                             }
                                                         }
                                                         """);

        // An unresolvable caught type is an error type — the TypeKindMapper gate drops it, no catch edge.
        model.CatchEdges("N.Worker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_CatchInLocalFunction_AttributesToEnclosingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 Handle();
                                                                 void Handle()
                                                                 {
                                                                     try { }
                                                                     catch (MyError) { }
                                                                 }
                                                             }
                                                         }
                                                         """);

        // A catch inside a local function attributes to the enclosing type (the existing attribution machinery).
        model.HasCatchEdge("N.Worker", "N.MyError").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_TopLevelStatementsCatch_AttributesToProgram()
    {
        CodebaseModel model = CompilationFactory.ExtractConsoleApp(("Program.cs", """
                                                                                  try { }
                                                                                  catch (System.InvalidOperationException) { }
                                                                                  """));

        // A top-level-statements catch attributes to the synthesized Program (the TopLevelProgramExtractionTests
        // precedent), the same way a top-level reference does.
        CatchEdge edge = model.CatchEdge("Program", "System.InvalidOperationException");
        edge.Caught.IsExternal.ShouldBeTrue();
        edge.Lines().ShouldBe([2]);
    }

    [Fact]
    public void ExtractFromCompilations_CatchEdges_AreOrdinallyOrdered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Beta : System.Exception {}
                                                         public class Alpha : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (Beta) { }
                                                                 try { }
                                                                 catch (Alpha) { }
                                                             }
                                                         }
                                                         """);

        model.CatchEdges("N.Worker").Select(e => e.Caught.FullName).ShouldBe(["N.Alpha", "N.Beta"]);
    }

    [Fact]
    public void ExtractFromCompilations_WhenFilteredTypedCatch_MintsTheEdgeWithAnEmptyUnfilteredSubset()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool flag)
                                                             {
                                                                 try { }
                                                                 catch (MyError) when (flag) { }
                                                             }
                                                         }
                                                         """);

        // The filter leaves the edge and its sites untouched — it only keeps the site out of the unfiltered
        // subset, which is the whole of the new fact.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines().ShouldBe([8]);
        edge.UnfilteredLines().ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_UnfilteredTypedCatch_RecordsTheSiteUnfiltered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError) { }
                                                             }
                                                         }
                                                         """);

        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines().ShouldBe([8]);
        edge.UnfilteredLines().ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_BareCatch_RecordsSystemExceptionUnfiltered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch { }
                                                             }
                                                         }
                                                         """);

        // A bare `catch` synthesizes System.Exception and spells no filter, so it is unfiltered — the shape a
        // ban on unfiltered broad catches must reach.
        CatchEdge edge = model.CatchEdge("N.Worker", "System.Exception");
        edge.Lines().ShouldBe([7]);
        edge.UnfilteredLines().ShouldBe([7]);
    }

    [Fact]
    public void ExtractFromCompilations_BareCatchWithFilter_RecordsSystemExceptionFiltered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker
                                                         {
                                                             public void Run(bool flag)
                                                             {
                                                                 try { }
                                                                 catch when (flag) { }
                                                             }
                                                         }
                                                         """);

        // The synthesized caught type is the same as for a bare `catch`; only the filter fact differs.
        CatchEdge edge = model.CatchEdge("N.Worker", "System.Exception");
        edge.Lines().ShouldBe([7]);
        edge.UnfilteredLines().ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_WhenTrueFilter_CountsAsFiltered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError) when (true) { }
                                                             }
                                                         }
                                                         """);

        // The honesty boundary: filter presence is syntactic. A `when (true)` catches everything the unfiltered
        // form would, and the axis still records it filtered — judging what a filter tests is not this fact's job.
        model.CatchEdge("N.Worker", "N.MyError").UnfilteredLines().ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_FilteredAndUnfilteredCatchesOfOnePair_RecordOnlyTheUnfilteredSite()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool flag)
                                                             {
                                                                 try { }
                                                                 catch (MyError) when (flag) { }
                                                                 try { }
                                                                 catch (MyError) { }
                                                             }
                                                         }
                                                         """);

        // One edge keyed (source, caught) as always; the two clauses split across the two site lists.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines().ShouldBe([8, 10]);
        edge.UnfilteredLines().ShouldBe([10]);
    }

    [Fact]
    public void ExtractFromCompilations_FilteredAndUnfilteredCatchesOnOneLine_RecordTheCollapsedSiteUnfiltered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool flag)
                                                             {
                                                                 try { }
                                                                 catch (MyError) when (flag) { } catch (MyError) { }
                                                             }
                                                         }
                                                         """);

        // The corner the recorded polarity was chosen for. Sites dedupe by (file, line), so two clauses of one
        // caught type on one physical line collapse to a single site — and that site is claimed by the
        // unfiltered clause. Recording the FILTERED subset instead would mark the collapsed site filtered and
        // hide a real unfiltered catch from a ban; recording the unfiltered subset errs toward the red answer.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines().ShouldBe([8]);
        edge.UnfilteredLines().ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_CatchEdgeAcrossTwoFragments_UnionsBothSiteLists()
    {
        CompilationInput first = CompilationFactory.Compile("P", ("A.cs", """
                                                                          namespace N;
                                                                          public class MyError : System.Exception {}
                                                                          public class Worker
                                                                          {
                                                                              public void Run(bool flag)
                                                                              {
                                                                                  try { }
                                                                                  catch (MyError) when (flag) { }
                                                                              }
                                                                          }
                                                                          """));
        CompilationInput second = CompilationFactory.Compile("P", ("B.cs", """
                                                                           namespace N;
                                                                           public class MyError : System.Exception {}
                                                                           public class Worker
                                                                           {
                                                                               public void Run(bool flag)
                                                                               {
                                                                                   try { }
                                                                                   catch (MyError) { }
                                                                               }
                                                                           }
                                                                           """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        // Both lists union per (source, caught), each staying (file, line) ordered.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Sites.Select(s => s.ToString()).ShouldBe(["A.cs:8", "B.cs:8"]);
        edge.UnfilteredSites.Select(s => s.ToString()).ShouldBe(["B.cs:8"]);
    }

    [Fact]
    public void ExtractFromCompilations_FilterDivergingAcrossFragments_RecordsTheSharedSiteUnfiltered()
    {
        // The second corner the recorded polarity was chosen for: one file, two compilations of it — a `#if`-guarded
        // filter across target frameworks — so one (file, line) is filtered in one fragment and unfiltered in the
        // other. Unioning the UNFILTERED sites reads the shared site unfiltered, the truthful answer for a ban;
        // unioning a FILTERED set would read it filtered and no merge-side subtraction could recover it.
        CompilationInput filtered = CompilationFactory.Compile("P", ("Worker.cs", """
                                                                                  namespace N;
                                                                                  public class MyError : System.Exception {}
                                                                                  public class Worker
                                                                                  {
                                                                                      public void Run(bool flag)
                                                                                      {
                                                                                          try { }
                                                                                          catch (MyError) when (flag) { }
                                                                                      }
                                                                                  }
                                                                                  """));
        CompilationInput unfiltered = CompilationFactory.Compile("P", ("Worker.cs", """
                                                                                    namespace N;
                                                                                    public class MyError : System.Exception {}
                                                                                    public class Worker
                                                                                    {
                                                                                        public void Run(bool flag)
                                                                                        {
                                                                                            try { }
                                                                                            catch (MyError) { }
                                                                                        }
                                                                                    }
                                                                                    """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([filtered, unfiltered]);

        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Sites.Select(s => s.ToString()).ShouldBe(["Worker.cs:8"]);
        edge.UnfilteredSites.Select(s => s.ToString()).ShouldBe(["Worker.cs:8"]);
    }

    [Fact]
    public void ExtractFromCompilations_MixedCatchShapes_KeepUnfilteredSitesASubsetOfSites()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool flag)
                                                             {
                                                                 try { }
                                                                 catch (MyError) when (flag) { }
                                                                 try { }
                                                                 catch (MyError) { }
                                                                 try { }
                                                                 catch (System.InvalidOperationException) when (flag) { }
                                                                 try { }
                                                                 catch { }
                                                             }
                                                         }
                                                         """);

        // The invariant the parallel recording holds by construction: every unfiltered site is a site. Compared
        // through the rendered file:line form, since the two lists carry their own SourceLocation instances.
        model.CatchEdges("N.Worker").Count.ShouldBe(3);
        foreach (CatchEdge edge in model.CatchEdges("N.Worker"))
            edge.UnfilteredSites.Select(s => s.ToString())
                .ShouldBeSubsetOf(edge.Sites.Select(s => s.ToString()));
    }
}