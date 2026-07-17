using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     Maps finding F1's shape — a spec that <c>typeof()</c>s a NuGet-packaged type whose assembly a
///     plain framework-dependent class-library build never stages into <c>bin</c>, so JIT-compiling
///     <c>Define()</c> throws <see cref="FileNotFoundException" /> for the missing package — into an
///     actionable <see cref="UserErrorException" /> naming the spec, the unresolved dependency, the
///     cause, and both remedies. Both host surfaces render the result message-only (the CLI top-level
///     handler to stderr, the MCP <c>GlobalCallToolFilter</c> to the client) and exit 2 either way.
/// </summary>
internal static class SpecDependencyLoadFailure
{
    /// <summary>
    ///     True when the failure is a missing dependency assembly rather than a spec's own file I/O:
    ///     an assembly-load <see cref="FileNotFoundException" /> carries the full assembly display
    ///     identity in <see cref="FileNotFoundException.FileName" />, so <c>", Version="</c> is the
    ///     loader's signature and never appears in a plain file path.
    /// </summary>
    internal static bool IsAssemblyLoadFailure(FileNotFoundException exception)
    {
        return exception.FileName is not null
               && exception.FileName.Contains(", Version=", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Renders the actionable message, keeping the original <see cref="FileNotFoundException" /> as
    ///     the inner exception. Call only when <see cref="IsAssemblyLoadFailure" /> holds, so
    ///     <see cref="FileNotFoundException.FileName" /> is the assembly identity to report.
    /// </summary>
    internal static UserErrorException Map(FileNotFoundException exception, string specDllPath)
    {
        string spec = Path.GetFileNameWithoutExtension(specDllPath);
        string? identity = exception.FileName;
        string message =
            $"The spec assembly '{spec}' failed to load its dependency '{identity}' while running Define().\n" +
            "A class-library build does not stage NuGet package assemblies into its output, so a spec that names a packaged type via typeof() builds clean but cannot run.\n" +
            "Add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj and rebuild, or target the type with a namespace pattern (arch.Namespace(...)), which needs no assembly load.";
        return new UserErrorException(message, exception);
    }
}