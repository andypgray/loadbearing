using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Checker semantics for <c>.Authored()</c> (GRAMMAR §5.2): the adjective drops every type whose
///     <c>IsGenerated</c> fact is set and keeps everything else, on a plain selection, on a union (whose
///     adjectives apply to the unioned set, §5.1), and on the project noun whose §4.1 breadth it exists to
///     narrow. The codebase is extracted from a real generator run, so the fact under test is the one
///     extraction produces rather than a hand-set flag.
/// </summary>
public sealed class AuthoredAdjectiveTests
{
    private static readonly CodebaseModel Codebase = CompilationFactory.ExtractConsoleAppWithGenerator(
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

                           public sealed class Gizmo
                           {
                           }
                       }
                       """));

    [Fact]
    public void ApplyAdjective_Authored_DropsGeneratedTypesAndKeepsAuthored()
    {
        // Arrange
        var arch = new Arch();
        Selection everything = arch.Types;

        // Act
        IReadOnlyList<string> unnarrowed = Names(everything);
        IReadOnlyList<string> authored = Names(everything.Authored());

        // Assert — exactly the generated pair goes, and nothing an author wrote goes with it.
        unnarrowed.ShouldContain("Gen.Emitted");
        unnarrowed.ShouldContain("Gen.Emitted.Inner");
        unnarrowed.Except(authored)
            .ShouldBe(["Gen.Emitted", "Gen.Emitted.Inner"], ignoreOrder: true);
    }

    [Fact]
    public void ApplyAdjective_AuthoredOnUnion_AppliesAfterTheSetUnion()
    {
        // Arrange — a union owns its adjectives rather than distributing them (GRAMMAR §5.1), so this
        // reaches the evaluator's union branch, a different code path from the single-selection one.
        var arch = new Arch();
        Selection union = arch.AnyOf(arch.Namespace("App.*"), arch.Namespace("Gen.*"));

        // Act
        IReadOnlyList<string> unnarrowed = Names(union);
        IReadOnlyList<string> authored = Names(union.Authored());

        // Assert
        unnarrowed.ShouldBe(["App.Gizmo", "App.Widget", "Gen.Emitted", "Gen.Emitted.Inner"], ignoreOrder: true);
        authored.ShouldBe(["App.Gizmo", "App.Widget"], ignoreOrder: true);
    }

    [Fact]
    public void ApplyAdjective_AuthoredOnProjectNoun_KeepsSynthesizedProgram()
    {
        // Arrange — the project noun is the §4.1 breadth `.Authored()` narrows: it names everything the
        // assembly declares. Narrowing must take the generator's output and nothing else, so the
        // synthesized top-level-statements Program — which no generator emitted — stays a subject.
        var arch = new Arch();

        // Act
        IReadOnlyList<string> authored = Names(arch.Project("TestProject")
            .Authored());

        // Assert
        authored.ShouldContain("Program");
        authored.ShouldNotContain("Gen.Emitted");
        authored.ShouldNotContain("Gen.Emitted.Inner");
    }

    private static IReadOnlyList<string> Names(Selection selection)
    {
        var evaluator = new SelectionEvaluator(Codebase);
        return evaluator.Evaluate(selection, SelectionPosition.Subject)
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    // An attributed container with an attribute-free nested type: two generated types from one attribute,
    // which is what makes the containing-type walk observable in the checker.
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
                                             """;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx => ctx.AddSource("Emitted.g.cs", EmittedSource));
        }
    }
}
