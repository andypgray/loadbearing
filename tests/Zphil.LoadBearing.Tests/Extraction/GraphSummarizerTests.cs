using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     <see cref="GraphSummarizer" /> facts over the MSBuild-free fast path: cross-project edge grouping
///     and type-pair counts, same-project exclusion, declared-vs-observed divergence, external grouping by
///     namespace root (two-segment, one-segment, and global), namespace inventory counts, and
///     deterministic ordering. Sources are controlled so the counts are hand-verifiable; the golden CLI
///     test proves the same summary shape over the real MyApp solution.
/// </summary>
/// <remarks>
///     The scoping rows below pin <see cref="GraphSummarizer.Scope" />'s asymmetry: a project edge
///     survives on <em>either</em> endpoint, an external edge on its source alone, and a surviving
///     project keeps its declared references verbatim. Both coverage statements take the either-end
///     rule too — a multiply-declared type on any of its declarers, a shadowed one on its declarer or
///     any of the projects binding the assembly instead.
/// </remarks>
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
        summary.Projects.Select(p => p.Name)
            .ShouldBe(["App", "Lib"]);
        summary.ProjectEdges.Select(e => (e.Source, e.Target, e.References))
            .ShouldBe([("App", "Lib", 2)]);
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
        summary.Projects.Single(p => p.Name == "App")
            .ProjectReferences.ShouldBe(["Lib"]);
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
    public void Summarize_NoGeneratedTypes_CountsZeroAtEveryLevel()
    {
        // Arrange — the case almost every project is in, and the one the omit-at-zero rendering rests on.
        CodebaseModel model = CompilationFactory.Extract("Plain", ("Plain.cs", """
                                                                               namespace Plain { public class Thing {} }
                                                                               """));

        // Act
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert
        ProjectSummary plain = summary.Projects.Single();
        plain.Generated.ShouldBe(0);
        plain.Namespaces.ShouldAllBe(n => n.Generated == 0);
    }

    [Fact]
    public void Summarize_GeneratedTypes_CountThemPerProjectAndPerNamespace()
    {
        // Arrange — a real generator run, so the count is over the fact extraction produced rather than one
        // this test set. Three namespace shapes at once: wholly generated, part generated, none.
        CodebaseModel model = CompilationFactory.ExtractConsoleAppWithGenerator(
            new SurveyGenerator(),
            ("Authored.cs", """
                            namespace Mixed { public class Written {} }
                            namespace Plain { public class Thing {} }
                            """));

        // Act
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert — the project count is the sum of the namespace counts, because a generated type is a
        // subset of the project's types rather than a population beside them.
        ProjectSummary project = summary.Projects.Single();
        project.Types.ShouldBe(5);
        project.Generated.ShouldBe(3);
        project.Namespaces.Select(n => (n.Namespace, n.Types, n.Generated))
            .ShouldBe([("Gen.Whole", 2, 2), ("Mixed", 2, 1), ("Plain", 1, 0)]);
    }

    [Fact]
    public void Summarize_MultiTargetedProject_CarriesItsFrameworksAndTheWinnerThrough()
    {
        // Arrange — one project name, two compilations, both declaring the same type: the collapse the pair
        // exists to state. This path takes its inputs as given, so the winner is the first of them; it is
        // the WORKSPACE path that hands frameworks over ordinal, and MultiTargetFrameworkTests pins that
        // against the real two-framework fixture. The list is ordinal either way — the merge sorts it.
        CompilationInput modern = Framework("net10.0");
        CompilationInput legacy = Framework("netstandard2.0");

        // Act
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert — both frameworks, and the one whose facts the shared Widget carries.
        ProjectSummary shared = summary.Projects.Single();
        shared.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        shared.FactsFollow.ShouldBe("net10.0");
    }

    [Fact]
    public void Summarize_SingleTargetedProject_SaysNothingAboutFrameworks()
    {
        // Arrange — the case every project of an ordinary solution is in, and the one the omit-when-empty
        // rendering rests on: one compilation, so the project name already says where every fact came from.
        CodebaseModel model = CompilationFactory.Extract("Plain", ("Plain.cs", """
                                                                               namespace Plain { public class Thing {} }
                                                                               """));

        // Act
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert
        ProjectSummary plain = summary.Projects.Single();
        plain.TargetFrameworks.ShouldBeEmpty();
        plain.FactsFollow.ShouldBeNull();
    }

    [Fact]
    public void Summarize_MultiTargetedProjectWhoseFrameworksShareNoType_NamesThemAndNoWinner()
    {
        // Arrange — two frameworks of one project declaring disjoint types, the shape a #if-guarded class
        // makes. Nothing collapsed, so nothing was displaced and there is no winner to name; the framework
        // list alone still says the project compiles twice, which is the fact a rule author needs.
        CompilationInput legacy = CompilationFactory.Compile("Shared", ("Legacy.cs", """
                                                                                     namespace Shared { public class LegacyOnly {} }
                                                                                     """)) with
        {
            TargetFramework = "netstandard2.0"
        };
        CompilationInput modern = CompilationFactory.Compile("Shared", ("Modern.cs", """
                                                                                     namespace Shared { public class ModernOnly {} }
                                                                                     """)) with
        {
            TargetFramework = "net10.0"
        };

        // Act
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([legacy, modern]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert
        ProjectSummary shared = summary.Projects.Single();
        shared.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        shared.FactsFollow.ShouldBeNull();
    }

    [Fact]
    public void Summarize_MixedSolutionMembership_CarriesEachProjectsLabelThrough()
    {
        // Arrange — App is declared, Lib is a passenger, and Loose was extracted with nothing read about it.
        CompilationInput lib = CompilationFactory.Compile("Lib", ("Lib.cs", """
                                                                            namespace Lib;
                                                                            public class Service {}
                                                                            """)) with
        {
            SolutionMember = false
        };
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib", ("App.cs", """
                                                                                                               namespace App;
                                                                                                               public class Client { public Lib.Service S; }
                                                                                                               """)) with
        {
            SolutionMember = true
        };
        CompilationInput loose = CompilationFactory.Compile("Loose", ("Loose.cs", """
                                                                                  namespace Loose;
                                                                                  public class Thing {}
                                                                                  """));

        // Act
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app, loose]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert — all three survive the survey; only the label differs. Dropping the passenger here would
        // hide the one project a reader opened the survey to find.
        summary.Projects.Select(p => (p.Name, p.SolutionMember))
            .ShouldBe([("App", true), ("Lib", false), ("Loose", null)]);
    }

    [Fact]
    public void Scope_MatchedProjects_CarryTheirMembershipLabelVerbatim()
    {
        // Arrange — a passenger and a declared member, both matched by the glob.
        CompilationInput lib = CompilationFactory.Compile("Acme.Lib", ("Lib.cs", """
                                                                                 namespace Acme.Lib;
                                                                                 public class Service {}
                                                                                 """)) with
        {
            SolutionMember = false
        };
        CompilationInput app = CompilationFactory.CompileReferencing("Acme.App", lib.Compilation, "Acme.Lib", ("App.cs", """
                                                                                                                         namespace Acme.App;
                                                                                                                         public class Client { public Acme.Lib.Service S; }
                                                                                                                         """)) with
        {
            SolutionMember = true
        };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        // Act
        GraphSummary scoped = GraphSummarizer.Scope(GraphSummarizer.Summarize(model), ["Acme.*"]);

        // Assert — Scope passes ProjectSummary instances through by reference, so the label rides with them.
        scoped.Projects.Select(p => (p.Name, p.SolutionMember))
            .ShouldBe([("Acme.App", true), ("Acme.Lib", false)]);
    }

    [Fact]
    public void Scope_ProjectGlob_KeepsMatchingProjectsAndTheirExternalEdges()
    {
        // Arrange
        GraphSummary summary = ThreeProjectSummary();

        // Act — '*' spans the dot, so one pattern takes both Acme projects and leaves Contoso.Web out.
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Acme.*"]);

        // Assert — the roster narrows; the one external edge rides with its source project.
        scoped.Projects.Select(p => p.Name)
            .ShouldBe(["Acme.App", "Acme.Lib"]);
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
        scoped.Projects.Select(p => p.Name)
            .ShouldBe(["Acme.Lib"]);
        scoped.ProjectEdges.Select(e => (e.Source, e.Target, e.References))
            .ShouldBe([("Acme.App", "Acme.Lib", 1)]);

        // The documented consequence: the surviving edge names a project the roster does not carry.
        scoped.Projects.Select(p => p.Name)
            .ShouldNotContain("Acme.App");

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
        scoped.Projects.Select(p => p.Name)
            .ShouldBe(["Acme.App"]);
        scoped.ProjectEdges.Select(e => (e.Source, e.Target))
            .ShouldBe([("Acme.App", "Acme.Lib"), ("Contoso.Web", "Acme.App")]);
        scoped.Projects.Single()
            .ProjectReferences.ShouldBe(["Acme.Lib"]);
    }

    [Fact]
    public void Scope_NoGlobs_NarrowsNothing()
    {
        // Arrange
        GraphSummary summary = ThreeProjectSummary();

        // Act
        GraphSummary scoped = GraphSummarizer.Scope(summary, []);

        // Assert — the unscoped survey, unchanged.
        scoped.Projects.Select(p => p.Name)
            .ShouldBe(["Acme.App", "Acme.Lib", "Contoso.Web"]);
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

    // ── Multiply-declared source ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Summarize_ReferenceIntoATypeTheSourceProjectAlsoDeclares_RendersNoProjectEdge()
    {
        // Arrange — Shared.Widget is compiled into BOTH projects (a <Compile Include> link in the real
        // shape). Facts follow Lib, the first declarer, so App's reference to its OWN copy resolves to a
        // node stamped 'Lib' — and used to render as App -> Lib, an edge no project file declares.
        GraphSummary summary = GraphSummarizer.Summarize(LinkedSourceModel());

        // Assert — App declares no ProjectReference to Lib and the survey must not invent one.
        summary.ProjectEdges.ShouldBeEmpty();
    }

    [Fact]
    public void Summarize_GenuineEdgeBetweenTheSamePair_SurvivesWithItsOwnCountAlone()
    {
        // Arrange — App both compiles its own copy of Shared.Widget and genuinely references Lib.Service,
        // which only Lib declares. Suppressing on the project pair rather than on the type pair would drop
        // the real edge with the phantom; keeping the phantom would inflate the count to 2.
        CompilationInput lib = CompilationFactory.Compile("Lib", ("Lib.cs", """
                                                                            namespace Shared { public class Widget {} }
                                                                            namespace Lib { public class Service {} }
                                                                            """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib", ("App.cs", """
                                                                                                               namespace Shared { public class Widget {} }
                                                                                                               namespace App
                                                                                                               {
                                                                                                                   public class Own { public Shared.Widget W; }
                                                                                                                   public class Client { public Lib.Service S; }
                                                                                                               }
                                                                                                               """));

        // Act
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert — one edge, one type pair: App.Client -> Lib.Service.
        summary.ProjectEdges.Select(e => (e.Source, e.Target, e.References))
            .ShouldBe([("App", "Lib", 1)]);
    }

    [Fact]
    public void Summarize_MultiplyDeclaredTypes_NameEveryDeclarerAndTheOneWhoseFactsWon()
    {
        // Arrange — the fact the suppressed edge would otherwise have been the only sign of: without it, a
        // reader cannot see that a rule anchored on App answers from Lib's facts for Shared.Widget.
        GraphSummary summary = GraphSummarizer.Summarize(LinkedSourceModel());

        // Assert — declaredBy carries the winner too, so the entry reads whole, and it is ordinal rather
        // than declarer-order: the roster is a set of names, not a history of which arrived first.
        MultiplyDeclaredTypeSummary widget = summary.MultiplyDeclaredTypes.ShouldHaveSingleItem();
        widget.Type.ShouldBe("Shared.Widget");
        widget.DeclaredBy.ShouldBe(["App", "Lib"]);
        widget.FactsFollow.ShouldBe("Lib");
    }

    [Fact]
    public void Summarize_SeveralMultiplyDeclaredTypes_OrdersThemOrdinalByFullName()
    {
        // Arrange — declared out of order in both files, so nothing but the sort can produce the order.
        CompilationInput lib = CompilationFactory.Compile("Lib", ("Lib.cs", """
                                                                            namespace Shared { public class Zebra {} public class Aardvark {} }
                                                                            """));
        CompilationInput app = CompilationFactory.Compile("App", ("App.cs", """
                                                                            namespace Shared { public class Zebra {} public class Aardvark {} }
                                                                            """));

        // Act
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);
        GraphSummary summary = GraphSummarizer.Summarize(model);

        // Assert
        summary.MultiplyDeclaredTypes.Select(t => t.Type)
            .ShouldBe(["Shared.Aardvark", "Shared.Zebra"]);
    }

    [Fact]
    public void Summarize_SolutionWithNoLinkedSource_SaysNothingAboutMultiplyDeclaredTypes()
    {
        // The common case, and the one the survey's optional key rests on: nothing to say, so the list is
        // empty and every existing document is byte-identical.
        GraphSummary summary = ThreeProjectSummary();

        summary.MultiplyDeclaredTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Scope_MultiplyDeclaredType_SurvivesWhenAnyOfItsDeclarersIsInScope()
    {
        // Arrange — Shared.Widget's facts follow Lib; the scope names App, the loser.
        GraphSummary summary = GraphSummarizer.Summarize(LinkedSourceModel());

        // Act
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["App"]);

        // Assert — any-declarer, mirroring the either-endpoint rule the project edges take. Keying on the
        // winner alone would drop the entry from precisely the scope whose author needs it: the reason to
        // read it is that a subject anchored on App answers from Lib's facts for this type.
        scoped.MultiplyDeclaredTypes.Select(t => t.Type)
            .ShouldBe(["Shared.Widget"]);
    }

    [Fact]
    public void Scope_MultiplyDeclaredTypeWithNoDeclarerInScope_IsNarrowedOut()
    {
        // Arrange
        GraphSummary summary = GraphSummarizer.Summarize(LinkedSourceModel());

        // Act — a project that declares nothing shared.
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Other"]);

        // Assert — a complete survey of a smaller subject, so an entry no scoped project declares goes.
        scoped.MultiplyDeclaredTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Scope_ShadowedType_SurvivesWhenABinderIsInScope()
    {
        // Arrange — Product.Tests declares Vendor.Widget and Product binds the package's; the scope names the
        // binder alone. The glob matcher takes a project name whole, so "Product" misses "Product.Tests" and
        // the declaring end is genuinely out of scope rather than swept in by a prefix.
        GraphSummary summary = GraphSummarizer.Summarize(ShadowedSourceModel());

        // Act
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Product"]);

        // Assert — either end keeps the entry, the same rule the project edges take. Keying on the declarer
        // alone would drop it from precisely the scope that needs it: the reader anchored on Product is the
        // one whose reference reaches the assembly rather than the declaration.
        scoped.ShadowedTypes.Select(t => t.Type)
            .ShouldBe(["Vendor.Widget"]);
    }

    [Fact]
    public void Scope_ShadowedTypeWithNeitherEndInScope_IsNarrowedOut()
    {
        // Arrange
        GraphSummary summary = GraphSummarizer.Summarize(ShadowedSourceModel());

        // Act — a project that neither declares the name nor binds the assembly's.
        GraphSummary scoped = GraphSummarizer.Scope(summary, ["Other"]);

        // Assert — a complete survey of a smaller subject, so an entry neither end reaches goes.
        scoped.ShadowedTypes.ShouldBeEmpty();
    }

    // One framework's compilation of the one project, declaring the type both of them declare.
    private static CompilationInput Framework(string targetFramework)
    {
        return CompilationFactory.Compile("Shared", ("Widget.cs", """
                                                                  namespace Shared { public class Widget {} }
                                                                  """)) with
        {
            TargetFramework = targetFramework
        };
    }

    // Two projects compiling one shared type, which is what a <Compile Include> link produces: App declares
    // Shared.Widget itself and references it, and declares NO ProjectReference to Lib. The first declarer in
    // input order wins the facts — this path takes its inputs as given, where the workspace path hands them
    // over ordinal by project name — so App's reference to its own copy resolves to a node stamped 'Lib'.
    private static CodebaseModel LinkedSourceModel()
    {
        CompilationInput lib = CompilationFactory.Compile("Lib", ("Lib.cs", """
                                                                            namespace Shared;
                                                                            public class Widget {}
                                                                            """));
        CompilationInput app = CompilationFactory.Compile("App", ("App.cs", """
                                                                            namespace Shared { public class Widget {} }
                                                                            namespace App { public class Own { public Shared.Widget W; } }
                                                                            """));

        return CodebaseExtractor.ExtractFromCompilations([lib, app]);
    }

    // One name declared twice with the ends split across projects, which is what a stand-in under a package's
    // own namespace produces: Product binds Vendor.Widget out of the assembly, Product.Tests declares its own
    // and references Product. Vendor is handed over as a metadata reference and never extracted — an input
    // would make it a project of the solution, and the split would be cross-project conflation instead.
    // PackageShadowedNameTests pins the DeclaredBy/BoundFromAssemblyBy halves this scoping row rests on.
    private static CodebaseModel ShadowedSourceModel()
    {
        Compilation vendor = CompilationFactory.CreateCompilation(
            "Vendor", [CompilationFactory.CoreLibrary], ("Vendor.cs", """
                                                                      namespace Vendor;
                                                                      public class Widget {}
                                                                      """));

        CompilationInput product = CompilationFactory.CompileAgainstPackages(
            "Product", [vendor], ("Service.cs", """
                                                namespace Product;
                                                public class Service { private Vendor.Widget widget; }
                                                """));

        CompilationInput tests = CompilationFactory.CompileReferencing(
            "Product.Tests", product.Compilation, "Product",
            ("Mocks/Vendor.cs", """
                                namespace Vendor;
                                public class Widget {}
                                """),
            ("WidgetTests.cs", """
                               namespace Product.Tests;
                               public class WidgetTests { private Vendor.Widget stub; }
                               """));

        return CodebaseExtractor.ExtractFromCompilations([product, tests]);
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

    // Three generated types over two namespaces: a wholly generated one, and one type joining an authored
    // namespace so the per-namespace count has a case where it is neither zero nor the whole.
    private sealed class SurveyGenerator : IIncrementalGenerator
    {
        private const string EmittedSource = """
                                             // <auto-generated/>
                                             namespace Gen.Whole
                                             {
                                                 public sealed class First {}
                                                 public sealed class Second {}
                                             }

                                             namespace Mixed
                                             {
                                                 public sealed class Emitted {}
                                             }
                                             """;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx => ctx.AddSource("Survey.g.cs", EmittedSource));
        }
    }
}
