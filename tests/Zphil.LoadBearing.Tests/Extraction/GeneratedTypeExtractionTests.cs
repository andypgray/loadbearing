using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Source-generator output on the extraction fast path: the ratified §4.1 boundary (a project noun
///     names what the assembly declares, generated types included) and the <c>IsGenerated</c> fact
///     <c>.Authored()</c> filters on (GRAMMAR §5.2). Detection is
///     <c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> on the type or on any type containing it —
///     nothing else — so the two halves pinned here are the containing-type walk and the partial case,
///     where the attribute sits on the generated <em>method</em> and the author's own class stays authored.
/// </summary>
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
        var inAuthoredNamespace = Names(evaluator.Evaluate(arch.Namespace("App.*"), SelectionPosition.Subject));
        var inProject = Names(evaluator.Evaluate(arch.Project("TestProject"), SelectionPosition.Subject));

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
}
