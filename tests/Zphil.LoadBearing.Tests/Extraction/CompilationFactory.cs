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

    // The full trusted-platform-assemblies set for the DI-aware overloads: the shared-framework assemblies
    // plus the test project's own dependency assemblies (deployed to the output directory), which include the
    // real Microsoft.Extensions.DependencyInjection.Abstractions (IServiceCollection, ServiceLifetime, the
    // Add*/TryAdd* extensions) and Microsoft.Extensions.Hosting.Abstractions (IHostedService, AddHostedService)
    // that the RegistrationRecognizer's symbol-first gate resolves against. Referencing the whole set is the
    // robust way to give the metadata reference closure everything the abstractions' public signatures bind to.
    private static readonly MetadataReference[] DiReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => path.Length > 0)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    // The same set minus Microsoft.Extensions.Hosting.Abstractions, which defines IHostedService — so
    // GetTypeByMetadataName("...IHostedService") returns null and AddHostedService (supplied as a source stub)
    // exercises the recognizer's implementation-only fallback (GRAMMAR §4.7).
    private static readonly MetadataReference[] DiReferencesNoHosting = DiReferences
        .Where(reference => reference is not PortableExecutableReference { FilePath: { } path }
                            || !path.EndsWith("Microsoft.Extensions.Hosting.Abstractions.dll", StringComparison.OrdinalIgnoreCase))
        .ToArray();

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

    /// <summary>
    ///     Extract a model from a <see cref="OutputKind.ConsoleApplication" /> compilation — the output kind
    ///     that makes Roslyn synthesize the top-level-statements <c>Program</c> entry point (a library has no
    ///     entry point, so this is the only way to exercise it on the fast path).
    /// </summary>
    public static CodebaseModel ExtractConsoleApp(params (string Path, string Source)[] files)
    {
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path)).ToArray();
        var compilation = CSharpCompilation.Create(
            "TestProject", trees, [CoreLib], new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        return CodebaseExtractor.ExtractFromCompilations([new CompilationInput(compilation, "TestProject", [])]);
    }

    /// <summary>Multi-file convenience: extract a model from several files in one project.</summary>
    public static CodebaseModel Extract(string projectName, params (string Path, string Source)[] files)
    {
        return CodebaseExtractor.ExtractFromCompilations([Compile(projectName, files)]);
    }

    /// <summary>
    ///     Extract from a compilation with NO metadata references — not even the core library — so
    ///     <c>Compilation.GetTypeByMetadataName</c> cannot resolve BCL types like <c>System.Exception</c>.
    ///     Exercises the defensive null-lookup guards (a synthesized-type lookup that returns null mints
    ///     nothing rather than throwing).
    /// </summary>
    public static CodebaseModel ExtractWithoutReferences(params (string Path, string Source)[] files)
    {
        CSharpCompilation compilation = CreateCompilation("NoRefs", [], files);
        return CodebaseExtractor.ExtractFromCompilations([new CompilationInput(compilation, "NoRefs", [])]);
    }

    /// <summary>
    ///     A single-project input compiled against the DI/Hosting abstractions (see <see cref="DiReferences" />),
    ///     so <c>AddSingleton&lt;IFoo, Foo&gt;()</c>, <c>AddHostedService&lt;T&gt;()</c>, and the rest bind to
    ///     the real framework symbols the registration recognizer (GRAMMAR §4.7) gates on. EF Core and
    ///     Microsoft.Extensions.Http are absent repo-wide, so <c>AddDbContext</c> / <c>AddHttpClient</c> tests
    ///     add source stubs (a class in namespace <c>Microsoft.Extensions.DependencyInjection</c> with the right
    ///     signature) as extra files — recognition is namespace + name + first-parameter, so a stub is faithful.
    /// </summary>
    public static CompilationInput CompileWithDi(string projectName, params (string Path, string Source)[] files)
    {
        return new CompilationInput(CreateCompilation(projectName, DiReferences, files), projectName, []);
    }

    /// <summary>Single-project convenience: extract a model from DI-referencing source in project <c>TestProject</c>.</summary>
    public static CodebaseModel ExtractWithDi(params (string Path, string Source)[] files)
    {
        return CodebaseExtractor.ExtractFromCompilations([CompileWithDi("TestProject", files)]);
    }

    /// <summary>
    ///     As <see cref="ExtractWithDi" /> but with Microsoft.Extensions.Hosting.Abstractions absent, so
    ///     <c>IHostedService</c> does not resolve — for the <c>AddHostedService</c> implementation-only
    ///     fallback (the call is supplied as a source stub, since the real extension lives in the removed
    ///     assembly).
    /// </summary>
    public static CodebaseModel ExtractWithDiNoHosting(params (string Path, string Source)[] files)
    {
        CSharpCompilation compilation = CreateCompilation("TestProject", DiReferencesNoHosting, files);
        return CodebaseExtractor.ExtractFromCompilations([new CompilationInput(compilation, "TestProject", [])]);
    }

    /// <summary>
    ///     Extract a model from a <see cref="OutputKind.ConsoleApplication" /> compilation against the
    ///     DI/Hosting abstractions — the output kind that makes Roslyn synthesize the top-level-statements
    ///     <c>Program</c>, so a registration call in top-level statements (the most common composition root,
    ///     not a declared type) is exercised through the whole-compilation registration walk.
    /// </summary>
    public static CodebaseModel ExtractConsoleAppWithDi(params (string Path, string Source)[] files)
    {
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path)).ToArray();
        var compilation = CSharpCompilation.Create(
            "TestProject", trees, DiReferences, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        return CodebaseExtractor.ExtractFromCompilations([new CompilationInput(compilation, "TestProject", [])]);
    }
}