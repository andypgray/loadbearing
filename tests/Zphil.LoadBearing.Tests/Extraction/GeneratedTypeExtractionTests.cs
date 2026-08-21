using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Source-generator output on the extraction fast path: the ratified §4.1 boundary (a project noun
///     names what the assembly declares, generated types included) and the <c>IsGenerated</c> fact
///     <c>.Authored()</c> filters on (GRAMMAR §5.2). Detection reads three signals —
///     <c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> on the type or on any type containing it, the
///     tree's provenance as a source-generated document, and the auto-generated banner comment — with the
///     two file signals lifting to a type only when they hold of <em>every</em> declaring file.
/// </summary>
/// <remarks>
///     Four halves are pinned here: the containing-type walk; the partial case where the attribute sits on
///     the generated <em>method</em> and the author's own class stays authored; each file signal on its own,
///     separated by running one generator twice through <c>CompilationFactory</c>'s provenance-reporting
///     overload, once with provenance and once without; and the guard that keeps metadata types — which
///     declare no syntax at all, so an all-parts test over an empty sequence would answer
///     <see langword="true" /> — authored.
/// </remarks>
public sealed class GeneratedTypeExtractionTests
{
    // Authored sources: top-level statements (so a synthesized Program rides along), one ordinary type, and
    // one partial class whose implementing part the generator supplies — the [GeneratedRegex] shape.
    private static readonly CodebaseModel Model = CompilationFactory.ExtractConsoleAppWithGenerator(
        new EmittingGenerator(),
        ("Program.cs", """
                       using App;

                       Widget.Spin();

                       namespace App
                       {
                           public static class Widget
                           {
                               public static void Spin()
                               {
                               }
                           }

                           public partial class Holder
                           {
                               public static partial string Emit();
                           }
                       }
                       """));

    // The file-signal fixture, run twice over one generator. The generator emits three trees carrying no
    // attribute anywhere: one led by a banner, one marked nothing at all, and two halves of a partial type.
    // With provenance reported every one of them is generator output; withheld, only the banner speaks.
    private static readonly CodebaseModel ProvenanceModel = FileSignalModel(true);
    private static readonly CodebaseModel BannerOnlyModel = FileSignalModel(false);

    [Fact]
    public void GeneratorOutput_DeclaredType_IsInventoriedWithProjectNameAndNotExternal()
    {
        // Arrange / Act
        TypeNode emitted = Model.Type("Gen.Emitted");

        // Assert — generator output is a solution-declared type of the project that emitted it, not an
        // external node standing in for someone else's assembly.
        emitted.IsExternal.ShouldBeFalse();
        emitted.ProjectName.ShouldBe("TestProject");
        emitted.Kind.ShouldBe(TypeKind.Class);
    }

    [Fact]
    public void GeneratorOutput_DeclaredType_HasNonEmptyFilePaths()
    {
        // Arrange / Act
        TypeNode emitted = Model.Type("Gen.Emitted");

        // Assert — the emitted tree carries a path, so a violation on generated code can still be sited.
        emitted.FilePaths.ShouldNotBeEmpty();
        emitted.FilePaths.ShouldAllBe(path => path.EndsWith("Emitted.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratorOutput_NamespaceGlobSelection_MissesItButProjectNounSeesIt()
    {
        // Arrange — the boundary in one assertion pair: a namespace glob scoped to the author's own
        // namespace never reaches the generator's, but the project noun names everything the assembly
        // declares (GRAMMAR §4.1). That is what `.Authored()` exists to opt out of.
        var arch = new Arch();
        var evaluator = new SelectionEvaluator(Model);

        // Act
        IReadOnlyList<string> inAuthoredNamespace = Names(evaluator.Evaluate(arch.Namespace("App.*"), SelectionPosition.Subject));
        IReadOnlyList<string> inProject = Names(evaluator.Evaluate(arch.Project("TestProject"), SelectionPosition.Subject));

        // Assert
        inAuthoredNamespace.ShouldNotContain("Gen.Emitted");
        inProject.ShouldContain("Gen.Emitted");
    }

    [Fact]
    public void ExtractFacts_GeneratedCodeAttributeOnType_SetsIsGenerated()
    {
        // Arrange / Act / Assert
        Model.Type("Gen.Emitted")
            .IsGenerated.ShouldBeTrue();
    }

    [Fact]
    public void ExtractFacts_NestedTypeInsideGeneratedType_InheritsIsGeneratedViaContainingTypeWalk()
    {
        // Arrange / Act
        TypeNode inner = Model.Type("Gen.Emitted.Inner");

        // Assert — the nested type carries no attribute of its own; the containing-type walk is what
        // makes it generated, which is how a generator's inner types ride along.
        inner.Attributes.ShouldBeEmpty();
        inner.IsGenerated.ShouldBeTrue();
    }

    [Fact]
    public void ExtractFacts_AuthoredTypes_ReportIsGeneratedFalse()
    {
        // Arrange / Act / Assert — including the synthesized Program, which no attribute reaches.
        Model.Type("App.Widget")
            .IsGenerated.ShouldBeFalse();
        Model.Type("Program")
            .IsGenerated.ShouldBeFalse();
    }

    [Fact]
    public void ExtractFacts_PartialTypeWithAuthoredAndGeneratedParts_IsGeneratedFalseWithoutTypeLevelAttribute()
    {
        // Arrange / Act
        TypeNode holder = Model.Type("App.Holder");

        // Assert — two parts merge into one symbol, and because the generator attributed the METHOD rather
        // than the type, the author's own class stays authored. This is the [GeneratedRegex] case.
        holder.FilePaths.Count.ShouldBe(2);
        holder.IsGenerated.ShouldBeFalse();
    }

    [Fact]
    public void ExtractFacts_ExternalMetadataTypes_StayAuthored()
    {
        // Arrange — the guard that makes the all-parts rule safe. A metadata symbol has no declaring syntax
        // at all, and "every declaring file is generated" over an empty sequence is vacuously true, so
        // without the length check the entire referenced world reports generated.
        IReadOnlyList<TypeNode> externals = Model.Types
            .Where(type => type.IsExternal)
            .ToList();

        // Act / Assert — asserted over every external rather than a named one, because the failure this
        // guards against is indiscriminate: it takes all of them at once or none.
        externals.ShouldNotBeEmpty();
        externals.ShouldAllBe(type => !type.IsGenerated);
    }

    [Fact]
    public void ExtractFacts_BannerWithoutAttributeOrProvenance_IsGenerated()
    {
        // Arrange / Act / Assert — the banner alone, with nothing else to go on. This is the Razor shape:
        // a compiled view carries a banner, no [GeneratedCode] and no [CompilerGenerated], so the attribute
        // arm cannot see it and a whole view tier would read as hand-written code.
        BannerOnlyModel.Type("Gen.Bannered")
            .IsGenerated.ShouldBeTrue();
    }

    [Fact]
    public void ExtractFacts_ProvenanceWithoutAttributeOrBanner_IsGenerated()
    {
        // Arrange / Act / Assert — neither marker in the source; the workspace reporting the tree as
        // generator output is the whole signal.
        ProvenanceModel.Type("Gen.Unmarked")
            .IsGenerated.ShouldBeTrue();
    }

    [Fact]
    public void ExtractFacts_SameUnmarkedSourceWithoutProvenance_IsAuthored()
    {
        // Arrange / Act / Assert — the same generator, the same source, provenance withheld. Reading true
        // above and false here is what proves the provenance arm moved it and no other signal did.
        BannerOnlyModel.Type("Gen.Unmarked")
            .IsGenerated.ShouldBeFalse();
    }

    [Fact]
    public void ExtractFacts_PartialTypeWithEveryPartGenerated_IsGenerated()
    {
        // Arrange / Act
        TypeNode split = ProvenanceModel.Type("Gen.Split");

        // Assert — the other side of App.Holder. Both parts are generator output, so the file signal holds
        // of every file declaring the type and lifts to the type; nobody can act on a violation here.
        split.FilePaths.Count.ShouldBe(2);
        split.IsGenerated.ShouldBeTrue();
    }

    private static IReadOnlyList<string> Names(IEnumerable<TypeNode> types)
    {
        return types.Select(type => type.FullName)
            .ToList();
    }

    // Emits both halves of the fixture in one post-initialization output: an attributed container with an
    // attribute-free nested type, and the implementing part of the author's partial class with the
    // attribute on the method (never the type).
    private sealed class EmittingGenerator : IIncrementalGenerator
    {
        private const string EmittedSource = """
                                             namespace Gen
                                             {
                                                 [System.CodeDom.Compiler.GeneratedCode("Test", "1.0")]
                                                 public static class Emitted
                                                 {
                                                     public sealed class Inner
                                                     {
                                                     }
                                                 }
                                             }

                                             namespace App
                                             {
                                                 public partial class Holder
                                                 {
                                                     [System.CodeDom.Compiler.GeneratedCode("Test", "1.0")]
                                                     public static partial string Emit() => "emitted";
                                                 }
                                             }
                                             """;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx => ctx.AddSource("Emitted.g.cs", EmittedSource));
        }
    }

    // One authored file beside the file-signal generator's four, so each model still has an authored
    // population to read the generated one against.
    private static CodebaseModel FileSignalModel(bool reportProvenance)
    {
        return CompilationFactory.ExtractConsoleAppWithGenerator(
            new FileSignalGenerator(),
            reportProvenance,
            ("Program.cs", """
                           using App;

                           Gauge.Read();

                           namespace App
                           {
                               public static class Gauge
                               {
                                   public static void Read()
                                   {
                                   }
                               }
                           }
                           """));
    }

    // Four trees carrying no attribute anywhere, so every verdict they earn comes from a FILE signal. One is
    // led by a banner the way a compiled Razor view is; the rest are marked nothing at all, which is the
    // shape only provenance can see. The last two are halves of one partial type — the case the all-parts
    // rule must answer generated, as against App.Holder above, whose author wrote one of its halves.
    private sealed class FileSignalGenerator : IIncrementalGenerator
    {
        private const string BanneredSource = """
                                              // <auto-generated/>
                                              namespace Gen
                                              {
                                                  public sealed class Bannered
                                                  {
                                                  }
                                              }
                                              """;

        private const string UnmarkedSource = """
                                              namespace Gen
                                              {
                                                  public sealed class Unmarked
                                                  {
                                                  }
                                              }
                                              """;

        private const string FirstSplitPart = """
                                              namespace Gen
                                              {
                                                  public sealed partial class Split
                                                  {
                                                      public int First { get; set; }
                                                  }
                                              }
                                              """;

        private const string SecondSplitPart = """
                                               namespace Gen
                                               {
                                                   public sealed partial class Split
                                                   {
                                                       public int Second { get; set; }
                                                   }
                                               }
                                               """;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx =>
            {
                ctx.AddSource("Bannered.g.cs", BanneredSource);
                ctx.AddSource("Unmarked.g.cs", UnmarkedSource);
                ctx.AddSource("Split.First.g.cs", FirstSplitPart);
                ctx.AddSource("Split.Second.g.cs", SecondSplitPart);
            });
        }
    }
}
