using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Builds <see cref="CompilationInput" />s and <see cref="CodebaseModel" />s from source strings
///     for the MSBuild-free fast path. Each source is parsed with an explicit file path so edge and
///     declaration sites carry a stable, assertable path; the only metadata reference is the runtime
///     core library (so BCL targets resolve, keeping predefined-type keywords out of the model).
/// </summary>
internal static class CompilationFactory
{
    private static readonly MetadataReference CoreLib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    public static CompilationInput Compile(string projectName, params (string Path, string Source)[] files)
    {
        return new CompilationInput(CreateCompilation(projectName, [CoreLib], files), projectName, []);
    }

    public static CompilationInput Compile(string projectName, string source)
    {
        return Compile(projectName, ("Test.cs", source));
    }

    /// <summary>A second-project input that references <paramref name="referenced" /> as metadata.</summary>
    public static CompilationInput CompileReferencing(
        string projectName, Compilation referenced, string referencedProjectName, params (string Path, string Source)[] files)
    {
        return new CompilationInput(CreateCompilation(projectName, [CoreLib, referenced.ToMetadataReference()], files), projectName, [referencedProjectName]);
    }

    public static CSharpCompilation CreateCompilation(
        string name, IReadOnlyList<MetadataReference> references, params (string Path, string Source)[] files)
    {
        var trees = files
            .Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path))
            .ToArray();
        return CSharpCompilation.Create(name, trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>Single-source convenience: extract a model from one file in project <c>TestProject</c>.</summary>
    public static CodebaseModel Extract(string source)
    {
        return CodebaseExtractor.ExtractFromCompilations([Compile("TestProject", source)]);
    }

    /// <summary>Multi-file convenience: extract a model from several files in one project.</summary>
    public static CodebaseModel Extract(string projectName, params (string Path, string Source)[] files)
    {
        return CodebaseExtractor.ExtractFromCompilations([Compile(projectName, files)]);
    }
}