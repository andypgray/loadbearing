using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     The cross-input unification semantics of the model build, over the MSBuild-free fast path with
///     several synthetic <see cref="CompilationInput" />s. These pin first-declarer-wins node facts and
///     ProjectName, declaration-site union across declarers, declare-all-before-reference
///     (declared-beats-external globally), the reference-equality contract on constructions and edges,
///     external-node sharing, and the same-project-name (multi-TFM) project union. The last two blocks pin
///     the two advisory note kinds that ride on top of first-declarer-wins: same-FQN cross-project
///     conflation, and one project's several target frameworks collapsing onto a shared type.
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

        model.Type("N.Dup")
            .DeclarationSites.Select(s => (s.FilePath, s.Line))
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

        TypeNode target = model.Edge("N2.Early", "N.Late")
            .Target;
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
        TypeConstruction construction = model.Type("N2.Handler")
            .AllInterfaces
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
        var first = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", ["Legacy"]);
        var second = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", ["Modern"]);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Projects.ShouldContain(p => p.Name == "P", expectedCount: 1);
        model.Projects.Single(p => p.Name == "P")
            .ProjectReferences.ShouldBe(["Legacy", "Modern"]);
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

        model.Types.ShouldContain(t => t.FullName == "System.Exception", expectedCount: 1);
        TypeNode shared = model.Type("System.Exception");
        shared.IsExternal.ShouldBeTrue();
        model.Edge("N.CA", "System.Exception")
            .Target.ShouldBeSameAs(shared);
        model.Edge("N2.CB", "System.Exception")
            .Target.ShouldBeSameAs(shared);
    }

    [Fact]
    public void ExtractFromCompilations_CrossInputConstruction_ConstructedIsSameInstanceAsTypesNode()
    {
        // A construction whose constructed type is declared by another input resolves to the declared node
        // (reference equality), never a shallow external — the ctor analog of the cross-input edge contract.
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public class Widget {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Maker { public object M() => new N.Widget(); }
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode widget = model.Type("N.Widget");
        ConstructorEdge edge = model.ConstructorEdge("N2.Maker", "N.Widget");
        edge.Constructed.ShouldBeSameAs(widget);
        edge.Constructed.IsExternal.ShouldBeFalse();
        edge.Source.ShouldBeSameAs(model.Type("N2.Maker"));
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectNameTwice_UnionsDuplicateConstructorEdgesToOneSite()
    {
        // Two compilations sharing a project name and source path model one project's two TFMs; the merge
        // unions the identical ctor edge (and its single site) rather than duplicating it.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            public class B { public object M() => new A(); }
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file);
        CompilationInput second = CompilationFactory.Compile("P", file);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.ConstructorEdges.ShouldContain(
            e => e.Source.FullName == "P.B" && e.Constructed.FullName == "P.A", expectedCount: 1);
        model.ConstructorEdge("P.B", "P.A")
            .Sites.ShouldHaveSingleItem();
    }

    // ── Injection edges / registration facts (GRAMMAR §4.7) ───────────────────────────────────────────

    [Fact]
    public void ExtractFromCompilations_CrossInputInjection_InjectedIsSameInstanceAsTypesNode()
    {
        // An injected parameter type declared by another input resolves to the declared node (reference
        // equality), never a shallow external — the injection analog of the cross-input edge contract.
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public interface IDep {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Svc { public Svc(N.IDep d) {} }
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode dep = model.Type("N.IDep");
        InjectionEdge edge = model.InjectionEdge("N2.Svc", "N.IDep");
        edge.Injected.ShouldBeSameAs(dep);
        edge.Injected.IsExternal.ShouldBeFalse();
        edge.Source.ShouldBeSameAs(model.Type("N2.Svc"));
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectNameTwice_UnionsDuplicateInjectionEdgesToOneSite()
    {
        // Two compilations sharing a project name and source path model one project's two TFMs; the merge
        // unions the identical injection edge (and its single site) rather than duplicating it.
        var file = ("P.cs", """
                            namespace P;
                            public interface IDep {}
                            public class Svc { public Svc(IDep d) {} }
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file);
        CompilationInput second = CompilationFactory.Compile("P", file);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.InjectionEdges.ShouldContain(
            e => e.Source.FullName == "P.Svc" && e.Injected.FullName == "P.IDep", expectedCount: 1);
        model.InjectionEdge("P.Svc", "P.IDep")
            .Sites.ShouldHaveSingleItem();
    }

    [Fact]
    public void ExtractFromCompilations_SameRegistrationInTwoFrameworks_UnionsToOneFactPerKey()
    {
        // One project's two target frameworks make the identical registration; the merge unions per
        // (lifetime, service, implementation) key to a single fact (its one shared site deduped).
        var file = ("Reg.cs", """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace P;
                              public interface IFoo {}
                              public class Foo : IFoo {}
                              public static class Reg
                              {
                                  public static void Configure(IServiceCollection services) => services.AddSingleton<IFoo, Foo>();
                              }
                              """);
        var first = new CompilationInput(CompilationFactory.CompileWithDi("P", file)
            .Compilation, "P", ["Legacy"]);
        var second = new CompilationInput(CompilationFactory.CompileWithDi("P", file)
            .Compilation, "P", ["Modern"]);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.ServiceRegistrations.ShouldContain(r => r.ServiceFullName == "P.IFoo", expectedCount: 1);
        model.Registration(Lifetime.Singleton, "P.IFoo", "P.Foo")
            .Sites.ShouldHaveSingleItem();
    }

    // ── Same-FQN cross-project conflation notes ───────────────────────────────────────────────────────

    [Fact]
    public void ExtractFromCompilations_SameFqnDeclaredByTwoDifferentProjects_RecordsCrossProjectMergeNote()
    {
        // Both Aproj and Bproj declare N.Dup. Aproj wins the facts (input order) while arch.Project("Bproj")
        // still selects the type — the merge records one advisory note naming winner, loser, and consequence.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class Dup {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBe([
            "Type 'N.Dup' is declared by projects 'Aproj' and 'Bproj'; its facts and project attribution "
            + "follow 'Aproj' (the first declarer), but arch.Project('Bproj') selections include it too, "
            + "and each declarer's reference to its own compiled-in copy counts against that declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_SameFqnSameProjectNameTwice_RecordsNoMergeNote()
    {
        // One project's two target frameworks declare the same FQN under the same name — the legitimate
        // multi-TFM union, not a conflation. It must stay silent.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        var first = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", ["Legacy"]);
        var second = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", ["Modern"]);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_DistinctFqnsAcrossProjects_HasNoMergeNotes()
    {
        // The common case: two projects, no shared FQN → nothing to conflate → the notes list stays empty
        // (so a merge-note-free model — every existing golden — is byte-identical).
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class A {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public class B {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_CrossProjectLoserDeclaredInTwoFrameworks_RecordsExactlyOneNote()
    {
        // Winner Aproj declares N.Dup once; loser Bproj declares it in two frameworks. The loser set
        // collapses the loser's two declarations to a single entry, and so to a single note.
        CompilationInput winner = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));
        var loserFirst = new CompilationInput(
            CompilationFactory.Compile("Bproj", ("B.cs", """
                                                         namespace N;
                                                         public class Dup {}
                                                         """))
                .Compilation, "Bproj", ["Legacy"]);
        var loserSecond = new CompilationInput(
            CompilationFactory.Compile("Bproj", ("B.cs", """
                                                         namespace N;
                                                         public class Dup {}
                                                         """))
                .Compilation, "Bproj", ["Modern"]);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, loserFirst, loserSecond]);

        model.MergeNotes.ShouldHaveSingleItem()
            .ShouldContain("declared by projects 'Aproj' and 'Bproj'");
    }

    [Fact]
    public void ExtractFromCompilations_SameFqnLostByThreeProjects_RecordsOneNoteNamingEveryLoser()
    {
        // The shape a spec-fixture layout produces when several sibling spec projects declare name-carrier
        // stubs so their typeof() anchors compile: one type shadowed several times over. No fixture here has
        // that layout — this repo's fixtures reference the projects they govern — but the shape is what any
        // real solution with stubbed anchors reports, so it stays pinned here. Grouping by
        // FQN keeps it to one line naming every loser rather than a line per loser: nothing dropped, and the
        // channel stays legible enough that a reader still notices a line arriving.
        CompilationInput winner = CompilationFactory.Compile("App.Web", ("W.cs", """
                                                                                 namespace N;
                                                                                 public class Dup {}
                                                                                 """));
        CompilationInput stubB = CompilationFactory.Compile("Spec.B", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));
        CompilationInput stubA = CompilationFactory.Compile("Spec.A", ("A.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        // Losers arrive out of order; the note orders them ordinal so the line is stable across runs.
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, stubB, stubA]);

        model.MergeNotes.ShouldBe([
            "Type 'N.Dup' is declared by projects 'App.Web', 'Spec.A' and 'Spec.B'; its facts and project "
            + "attribution follow 'App.Web' (the first declarer), but arch.Project('Spec.A') and "
            + "arch.Project('Spec.B') selections include it too, and each declarer's reference to its own "
            + "compiled-in copy counts against that declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_ConflatedType_CarriesEveryLosingProjectOnTheWinningNode()
    {
        // The same conflation as the note above, kept as a fact rather than only as a sentence: the survey
        // has to suppress an edge on it, and no consumer can act on prose. Losers only — ProjectName is the
        // winner, so repeating it here would make the node say the same thing twice.
        CompilationInput winner = CompilationFactory.Compile("App.Web", ("W.cs", """
                                                                                 namespace N;
                                                                                 public class Dup {}
                                                                                 """));
        CompilationInput stubB = CompilationFactory.Compile("Spec.B", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));
        CompilationInput stubA = CompilationFactory.Compile("Spec.A", ("A.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        // Losers arrive out of order; the list orders them ordinal like the note that reads the same table.
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, stubB, stubA]);

        TypeNode dup = model.Type("N.Dup");
        dup.ProjectName.ShouldBe("App.Web");
        dup.AlsoDeclaredBy.ShouldBe(["Spec.A", "Spec.B"]);
    }

    [Fact]
    public void ExtractFromCompilations_TypesNoOtherProjectDeclares_CarryNoOtherDeclarers()
    {
        // The overwhelming common case, and what keeps the survey's coverage statement absent rather than
        // empty: an ordinary type says nothing, and so does one whose own project declared it twice under
        // two target frameworks — one project is never a conflation.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        var modern = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "net10.0");
        var legacy = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "netstandard2.0");
        CompilationInput other = CompilationFactory.Compile("Q", ("Q.cs", """
                                                                          namespace Q;
                                                                          public class B {}
                                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy, other]);

        model.Type("P.A")
            .AlsoDeclaredBy.ShouldBeEmpty();
        model.Type("Q.B")
            .AlsoDeclaredBy.ShouldBeEmpty();
    }

    // ── Multi-target-framework collapse notes ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFromCompilations_SameProjectTwoFrameworksSharingAType_RecordsOneMultiFrameworkNote()
    {
        // One project file's two frameworks both declare P.A, so the type carries the first-extracted
        // framework's facts and only its facts. That is not a conflation — the project is one project — but
        // it is not free either, so it gets a note of its own naming both frameworks and the winner.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        var modern = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "net10.0");
        var legacy = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "netstandard2.0");

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.MergeNotes.ShouldBe([
            "Project 'P' targets 'net10.0' and 'netstandard2.0'; the types they share take their facts "
            + "from 'net10.0' (the first extracted), so a rule about them is checked against that "
            + "framework alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectTwoFrameworksDeclaringDisjointTypes_RecordsNoNote()
    {
        // The gate, and it is a correctness matter rather than an economy: a type only ONE framework
        // declares (a #if-guarded class, a framework-conditional <Compile>) keeps its own framework's facts.
        // Nothing collapsed, so a note saying the facts came from the other framework would be false.
        var modern = new CompilationInput(
            CompilationFactory.Compile("P", ("Modern.cs", """
                                                          namespace P;
                                                          public class Modern {}
                                                          """))
                .Compilation, "P", [], "net10.0");
        var legacy = new CompilationInput(
            CompilationFactory.Compile("P", ("Legacy.cs", """
                                                          namespace P;
                                                          public class Legacy {}
                                                          """))
                .Compilation, "P", [], "netstandard2.0");

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.MergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_MultiFrameworkProjectSharingManyTypes_RecordsExactlyOneNote()
    {
        // Per project, not per type: the answer is identical for every shared type, so a project sharing
        // three types costs one line and a project sharing two hundred still costs one — which is what keeps
        // the channel legible enough that a reader notices a line arriving at all.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            public class B {}
                            public class C {}
                            """);
        var modern = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "net10.0");
        var legacy = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "netstandard2.0");

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.MergeNotes.ShouldHaveSingleItem()
            .ShouldContain("Project 'P' targets 'net10.0' and 'netstandard2.0'");
    }

    // ── Multi-target-framework membership union ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, null, true)]
    [InlineData(null, true, true)]
    [InlineData(false, null, false)]
    [InlineData(null, false, false)]
    [InlineData(false, false, false)]
    [InlineData(null, null, null)]
    public void ExtractFromCompilations_MultiFrameworkProject_UnionsMembershipWithUnknownLosing(
        bool? first, bool? second, bool? expected)
    {
        // One project file, several compilations, one ProjectNode — so the label has to union like the
        // reference edges do. Declared by either wins; unknown never overrides a fragment that actually read
        // the solution, and only an all-unknown project stays unlabeled.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        var modern = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "net10.0", first);
        var legacy = new CompilationInput(CompilationFactory.Compile("P", file)
            .Compilation, "P", [], "netstandard2.0", second);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.Projects.Single()
            .SolutionMember.ShouldBe(expected);
    }
}
