using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     Maps the two ways a <c>typeof()</c> anchor can fail to resolve while <c>Define()</c> runs — a
///     <see cref="FileNotFoundException" /> for the anchored type's assembly, a
///     <see cref="TypeLoadException" /> for the type itself — into an actionable
///     <see cref="UserErrorException" /> naming the spec, what could not be loaded, the cause, and the
///     remedy that applies.
/// </summary>
/// <remarks>
///     Both host surfaces render the result message-only (the CLI top-level handler to stderr, the MCP
///     <c>GlobalCallToolFilter</c> to the client) and exit 2 either way.
/// </remarks>
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
    /// <remarks>
    ///     The remedy line names both worlds because the host cannot tell them apart from the identity
    ///     alone, and offering only the packaging fix sends a .NET Framework spec author down a route that
    ///     can never work: a framework reference resolves from the targeting pack or the GAC, so
    ///     <c>CopyLocalLockFileAssemblies</c> has nothing to copy.
    /// </remarks>
    internal static UserErrorException Map(FileNotFoundException exception, string specDllPath)
    {
        string spec = Path.GetFileNameWithoutExtension(specDllPath);
        string? identity = exception.FileName;
        string message =
            $"The spec assembly '{spec}' failed to load its dependency '{identity}' while running Define().\n" +
            "A typeof() anchor loads its type's assembly, and this one is not beside the spec DLL: a class-library build does not stage NuGet package assemblies into its output, and a .NET Framework reference assembly resolves from the targeting pack or the GAC and is never staged at all.\n" +
            "If it is a package, add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj and rebuild; if it is a .NET Framework assembly, no build setting can help — target the type with a namespace pattern (arch.Namespace(...)), which needs no assembly load.";
        return new UserErrorException(message, exception);
    }

    /// <summary>
    ///     Renders the actionable message for a type that could not be loaded at all, keeping the original
    ///     <see cref="TypeLoadException" /> as the inner exception. Unlike the assembly case there is no
    ///     packaging remedy to offer: the type exists only on .NET Framework, so the pattern anchor is the
    ///     whole answer.
    /// </summary>
    internal static UserErrorException Map(TypeLoadException exception, string specDllPath)
    {
        string spec = Path.GetFileNameWithoutExtension(specDllPath);
        string type = string.IsNullOrWhiteSpace(exception.TypeName) ? "a type" : $"the type '{exception.TypeName}'";
        string message =
            $"The spec assembly '{spec}' could not load {type} while running Define().\n" +
            "A typeof() anchor loads its type's whole closure, base types and implemented interfaces included, and this one reaches an assembly that does not exist on .NET — so the anchor cannot resolve however the spec project is built.\n" +
            "Target the type with a namespace pattern (arch.Namespace(...)) instead, which needs no assembly load.";
        return new UserErrorException(message, exception);
    }
}
