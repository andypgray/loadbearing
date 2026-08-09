using System.Reflection;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Discovery;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The two spec→model primitives every workspace command needs: <see cref="DiscoverSolution" /> (which
///     solution a run is about) and <see cref="LoadModel" /> (the ALC-loaded, validated model behind a spec
///     DLL). <see cref="CodebaseSource" /> composes them into the one acquisition ladder — discover, acquire
///     the workspace, resolve the spec, load the model — so both live here rather than in it: solution
///     discovery is also what each <see cref="ISolutionSource" /> performs, and <c>explain</c>'s DLL fast path
///     reaches <see cref="LoadModel" /> alone, with no workspace at all
///     (<see cref="SpecResolver.TryResolveWithoutSolution" />).
/// </summary>
internal static class ModelPipeline
{
    /// <summary>
    ///     Discovers the target solution file: an explicit file path, a directory to search, or a
    ///     walk-up from the working directory. Discovery failures become <see cref="UserErrorException" />.
    /// </summary>
    public static string DiscoverSolution(string? solution, string workingDirectory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(solution))
                return Directory.Exists(solution)
                    ? SolutionDiscovery.DiscoverSolution(null, solution)
                    : SolutionDiscovery.DiscoverSolution(solution);

            return SolutionDiscovery.DiscoverSolution(null, workingDirectory);
        }
        catch (FileNotFoundException ex)
        {
            throw new UserErrorException(ex.Message, ex);
        }
        catch (InvalidOperationException ex) when (ex is not UserErrorException)
        {
            throw new UserErrorException(ex.Message, ex);
        }
    }

    /// <summary>
    ///     Loads the spec DLL in a collectible ALC (the <c>Zphil.LoadBearing</c> contract resolves to this
    ///     host's own copy, whatever version the spec was compiled against, so type identity holds),
    ///     discovers and builds the model, then best-effort unloads.
    ///     The returned model roots the spec's <c>Type</c> references, so it stays usable after
    ///     <c>Unload()</c> in this one-shot process.
    /// </summary>
    public static ArchitectureModel LoadModel(string specDllPath)
    {
        var context = new SpecLoadContext(specDllPath);
        try
        {
            Assembly assembly = context.LoadWithoutLocking(specDllPath);
            IReadOnlyList<IArchitectureSpec> specs;
            try
            {
                specs = SpecDiscovery.FindSpecs(assembly);
            }
            catch (SpecDiscoveryException ex)
            {
                throw new UserErrorException(ex.Message, ex);
            }
            catch (ReflectionTypeLoadException ex)
            {
                // GetTypes() couldn't load every type — a missing or version-mismatched dependency of the
                // spec assembly. Surface the distinct loader messages as one user error, not a reflection crash.
                throw new UserErrorException(LoaderFailureMessage(ex, specDllPath), ex);
            }

            try
            {
                return ArchModelBuilder.Build(specs); // SpecValidationException propagates to the top handler
            }
            catch (FileNotFoundException ex) when (SpecDependencyLoadFailure.IsAssemblyLoadFailure(ex))
            {
                throw SpecDependencyLoadFailure.Map(ex, specDllPath);
            }
            catch (MissingMemberException ex)
            {
                // A spec built against a newer contract than this host carries. The bind is version-agnostic
                // by design, so it got this far; the member it reached for is the one thing that cannot work.
                throw SpecContractMismatch.Map(assembly, specDllPath, ex);
            }
            catch (TypeLoadException ex)
            {
                // A typeof() anchor whose base type or implemented interface lives in a .NET Framework-only
                // assembly: the dependency resolved, the type did not. Without this arm it surfaces raw.
                throw SpecDependencyLoadFailure.Map(ex, specDllPath);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    ///     The <see cref="UserErrorException" /> text for a <see cref="ReflectionTypeLoadException" /> out of
    ///     spec discovery: the <em>distinct</em> loader messages (deduped, ordinal-sorted so the output is
    ///     deterministic) under a naming/fix frame. Internal so a fabricated exception can pin the shape.
    /// </summary>
    internal static string LoaderFailureMessage(ReflectionTypeLoadException ex, string specDllPath)
    {
        var messages = ex.LoaderExceptions
            .Select(inner => inner?.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToList();

        string detail = messages.Count > 0
            ? string.Join("\n", messages.Select(message => "  " + message))
            : "  (the runtime reported no loader detail)";

        return $"Could not load spec assembly '{Path.GetFileName(specDllPath)}'; one or more types failed to load:\n"
               + detail
               + "\nBuild the spec project and restore its dependencies, then retry. If it is already built, the"
               + " assembly named above is a dependency a class-library build does not stage beside the spec DLL:"
               + " add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj.";
    }
}
