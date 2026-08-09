using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     <see cref="GraphSummarizer" /> facts over the MSBuild-free fast path: cross-project edge grouping
///     and type-pair counts, same-project exclusion, declared-vs-observed divergence, external grouping by
///     namespace root (two-segment, one-segment, and global), namespace inventory counts, and
///     deterministic ordering. Sources are controlled so the counts are hand-verifiable; the golden CLI
///     test proves the same summary shape over the real MyApp solution.
///     <para>
///         The scoping rows below pin <see cref="GraphSummarizer.Scope" />'s asymmetry: a project edge
///         survives on <em>either</em> endpoint, an external edge on its source alone, and a surviving
///         project keeps its declared references verbatim.
///     </para>
/// </summary>
public sealed class GraphSummarizerTests
{
    [Fact]
    public void Summarize_CrossProjectReferences_GroupsByProjectPairCountsTypePairsAndExcludesSameProject()
    {
        // Arrange — App references Lib via two distinct type-pairs (A→Service, B→Helper) plus a
        // same-project edge (B→A) that must not appear in the survey.
        CompilationInput lib = CompilationFactory.Compile("Lib", ("Lib.cs", """
                                                                            namespace Lib;
                                                                            public class Service {}
                                                                            public class Helper {}
                                                                            """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib", ("App.cs", """
                                                                                                               namespace App;
                                                                                                               public class A { public Lib.Service S; }
                                                                                                               public class B { public Lib.Helper H; public A Sibling; }
                                                                                                               """));

        // Act — input order [lib, app]; the summary must come back ordinal by name regardless.
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert
        summary.Projects.Select(p => p.Name).ShouldBe(["App", "Lib"]);
        summary.ProjectEdges.Select(e => (e.Source, e.Target, e.References)).ShouldBe([("App", "Lib", 2)]);
    }

    [Fact]
    public void Summarize_DeclaredReferenceWithoutObservedEdge_ShowsDivergence()
    {
        // Arrange — App declares a reference to Lib but no App type touches a Lib type.
        CompilationInput lib = CompilationFactory.Compile("Lib", ("Lib.cs", """
                                                                            namespace Lib;
                                                                            public class Service {}
                                                                            """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib", ("App.cs", """
                                                                                                               namespace App;
                                                                                                               public class Standalone {}
                                                                                                               """));

        // Act
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert — the declared reference is visible, but no observed edge backs it (the dead-reference signal).
        summary.Projects.Single(p => p.Name == "App").ProjectReferences.ShouldBe(["Lib"]);
        summary.ProjectEdges.ShouldBeEmpty();
    }

    [Fact]
    public void Summarize_ExternalReferences_GroupByNamespaceRootAcrossSegmentWidths()
    {
        // Arrange — Vendor is referenced as metadata but NOT declared, so its types are external. Its
        // namespaces exercise a two-segment root (Vendor.Data), a one-segment namespace (Solo), and the
        // global namespace (Rootless).
        CompilationInput vendor = CompilationFactory.Compile("Vendor", ("Vendor.cs", """
                                                                                     namespace Vendor.Data { public class Record {} public class Table {} }
                                                                                     namespace Solo { public class Widget {} }
                                                                                     public class Rootless {}
                                                                                     """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", vendor.Compilation, "Vendor", ("App.cs", """
                                                                                                                     namespace App;
                                                                                                                     public class Client
                                                                                                                     {
                                                                                                                         public Vendor.Data.Record R;
                                                                                                                         public Vendor.Data.Table T;
                                                                                                                         public Solo.Widget W;
                                                                                                                         public Rootless Root;
                                                                                                                     }
                                                                                                                     """));

        // Act — extract App only, so Vendor's types stay external.
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([app]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert — grouped by (source, root) ordinal; Vendor.Data collapses its two type-pairs to one row.
        summary.ExternalEdges.Select(e => (e.Source, e.TargetNamespaceRoot, e.References))
            .ShouldBe([("App", "(global)", 1), ("App", "Solo", 1), ("App", "Vendor.Data", 2)]);
    }

    [Fact]
    public void Summarize_NamespaceInventory_CountsPerNamespaceOrdinalWithGlobalFirst()
    {
        // Arrange — one project spanning two namespaces plus a global-namespace type.
        CodebaseModel model = CompilationFactory.Extract("Multi", ("Multi.cs", """
                                                                               namespace Multi.A { public class One {} public class Two {} }
                                                                               namespace Multi.B { public class Three {} }
                                                                               public class Rootless {}
                                                                               """));

        // Act
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert — 4 declared types; the empty namespace renders (global) and sorts first (ordinal).
        ProjectSummary multi = summary.Projects.Single();
        multi.Types.ShouldBe(4);
        multi.Namespaces.Select(n => (n.Namespace, n.Types))
            .ShouldBe([("(global)", 1), ("Multi.A", 2), ("Multi.B", 1)]);
    }

    [Fact]
    public void Scope_ProjectGlob_KeepsMatchingProjectsAndTheirExternalEdges()
    {
        // Arrange
        GraphSummary summary = ThreeProjectSummary();

        // Act — '*' spans the dot, so one pattern takes both Acme projects and leaves Contoso.Web out.
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Acme.*"]);

        // Assert — the roster narrows; the one external edge rides with its source project.
        scoped.Projects.Select(p => p.Name).ShouldBe(["Acme.App", "Acme.Lib"]);
        scoped.ExternalEdges.Select(e => (e.Source, e.TargetNamespaceRoot, e.References))
            .ShouldBe([("Acme.App", "System", 1)]);
    }

    [Fact]
    public void Scope_EdgeIntoTheScopeFromOutside_SurvivesAndNamesAProjectTheRosterDoesNot()
    {
        // Arrange — only Acme.App references Acme.Lib, so scoping to Acme.Lib leaves an inbound edge only.
        GraphSummary summary = ThreeProjectSummary();

        // Act
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Acme.Lib"]);

        // Assert — source-OR-target: an edge whose source is out of scope survives, because who reaches into
        // the scoped projects is the evidence a layering rule is drafted from. Requiring both ends would
        // drop this row and leave the survey claiming nobody references Acme.Lib.
        scoped.Projects.Select(p => p.Name).ShouldBe(["Acme.Lib"]);
        scoped.ProjectEdges.Select(e => (e.Source, e.Target, e.References))
            .ShouldBe([("Acme.App", "Acme.Lib", 1)]);

        // The documented consequence: the surviving edge names a project the roster does not carry.
        scoped.Projects.Select(p => p.Name).ShouldNotContain("Acme.App");

        // An external edge is attributed to one project, so it needs a source match and Acme.App's is gone.
        scoped.ExternalEdges.ShouldBeEmpty();
    }

    [Fact]
    public void Scope_MatchedProject_KeepsBothEdgeDirectionsAndItsDeclaredReferencesVerbatim()
    {
        // Arrange — Acme.App sits in the middle: it references Acme.Lib and Contoso.Web references it.
        GraphSummary summary = ThreeProjectSummary();

        // Act
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Acme.App"]);

        // Assert — one project, both of its edges, and the declared reference to the now-out-of-scope
        // Acme.Lib still verbatim (filtering it would erase the declared-versus-observed divergence signal).
        scoped.Projects.Select(p => p.Name).ShouldBe(["Acme.App"]);
        scoped.ProjectEdges.Select(e => (e.Source, e.Target))
            .ShouldBe([("Acme.App", "Acme.Lib"), ("Contoso.Web", "Acme.App")]);
        scoped.Projects.Single().ProjectReferences.ShouldBe(["Acme.Lib"]);
    }

    [Fact]
    public void Scope_NoGlobs_NarrowsNothing()
    {
        // Arrange
        GraphSummary summary = ThreeProjectSummary();

        // Act
        GraphSummary scoped = GraphSummarizer.Scope(summary, []);

        // Assert — the unscoped survey, unchanged.
        scoped.Projects.Select(p => p.Name).ShouldBe(["Acme.App", "Acme.Lib", "Contoso.Web"]);
        scoped.ProjectEdges.Count.ShouldBe(summary.ProjectEdges.Count);
        scoped.ExternalEdges.Count.ShouldBe(summary.ExternalEdges.Count);
    }

    [Fact]
    public void Scope_GlobMatchingNothing_YieldsAnEmptySurvey()
    {
        // Arrange
        GraphSummary summary = ThreeProjectSummary();

        // Act — a glob is matched against the whole project name, so a namespace-shaped prefix hits nothing.
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Fabrikam.*"]);

        // Assert — empty rather than everything, which is what lets a caller refuse an unmatched filter.
        scoped.Projects.ShouldBeEmpty();
        scoped.ProjectEdges.ShouldBeEmpty();
        scoped.ExternalEdges.ShouldBeEmpty();
    }

    // Three projects in a chain — Contoso.Web -> Acme.App -> Acme.Lib — with one external edge, out of
    // Acme.App only (System.Exception as a base type), so every scoping row below is hand-verifiable.
    private static GraphSummary ThreeProjectSummary()
    {
        CompilationInput lib = CompilationFactory.Compile("Acme.Lib", ("Lib.cs", """
                                                                                 namespace Acme.Lib;
                                                                                 public class Service {}
                                                                                 """));
        CompilationInput app = CompilationFactory.CompileReferencing("Acme.App", lib.Compilation, "Acme.Lib", ("App.cs", """
                                                                                                                         namespace Acme.App;
                                                                                                                         public class Client { public Acme.Lib.Service S; }
                                                                                                                         public class Failure : System.Exception {}
                                                                                                                         """));
        CompilationInput web = CompilationFactory.CompileReferencing("Contoso.Web", app.Compilation, "Acme.App", ("Web.cs", """
                                                                                                                            namespace Contoso.Web;
                                                                                                                            public class Controller { public Acme.App.Client C; }
                                                                                                                            """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app, web]);
        return GraphSummarizer.Summarize(model);
    }
}
