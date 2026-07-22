using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Catch edges (GRAMMAR §4.8), over the MSBuild-free fast path — the catch analog of
///     <see cref="CodebaseExtractorConstructorEdgeTests" />. One fact per <c>catch</c> shape: a typed catch
///     (catch-channel beside the reference edge its type-name mints, no double-mint), a bare catch
///     (synthesized <c>System.Exception</c>, no reference edge, plus the null-lookup defensive row), <c>when</c>
///     filters, a rethrowing catch, and the must-NOT-mint rows (type-parameter, self-catch, error type). Plus
///     the attribution rows (lambda/local-function → enclosing type; top-level statements → <c>Program</c>).
///     Line numbers are 1-based and count from the first content line of each raw source literal.
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
}