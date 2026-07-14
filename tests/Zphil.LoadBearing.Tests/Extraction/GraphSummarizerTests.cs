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
}