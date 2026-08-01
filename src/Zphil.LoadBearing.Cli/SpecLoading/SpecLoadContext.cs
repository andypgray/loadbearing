using System.Reflection;
using System.Runtime.Loader;

namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     A collectible <see cref="AssemblyLoadContext" /> that isolates a prebuilt spec DLL — the
///     host-side half of loading a spec in isolation, now living in the CLI (it started in the
///     test project). The type-identity fix is the shared-contract short-circuit:
///     <c>Zphil.LoadBearing</c> resolves from the Default context so <c>spec is IArchitectureSpec</c>
///     holds across the boundary; everything else resolves through the dependency resolver.
/// </summary>
/// <remarks>
///     Every assembly this context loads is loaded from its <em>bytes</em>, never its path — see
///     <see cref="LoadWithoutLocking" />. The context is collectible, but the model it produces roots
///     the spec's <c>Type</c> references, so it is never actually collected while that model lives;
///     a path load would therefore hold an OS file lock for the whole host lifetime.
/// </remarks>
internal sealed class SpecLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    internal SpecLoadContext(string mainAssemblyPath)
        : base(true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    /// <summary>
    ///     Loads an assembly from its bytes, so the file is not locked for the rest of this process's
    ///     lifetime. In the long-lived MCP server a path load pinned the spec's build output — the spec
    ///     DLL and the five dependencies beside it — and every <c>dotnet build</c> of the spec project
    ///     then failed with MSB3021/MSB3027 until the server was killed. Killing it is terminal for a
    ///     stdio server (the client never reconnects one), which silently disarmed the per-edit
    ///     <c>arch_check</c> hook for the rest of the session.
    /// </summary>
    /// <remarks>
    ///     A path that is not on disk is handed to the loader rather than opened as a stream, so the failure
    ///     is the loader's own <see cref="FileNotFoundException" /> naming that path. That is the spec DLL
    ///     itself going missing between resolution and load — a rebuild landing underneath a warm host — and
    ///     not the missing-dependency case: <see cref="AssemblyDependencyResolver" /> re-checks the disk, so a
    ///     dependency that has gone resolves to null and the Default context's own failure is what surfaces,
    ///     carrying the assembly display identity <c>SpecDependencyLoadFailure.IsAssemblyLoadFailure</c> keys
    ///     on. Both shapes are pinned by <c>SpecLoadContextFallbackTests</c>.
    ///     <see cref="FileShare" /> admits writers and deleters: a concurrent build may replace the file
    ///     while these bytes are in flight, and the copy already read stays valid.
    /// </remarks>
    internal Assembly LoadWithoutLocking(string path)
    {
        if (!File.Exists(path)) return LoadFromAssemblyPath(path);

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return LoadFromStream(stream);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "Zphil.LoadBearing")
            // Fall back to the Default context so the shared contract type is identical on both sides.
            return null;

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadWithoutLocking(path) : null;
    }
}