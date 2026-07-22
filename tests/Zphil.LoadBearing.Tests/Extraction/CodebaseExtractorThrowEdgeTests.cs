using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Throw edges (GRAMMAR §4.8), over the MSBuild-free fast path — the throw analog of
///     <see cref="CodebaseExtractorConstructorEdgeTests" />. One fact per <c>throw</c> shape: a
///     <c>throw new T()</c> statement (coexisting with its construction and reference edges), the throw-expression
///     forms (expression-bodied <c>=&gt; throw</c>, <c>?? throw</c>, switch-expression arm), the natural-static-type
///     rule (never the throw-converted <c>System.Exception</c>), <c>throw ex</c>'s variable static type, and the
///     must-NOT-mint rows (bare rethrow <c>throw;</c>, <c>throw null</c>, type-parameter throw, throw helpers,
///     self-throw, error type). Plus the attribution rows. Line numbers count from the first content line of each
///     raw source literal.
/// </summary>
public sealed class CodebaseExtractorThrowEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_ThrowNew_CoexistsWithConstructionAndReferenceEdges()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker { public void Run() { throw new MyError(); } }
                                                         """);

        // The same site yields all three facts: the throw edge (§4.8), the construction edge (§4.5), and the
        // §4.1 reference edge from the `MyError` name — recorded beside one another, never instead.
        model.ThrowEdge("N.Worker", "N.MyError").Lines().ShouldBe([3]);
        model.HasConstructorEdge("N.Worker", "N.MyError").ShouldBeTrue();
        model.HasEdge("N.Worker", "N.MyError").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowNewDerivedException_RecordsNaturalTypeNotConvertedException()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker { public void Run() { throw new MyError(); } }
                                                         """);

        // CRITICAL: the thrown type is the expression's NATURAL static type (MyError), never the throw
        // conversion's ConvertedType (System.Exception) — using ConvertedType would collapse every throw to
        // System.Exception and erase the real type.
        model.HasThrowEdge("N.Worker", "N.MyError").ShouldBeTrue();
        model.HasThrowEdge("N.Worker", "System.Exception").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_ExpressionBodiedThrow_MintsThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker { public object Run() => throw new MyError(); }
                                                         """);

        // The expression-bodied `=> throw new X()` is a throw EXPRESSION (the most common form in the wild).
        model.ThrowEdge("N.Worker", "N.MyError").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_NullCoalescingThrow_MintsThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public string Run(string s)
                                                             {
                                                                 return s ?? throw new MyError();
                                                             }
                                                         }
                                                         """);

        // A `?? throw` right-operand is a throw expression.
        model.ThrowEdge("N.Worker", "N.MyError").Lines().ShouldBe([7]);
    }

    [Fact]
    public void ExtractFromCompilations_SwitchExpressionArmThrow_MintsThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public int Run(int x) => x switch
                                                             {
                                                                 1 => 10,
                                                                 _ => throw new MyError()
                                                             };
                                                         }
                                                         """);

        // A switch-expression arm is a throw expression.
        model.ThrowEdge("N.Worker", "N.MyError").Lines().ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_ThrowVariable_MintsTheVariableStaticType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError ex) { throw ex; }
                                                             }
                                                         }
                                                         """);

        // `throw ex` mints the VARIABLE's static type — the deliberate asymmetry with the bare rethrow `throw;`
        // (which mints nothing): under strict MustOnlyThrow, `catch (Exception ex) { throw ex; }` is red.
        model.ThrowEdge("N.Worker", "N.MyError").Lines().ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_BareRethrow_MintsNothing()
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

        // A bare rethrow `throw;` (null expression) mints no throw edge.
        model.ThrowEdges("N.Worker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowNull_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker { public void Run() { throw null; } }
                                                         """);

        // `throw null` has no thrown type (the null literal), so it mints nothing.
        model.ThrowEdges("N.Worker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_TypeParameterThrow_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker { public void Run<T>(T ex) where T : System.Exception { throw ex; } }
                                                         """);

        // A type-parameter thrown type is not a named type, so it mints nothing.
        model.ThrowEdges("N.Worker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowHelper_MintsMemberUseOnlyNoThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker
                                                         {
                                                             public void Run(object o)
                                                             {
                                                                 System.ArgumentNullException.ThrowIfNull(o);
                                                             }
                                                         }
                                                         """);

        // A throw helper is an ordinary invocation, not a throw: the walk sees a member use, not a throw. So it
        // mints the member-use (and reference) edge but NO throw edge — the named §4.8 honesty boundary.
        model.ThrowEdges("N.Worker").ShouldBeEmpty();
        model.HasEdge("N.Worker", "System.ArgumentNullException").ShouldBeTrue();
        model.MemberEdges("N.Worker").ShouldContain(e => e.Member.Name == "ThrowIfNull");
    }

    [Fact]
    public void ExtractFromCompilations_ConstructedGenericThrownType_NormalizesToOpenDefinition()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Boom<T> : System.Exception {}
                                                         public class Worker { public void Run() { throw new Boom<int>(); } }
                                                         """);

        // throw new Boom<int>() records the OPEN definition N.Boom<T> (§4.1).
        model.HasThrowEdge("N.Worker", "N.Boom<T>").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_SelfThrow_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Recursive : System.Exception
                                                         {
                                                             public void Run() { throw new Recursive(); }
                                                         }
                                                         """);

        // Self-throw is dropped, mirroring the type-edge self-drop (§4.1).
        model.HasThrowEdge("N.Recursive", "N.Recursive").ShouldBeFalse();
        model.ThrowEdges("N.Recursive").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ErrorThrownType_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker { public void Run() { throw new Undefined(); } }
                                                         """);

        // An unresolvable thrown type is an error type — the TypeKindMapper gate drops it, no throw edge.
        model.ThrowEdges("N.Worker").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowInLocalFunction_AttributesToEnclosingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 void Handle() { throw new MyError(); }
                                                                 Handle();
                                                             }
                                                         }
                                                         """);

        // A throw inside a local function attributes to the enclosing type (the existing attribution machinery).
        model.HasThrowEdge("N.Worker", "N.MyError").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_TopLevelStatementsThrow_AttributesToProgram()
    {
        CodebaseModel model = CompilationFactory.ExtractConsoleApp(("Program.cs", """
                                                                                  throw new System.InvalidOperationException();
                                                                                  """));

        // A top-level-statements throw attributes to the synthesized Program (the TopLevelProgramExtractionTests
        // precedent).
        ThrowEdge edge = model.ThrowEdge("Program", "System.InvalidOperationException");
        edge.Thrown.IsExternal.ShouldBeTrue();
        edge.Lines().ShouldBe([1]);
    }

    [Fact]
    public void ExtractFromCompilations_ThrowEdges_AreOrdinallyOrdered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Beta : System.Exception {}
                                                         public class Alpha : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool b)
                                                             {
                                                                 if (b) throw new Beta();
                                                                 throw new Alpha();
                                                             }
                                                         }
                                                         """);

        model.ThrowEdges("N.Worker").Select(e => e.Thrown.FullName).ShouldBe(["N.Alpha", "N.Beta"]);
    }
}