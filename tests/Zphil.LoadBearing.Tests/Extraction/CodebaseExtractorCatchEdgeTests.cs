using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Catch edges (GRAMMAR §4.8), over the MSBuild-free fast path — the catch analog of
///     <see cref="CodebaseExtractorConstructorEdgeTests" />.
/// </summary>
/// <remarks>
///     One fact per <c>catch</c> shape: a typed catch
///     (catch-channel beside the reference edge its type-name mints, no double-mint), a bare catch
///     (synthesized <c>System.Exception</c>, no reference edge, plus the null-lookup defensive row), <c>when</c>
///     filters, a rethrowing catch, and the must-NOT-mint rows (type-parameter, self-catch, error type). Plus
///     the attribution rows (lambda/local-function → enclosing type; top-level statements → <c>Program</c>).
///     Line numbers are 1-based and count from the first content line of each raw source literal.
///     The <see cref="CatchEdge.UnfilteredSites" /> rows carry the filter fact: which sites spell no
///     <c>when</c> filter, including the two corners that decided the recorded subset's polarity — a
///     same-line collision and a filter that diverges across fragments. The
///     <see cref="CatchEdge.SwallowingSites" /> rows carry the rethrow fact on top of it: which of those
///     sites do not end in a <c>throw</c>, the same two polarity corners, and both sides of the syntactic
///     honesty boundary — a throw that is the last statement but not on every path, and a throw on some path
///     that is not the last statement.
/// </remarks>
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
        model.CatchEdge("N.Worker", "N.MyError")
            .Lines()
            .ShouldBe([8]);
        model.CatchEdges("N.Worker")
            .Count.ShouldBe(1);
        model.HasEdge("N.Worker", "N.MyError")
            .ShouldBeTrue();
        model.Edge("N.Worker", "N.MyError")
            .Lines()
            .ShouldBe([8]);
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
        edge.Lines()
            .ShouldBe([7]);
        model.HasEdge("N.Worker", "System.Exception")
            .ShouldBeFalse();
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

        model.CatchEdges("N.IWorker")
            .ShouldBeEmpty();
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
        model.CatchEdge("N.Worker", "N.MyError")
            .Lines()
            .ShouldBe([9]);
        model.HasEdge("N.Worker", "N.Guard")
            .ShouldBeTrue();
        model.MemberEdges("N.Worker")
            .ShouldContain(e => e.Member.Name == "Ok");
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
        model.HasCatchEdge("N.Worker", "N.MyError")
            .ShouldBeTrue();
        model.ThrowEdges("N.Worker")
            .ShouldBeEmpty();
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
        model.CatchEdges("N.Worker")
            .ShouldBeEmpty();
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
        model.HasCatchEdge("N.Worker", "N.Boom<T>")
            .ShouldBeTrue();
        model.CatchEdge("N.Worker", "N.Boom<T>")
            .Lines()
            .ShouldBe([8]);
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
        model.HasCatchEdge("N.Recursive", "N.Recursive")
            .ShouldBeFalse();
        model.CatchEdges("N.Recursive")
            .ShouldBeEmpty();
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
        model.CatchEdges("N.Worker")
            .ShouldBeEmpty();
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
        model.HasCatchEdge("N.Worker", "N.MyError")
            .ShouldBeTrue();
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
        edge.Lines()
            .ShouldBe([2]);
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

        model.CatchEdges("N.Worker")
            .Select(e => e.Caught.FullName)
            .ShouldBe(["N.Alpha", "N.Beta"]);
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
        // subset, which is the whole of the new fact. Its positive-polarity partners — the typed and bare
        // catches that DO record a site unfiltered — are the swallowing rows further down
        // (ExtractFromCompilations_UnfilteredCatchNotEndingInThrow_RecordsTheSiteSwallowing and
        // ExtractFromCompilations_BareCatchNotEndingInThrow_RecordsSystemExceptionSwallowing), which assert
        // the unfiltered subset on the way to the rethrow fact rather than repeating it in a row of their own.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines()
            .ShouldBe([8]);
        edge.UnfilteredLines()
            .ShouldBeEmpty();
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
        edge.Lines()
            .ShouldBe([7]);
        edge.UnfilteredLines()
            .ShouldBeEmpty();
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
        model.CatchEdge("N.Worker", "N.MyError")
            .UnfilteredLines()
            .ShouldBeEmpty();
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
        edge.Lines()
            .ShouldBe([8, 10]);
        edge.UnfilteredLines()
            .ShouldBe([10]);
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
        edge.Lines()
            .ShouldBe([8]);
        edge.UnfilteredLines()
            .ShouldBe([8]);
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
        edge.Sites.Select(s => s.ToString())
            .ShouldBe(["A.cs:8", "B.cs:8"]);
        edge.UnfilteredSites.Select(s => s.ToString())
            .ShouldBe(["B.cs:8"]);
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
        edge.Sites.Select(s => s.ToString())
            .ShouldBe(["Worker.cs:8"]);
        edge.UnfilteredSites.Select(s => s.ToString())
            .ShouldBe(["Worker.cs:8"]);
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
        model.CatchEdges("N.Worker")
            .Count.ShouldBe(3);
        foreach (CatchEdge edge in model.CatchEdges("N.Worker"))
            edge.UnfilteredSites.Select(s => s.ToString())
                .ShouldBeSubsetOf(edge.Sites.Select(s => s.ToString()));
    }

    [Fact]
    public void ExtractFromCompilations_UnfilteredCatchNotEndingInThrow_RecordsTheSiteSwallowing()
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
        edge.Lines()
            .ShouldBe([8]);
        edge.UnfilteredLines()
            .ShouldBe([8]);
        edge.SwallowingLines()
            .ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_RethrowingCatch_MintsTheEdgeWithAnEmptySwallowingSubset()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError) { Cleanup(); throw; }
                                                             }
                                                             private void Cleanup() {}
                                                         }
                                                         """);

        // The rethrow leaves the edge and its unfiltered subset untouched — it only keeps the site out of the
        // swallowing subset, which is the whole of the new fact.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines()
            .ShouldBe([8]);
        edge.UnfilteredLines()
            .ShouldBe([8]);
        edge.SwallowingLines()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_TranslatingCatch_MintsTheEdgeWithAnEmptySwallowingSubset()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError e) { throw new System.InvalidOperationException("x", e); }
                                                             }
                                                         }
                                                         """);

        // `throw new X(…)` and a bare `throw;` are both throw statements, so the fact reads the same for the
        // translate-and-throw shape as for cleanup-and-rethrow — the throw operand is never judged.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.UnfilteredLines()
            .ShouldBe([8]);
        edge.SwallowingLines()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_BareCatchNotEndingInThrow_RecordsSystemExceptionSwallowing()
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

        // Self-contained down to the site list, deliberately: this is the only fact left carrying the
        // unfiltered subset for a bare `catch`, so it may not lean on a sibling for `Lines()`.
        CatchEdge edge = model.CatchEdge("N.Worker", "System.Exception");
        edge.Lines()
            .ShouldBe([7]);
        edge.UnfilteredLines()
            .ShouldBe([7]);
        edge.SwallowingLines()
            .ShouldBe([7]);
    }

    [Fact]
    public void ExtractFromCompilations_FilteredCatchNotEndingInThrow_RecordsAnEmptySwallowingSubset()
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

        // The swallowing subset is a subset of the UNFILTERED subset, not of the sites: a filtered clause that
        // swallows is where the handler named its expectations, and stays lawful.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines()
            .ShouldBe([8]);
        edge.UnfilteredLines()
            .ShouldBeEmpty();
        edge.SwallowingLines()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ReturnBeforeATerminalThrow_CountsAsThrowing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool flag)
                                                             {
                                                                 try { }
                                                                 catch (MyError) { if (flag) return; throw; }
                                                             }
                                                         }
                                                         """);

        // The documented honesty boundary, one side: the fact is the block's LAST STATEMENT, never an all-paths
        // flow analysis, so a handler that can leave without throwing still reads as throwing.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.UnfilteredLines()
            .ShouldBe([8]);
        edge.SwallowingLines()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowThatIsNotTheLastStatement_CountsAsSwallowing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool flag)
                                                             {
                                                                 try { }
                                                                 catch (MyError) { if (flag) throw; Cleanup(); }
                                                             }
                                                             private void Cleanup() {}
                                                         }
                                                         """);

        // The other side of the same boundary, and the reason it is stated rather than hidden: a handler that
        // throws on some path but ends on another statement reads as swallowing.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.UnfilteredLines()
            .ShouldBe([8]);
        edge.SwallowingLines()
            .ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_RethrowingAndSwallowingCatchesOfOnePair_RecordOnlyTheSwallowingSite()
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
                                                                 try { }
                                                                 catch (MyError) { }
                                                             }
                                                         }
                                                         """);

        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines()
            .ShouldBe([8, 10]);
        edge.UnfilteredLines()
            .ShouldBe([8, 10]);
        edge.SwallowingLines()
            .ShouldBe([10]);
    }

    [Fact]
    public void ExtractFromCompilations_RethrowingAndSwallowingCatchesOnOneLine_RecordTheCollapsedSiteSwallowing()
    {
        // The polarity corner, one level down from the filter subset's, and the reason the swallowing sites are
        // recorded rather than derived. Sites dedupe by (file, line), so a rethrowing and a swallowing catch of
        // one type on ONE physical line collapse to a single site — the corner the test above cannot reach,
        // because its two clauses sit on lines of their own. Recording the SWALLOWING sites reads that collapsed
        // site swallowing, the truthful answer for a ban; a recorded THROWING set with the complement taken at
        // the merge would read the very same site lawful and hide the swallow.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { } catch (MyError) { throw; } try { } catch (MyError) { }
                                                             }
                                                         }
                                                         """);

        // One site, not two: the collapse is the premise, so asserting it here is what keeps the corner real.
        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Lines()
            .ShouldBe([7]);
        edge.UnfilteredLines()
            .ShouldBe([7]);
        edge.SwallowingLines()
            .ShouldBe([7]);
    }

    [Fact]
    public void ExtractFromCompilations_RethrowDivergingAcrossFragments_RecordsTheSharedSiteSwallowing()
    {
        // The second corner: one file, two compilations of it — a `#if`-guarded rethrow across target frameworks
        // — so one (file, line) rethrows in one fragment and swallows in the other. Unioning the SWALLOWING sites
        // reads the shared site swallowing, the truthful answer for a ban.
        CompilationInput rethrowing = CompilationFactory.Compile("P", ("Worker.cs", """
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
                                                                                    """));
        CompilationInput swallowing = CompilationFactory.Compile("P", ("Worker.cs", """
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
                                                                                    """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([rethrowing, swallowing]);

        CatchEdge edge = model.CatchEdge("N.Worker", "N.MyError");
        edge.Sites.Select(s => s.ToString())
            .ShouldBe(["Worker.cs:8"]);
        edge.UnfilteredSites.Select(s => s.ToString())
            .ShouldBe(["Worker.cs:8"]);
        edge.SwallowingSites.Select(s => s.ToString())
            .ShouldBe(["Worker.cs:8"]);
    }

    [Fact]
    public void ExtractFromCompilations_MixedCatchShapes_KeepSwallowingSitesASubsetOfUnfilteredSites()
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
                                                                 catch (System.InvalidOperationException) { throw; }
                                                                 try { }
                                                                 catch { }
                                                             }
                                                         }
                                                         """);

        // The subset-of-a-subset invariant the parallel recording holds by construction: every swallowing site is
        // an unfiltered site, and every unfiltered site is a site.
        model.CatchEdges("N.Worker")
            .Count.ShouldBe(3);
        foreach (CatchEdge edge in model.CatchEdges("N.Worker"))
        {
            edge.UnfilteredSites.Select(s => s.ToString())
                .ShouldBeSubsetOf(edge.Sites.Select(s => s.ToString()));
            edge.SwallowingSites.Select(s => s.ToString())
                .ShouldBeSubsetOf(edge.UnfilteredSites.Select(s => s.ToString()));
        }
    }
}
