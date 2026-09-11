using System.Security;
using System.Text.Json;
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
    /// <summary>Where the assembly that would not load stands in the spec's own dependency manifest.</summary>
    private enum ManifestMembership
    {
        /// <summary>The manifest names it: a package asset that resolved when the spec was built.</summary>
        Named,

        /// <summary>
        ///     The manifest does not name it, or there is no manifest at all — either way no build setting
        ///     stages it.
        /// </summary>
        Absent,

        /// <summary>The manifest could not be read, so membership decides nothing.</summary>
        Unknown
    }

    /// <summary>The manifest sections that declare loadable managed assemblies.</summary>
    private static readonly string[] RuntimeAssetSections = ["runtime", "runtimeTargets"];

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
    ///     <para>
    ///         The remedy is chosen by <em>structure already on disk</em> rather than by asking the reader to
    ///         classify their own assembly. The spec's <c>.deps.json</c> is the same manifest
    ///         <see cref="System.Runtime.Loader.AssemblyDependencyResolver" /> just failed to resolve through,
    ///         so its membership answers exactly the question the remedy turns on: an assembly the manifest
    ///         names is a package asset and staging it is the fix, while one the manifest does not name cannot
    ///         be staged by any build setting.
    ///     </para>
    ///     <para>
    ///         A .NET <em>shared</em> framework is neither a package nor a .NET Framework assembly, and the
    ///         remedy must not pretend otherwise: under <c>&lt;FrameworkReference&gt;</c> it contributes no
    ///         entry to the manifest and no file to the output at all, so <c>CopyLocalLockFileAssemblies</c>
    ///         has nothing to copy and a spec anchoring <c>typeof(ControllerBase)</c> fails identically with
    ///         the property on and off (measured).
    ///     </para>
    /// </remarks>
    internal static UserErrorException Map(FileNotFoundException exception, string specDllPath)
    {
        string spec = Path.GetFileNameWithoutExtension(specDllPath);
        string? identity = exception.FileName;
        string message =
            $"The spec assembly '{spec}' failed to load its dependency '{identity}' while running Define().\n" +
            RemedyFor(specDllPath, identity);
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

    /// <summary>The cause-and-remedy lines that follow the opening line, one pair per membership.</summary>
    private static string RemedyFor(string specDllPath, string? identity)
    {
        return MembershipOf(specDllPath, identity) switch
        {
            ManifestMembership.Named =>
                "A typeof() anchor loads its type's assembly, and the spec's own dependency manifest (.deps.json) names this one — so it is a package asset that resolved when the spec was built, and it is now neither staged beside the spec DLL nor present in this machine's NuGet package folder.\n" +
                "Add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj and rebuild, so the assembly is staged beside the spec.",
            ManifestMembership.Absent =>
                "A typeof() anchor loads its type's assembly, and this one is not among the spec's own dependencies: the spec's dependency manifest (.deps.json) does not name it, and a .NET Framework spec has no manifest at all. No build setting stages it — it comes from a .NET shared framework pulled in by <FrameworkReference> (Microsoft.AspNetCore.App, Microsoft.WindowsDesktop.App) that this tool's host does not carry, or from a .NET Framework targeting pack or the GAC. CopyLocalLockFileAssemblies has nothing to copy in either case.\n" +
                "Anchor the type by name instead of by typeof(): every typeof() anchor position carries a string overload — .DerivedFrom(\"Microsoft.AspNetCore.Mvc.ControllerBase\") renders identically to the typeof() form — and a string anchor needs no assembly load, as does a namespace pattern (arch.Namespace(...)).",
            _ =>
                "A typeof() anchor loads its type's assembly, and this one is not beside the spec DLL: a class-library build does not stage NuGet package assemblies into its output, and a .NET Framework reference assembly resolves from the targeting pack or the GAC and is never staged at all.\n" +
                "If it is a package, add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj and rebuild; if it is a .NET Framework assembly, no build setting can help — target the type with a namespace pattern (arch.Namespace(...)), which needs no assembly load."
        };
    }

    /// <summary>
    ///     Reads the spec's <c>.deps.json</c> and reports whether it declares a runtime assembly with the
    ///     failed identity's simple name.
    /// </summary>
    /// <remarks>
    ///     A missing manifest is <see cref="ManifestMembership.Absent" /> rather than
    ///     <see cref="ManifestMembership.Unknown" />, because its absence is itself the answer: a spec with no
    ///     manifest declares no dependencies to stage. Everything else that can go wrong reading one —
    ///     unreadable file, malformed JSON, an identity with no simple name — is
    ///     <see cref="ManifestMembership.Unknown" /> and falls back to the both-worlds wording, because this
    ///     runs while rendering a diagnostic and a worse diagnostic beats a second exception. The filter is
    ///     what keeps that narrow: a failure nobody anticipated here still travels.
    /// </remarks>
    private static ManifestMembership MembershipOf(string specDllPath, string? identity)
    {
        string simpleName = SimpleNameOf(identity);
        if (simpleName.Length == 0) return ManifestMembership.Unknown;

        string manifestPath = Path.ChangeExtension(specDllPath, ".deps.json");

        try
        {
            if (!File.Exists(manifestPath)) return ManifestMembership.Absent;

            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            bool named = DeclaredRuntimeAssemblyNames(manifest)
                .Any(name => string.Equals(name, simpleName, StringComparison.OrdinalIgnoreCase));
            return named ? ManifestMembership.Named : ManifestMembership.Absent;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException
                                       or SecurityException
                                       or ArgumentException
                                       or JsonException)
        {
            return ManifestMembership.Unknown;
        }
    }

    /// <summary>
    ///     The simple name out of an assembly display identity — everything ahead of the first comma, which
    ///     is where <c>, Version=</c> begins.
    /// </summary>
    private static string SimpleNameOf(string? identity)
    {
        if (identity is null) return string.Empty;

        int comma = identity.IndexOf(',');
        return (comma < 0 ? identity : identity[..comma]).Trim();
    }

    /// <summary>
    ///     Every runtime assembly the manifest's targets declare, by simple name. The asset keys are the
    ///     package-relative paths the resolver itself maps (<c>lib/net6.0/Newtonsoft.Json.dll</c>), so their
    ///     file names are the identities it can satisfy; <c>runtimeTargets</c> carries the RID-specific ones.
    /// </summary>
    private static IEnumerable<string> DeclaredRuntimeAssemblyNames(JsonDocument manifest)
    {
        if (!manifest.RootElement.TryGetProperty("targets", out JsonElement targets)) yield break;
        if (targets.ValueKind != JsonValueKind.Object) yield break;

        foreach (JsonProperty framework in targets.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object) continue;

            foreach (JsonProperty library in framework.Value.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object) continue;

                foreach (string section in RuntimeAssetSections)
                {
                    if (!library.Value.TryGetProperty(section, out JsonElement assets)) continue;
                    if (assets.ValueKind != JsonValueKind.Object) continue;

                    foreach (JsonProperty asset in assets.EnumerateObject())
                        yield return Path.GetFileNameWithoutExtension(asset.Name);
                }
            }
        }
    }
}
