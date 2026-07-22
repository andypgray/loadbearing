using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Compiles a one-off <see cref="IArchitectureSpec" /> assembly from C# source and emits it to disk —
///     the test-time analog of the committed fixture spec projects, for cases that need a spec DLL carrying
///     a rule the golden-pinned fixtures do not (e.g. a warm-MCP pin over a freshly-built verb). The
///     compilation references the full trusted-platform-assembly set plus the in-process
///     <c>Zphil.LoadBearing</c> core, so the spec binds to the SAME contract types the host loaded;
///     <c>SpecLoadContext</c> then resolves <c>Zphil.LoadBearing</c> from the Default context, keeping type
///     identity across the load boundary. The emitted DLL needs no <c>.deps.json</c> because it depends
///     only on the core and the BCL — both resolvable from the running runtime.
/// </summary>
internal static class SpecAssemblyCompiler
{
    private static readonly MetadataReference[] References = BuildReferences();

    /// <summary>
    ///     Compiles <paramref name="source" /> to a spec DLL at <paramref name="outputPath" /> under
    ///     <paramref name="assemblyName" />; throws <see cref="InvalidOperationException" /> listing every
    ///     compile error on failure.
    /// </summary>
    public static void EmitSpecDll(string source, string outputPath, string assemblyName)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        EmitResult result = compilation.Emit(outputPath);
        if (result.Success) return;

        string errors = string.Join(
            "\n",
            result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()));
        throw new InvalidOperationException($"Spec compilation failed:\n{errors}");
    }

    // The shared-framework + deployed-assembly reference closure (the same TRUSTED_PLATFORM_ASSEMBLIES set
    // CompilationFactory uses), with the in-process core added if the runtime did not already list it — so
    // typeof(Arch)'s assembly is always resolvable and never duplicated.
    private static MetadataReference[] BuildReferences()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .ToList();

        string corePath = typeof(Arch).Assembly.Location;
        if (!paths.Any(path => string.Equals(path, corePath, StringComparison.OrdinalIgnoreCase)))
            paths.Add(corePath);

        return paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray();
    }
}