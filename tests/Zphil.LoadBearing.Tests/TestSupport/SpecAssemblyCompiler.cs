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
/// <remarks>
///     The <see cref="EmitSpecDll(string,string,string,MetadataReference)" /> overload <em>swaps</em> the
///     contract instead of adding to it, for the one subject that needs a spec built against a contract
///     identity this host does not carry (<see cref="SkewedContract" />). Its reference set is
///     <see cref="PlatformReferences" /> — the same closure with the in-process core filtered out — because
///     leaving the real core beside a stand-in makes every contract type ambiguous at compile time.
/// </remarks>
internal static class SpecAssemblyCompiler
{
    private static readonly string CorePath = typeof(Arch).Assembly.Location;

    // The shared-framework + deployed-assembly reference closure (the same TRUSTED_PLATFORM_ASSEMBLIES set
    // CompilationFactory uses) minus the in-process core, so a caller supplying its own Zphil.LoadBearing
    // never faces two of them. Initialized before HostContract, which is built on top of it.
    private static readonly MetadataReference[] Platform = BuildPlatformReferences();

    private static readonly MetadataReference[] HostContract =
        [..Platform, MetadataReference.CreateFromFile(CorePath)];

    /// <summary>
    ///     The reference closure with the in-process <c>Zphil.LoadBearing</c> removed — what a compilation
    ///     that brings its own contract must build against.
    /// </summary>
    internal static IReadOnlyList<MetadataReference> PlatformReferences => Platform;

    /// <summary>
    ///     Compiles <paramref name="source" /> to a spec DLL at <paramref name="outputPath" /> under
    ///     <paramref name="assemblyName" />, against the contract this host carries; throws
    ///     <see cref="InvalidOperationException" /> listing every compile error on failure.
    /// </summary>
    public static void EmitSpecDll(string source, string outputPath, string assemblyName)
    {
        EmitSpecDll(source, outputPath, assemblyName, HostContract);
    }

    /// <summary>
    ///     <see cref="EmitSpecDll(string,string,string)" /> against <paramref name="contract" /> in place of
    ///     the host's own — the spec's assembly ref then carries whatever identity that reference declares.
    /// </summary>
    public static void EmitSpecDll(string source, string outputPath, string assemblyName, MetadataReference contract)
    {
        EmitSpecDll(source, outputPath, assemblyName, [..Platform, contract]);
    }

    /// <summary>
    ///     <see cref="EmitSpecDll(string,string,string)" />'s image form, against the contract this host
    ///     carries — for a caller that writes one spec into several workspaces and would otherwise pay for
    ///     the same compilation once per copy.
    /// </summary>
    internal static byte[] EmitImage(string source, string assemblyName)
    {
        return EmitImage(source, assemblyName, HostContract);
    }

    /// <summary>
    ///     Compiles <paramref name="source" /> against <paramref name="references" /> and hands back the
    ///     emitted image, for a caller that <em>references</em> the result rather than loads it.
    /// </summary>
    internal static byte[] EmitImage(string source, string assemblyName, IReadOnlyList<MetadataReference> references)
    {
        using var stream = new MemoryStream();
        EmitResult result = Compile(source, assemblyName, references)
            .Emit(stream);
        ThrowOnFailure(result, assemblyName);
        return stream.ToArray();
    }

    private static void EmitSpecDll(string source, string outputPath, string assemblyName, IReadOnlyList<MetadataReference> references)
    {
        EmitResult result = Compile(source, assemblyName, references)
            .Emit(outputPath);
        ThrowOnFailure(result, assemblyName);
    }

    private static CSharpCompilation Compile(string source, string assemblyName, IReadOnlyList<MetadataReference> references)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void ThrowOnFailure(EmitResult result, string assemblyName)
    {
        if (result.Success) return;

        string errors = string.Join(
            "\n",
            result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()));
        throw new InvalidOperationException($"Compilation of '{assemblyName}' failed:\n{errors}");
    }

    private static MetadataReference[] BuildPlatformReferences()
    {
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Where(path => !string.Equals(path, CorePath, StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
