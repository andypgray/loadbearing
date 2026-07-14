using System.Reflection;
using System.Runtime.Loader;

namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     A collectible <see cref="AssemblyLoadContext" /> that isolates a prebuilt spec DLL — the
///     host-side half of DESIGN.md open question (b), now living in the CLI (Phase 3; it started in
///     the test project). The type-identity fix is the shared-contract short-circuit:
///     <c>Zphil.LoadBearing</c> resolves from the Default context so <c>spec is IArchitectureSpec</c>
///     holds across the boundary; everything else resolves through the dependency resolver.
/// </summary>
internal sealed class SpecLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    internal SpecLoadContext(string mainAssemblyPath)
        : base(true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "Zphil.LoadBearing")
            // Fall back to the Default context so the shared contract type is identical on both sides.
            return null;

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }
}