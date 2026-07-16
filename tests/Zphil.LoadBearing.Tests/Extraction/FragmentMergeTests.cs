using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     The cross-input unification semantics of the model build, over the MSBuild-free fast path with
///     several synthetic <see cref="CompilationInput" />s. These pin the behaviour the Phase 11 (WP5)
///     per-input-fragment refactor must reproduce byte-for-byte: first-declarer-wins node facts and
///     ProjectName, declaration-site union across declarers, declare-all-before-reference
///     (declared-beats-external globally), the reference-equality contract on constructions and edges,
///     external-node sharing, and the same-project-name (multi-TFM) project union.
/// </summary>
public sealed class FragmentMergeTests
{
    [Fact]
    public void ExtractFromCompilations_DuplicateFqnInTwoInputs_FirstInputWinsProjectNameAndShapeFacts()
    {
        // Both inputs declare N.Dup; the first (ordinal-project input order) owns the node facts.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class Dup {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public sealed class Dup {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        TypeNode dup = model.Type("N.Dup");
        dup.ProjectName.ShouldBe("Aproj");
        dup.IsSealed.ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_DuplicateFqnInTwoInputs_UnionsDeclarationSitesAcrossInputs()
    {
        // Partial-across-projects shape: one FQN declared by two inputs unions its declaration sites.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class Dup {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Type("N.Dup").DeclarationSites.Select(s => (s.FilePath, s.Line))
            .ShouldBe([("A.cs", 2), ("B.cs", 2)]);
    }

    [Fact]
    public void ExtractFromCompilations_FqnReferencedByEarlierInputDeclaredByLater_MaterializesAsDeclaredNode()
    {
        // The referencer is ordered BEFORE the declarer, so this only holds if declare-all fully
        // precedes hierarchy/edges: N.Late resolves to the declared node, not a shallow external.
        CompilationInput declarer = CompilationFactory.Compile("Bproj", ("Late.cs", """
                                                                                    namespace N;
                                                                                    public class Late {}
                                                                                    """));
        CompilationInput referencer = CompilationFactory.CompileReferencing(
            "Aproj", declarer.Compilation, "Bproj", ("Early.cs", """
                                                                 namespace N2;
                                                                 public class Early { public N.Late L; }
                                                                 """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([referencer, declarer]);

        TypeNode target = model.Edge("N2.Early", "N.Late").Target;
        target.IsExternal.ShouldBeFalse();
        target.ProjectName.ShouldBe("Bproj");
    }

    [Fact]
    public void ExtractFromCompilations_CrossInputConstruction_DefinitionIsSameInstanceAsTypesNode()
    {
        // The construction's Definition must be the very TypeNode instance held by Types (the
        // documented reference-equality contract), even when definition and user cross inputs.
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public interface IHandler<T> {}
                                                                              public class Msg {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Handler : N.IHandler<N.Msg> {}
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode handlerDef = model.Type("N.IHandler<T>");
        TypeConstruction construction = model.Type("N2.Handler").AllInterfaces
            .Single(c => c.Definition.FullName == "N.IHandler<T>");
        construction.Definition.ShouldBeSameAs(handlerDef);
        construction.FullName.ShouldBe("N.IHandler<N.Msg>");
    }

    [Fact]
    public void ExtractFromCompilations_CrossInputEdges_ReferenceSameNodeInstancesAsTypes()
    {
        // Edge Source/Target are the same instances as Types (reference equality, not name equality).
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public interface IHandler<T> {}
                                                                              public class Msg {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Handler : N.IHandler<N.Msg> {}
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode handler = model.Type("N2.Handler");
        TypeNode handlerDef = model.Type("N.IHandler<T>");
        TypeNode msg = model.Type("N.Msg");
        ReferenceEdge toInterface = model.Edge("N2.Handler", "N.IHandler<T>");
        ReferenceEdge toArgument = model.Edge("N2.Handler", "N.Msg");

        toInterface.Source.ShouldBeSameAs(handler);
        toInterface.Target.ShouldBeSameAs(handlerDef);
        toArgument.Target.ShouldBeSameAs(msg);
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectNameTwice_UnionsProjectReferences()
    {
        // Two compilations sharing a project name model one project's two TFMs; BuildProjects collapses
        // them to one node whose references are the ordinal union of both TFMs' references.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        var first = new CompilationInput(CompilationFactory.Compile("P", file).Compilation, "P", ["Legacy"]);
        var second = new CompilationInput(CompilationFactory.Compile("P", file).Compilation, "P", ["Modern"]);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Projects.Count(p => p.Name == "P").ShouldBe(1);
        model.Projects.Single(p => p.Name == "P").ProjectReferences.ShouldBe(["Legacy", "Modern"]);
    }

    [Fact]
    public void ExtractFromCompilations_ExternalReferencedByTwoInputs_IsSingleSharedExternalNode()
    {
        // The same external FQN referenced from two inputs unifies to one shared external node.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class CA { public System.Exception E; }
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N2;
                                                                               public class CB { public System.Exception E; }
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Types.Count(t => t.FullName == "System.Exception").ShouldBe(1);
        TypeNode shared = model.Type("System.Exception");
        shared.IsExternal.ShouldBeTrue();
        model.Edge("N.CA", "System.Exception").Target.ShouldBeSameAs(shared);
        model.Edge("N2.CB", "System.Exception").Target.ShouldBeSameAs(shared);
    }
}