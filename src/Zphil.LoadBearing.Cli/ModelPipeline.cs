using System.Reflection;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Discovery;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The spec→model prefix shared by every workspace command (check, render, and explain's
///     convention/csproj path): discover the solution → load the workspace → resolve the spec →
///     ALC-load and validate the model. Factored out of <c>CheckRunner</c> so all three commands share
///     one staleness contract and one quarantine story. explain's DLL fast path skips the workspace
///     entirely via <see cref="SpecResolver.TryResolveWithoutSolution" /> and only calls
///     <see cref="LoadModel" />.
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
                    ? SolutionDiscovery.DiscoverSolution(null, solution!)
                    : SolutionDiscovery.DiscoverSolution(solution!);

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
    ///     Runs the whole workspace prefix and returns a disposable bundle the caller <c>using</c>s.
    ///     On any failure after the workspace opens, the workspace is disposed before the exception
    ///     propagates, so a partial run never leaks a BuildHost.
    /// </summary>
    public static async Task<WorkspaceModel> LoadWithWorkspaceAsync(
        string? solution, string? spec, string workingDirectory, CancellationToken ct)
    {
        string solutionPath = DiscoverSolution(solution, workingDirectory);
        var diagnostics = new List<string>();
        LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solutionPath, diagnostics.Add, ct);
        try
        {
            SpecResolution resolution = SpecResolver.Resolve(loaded.Solution, spec);
            ArchitectureModel model = LoadModel(resolution.DllPath);
            return new WorkspaceModel(loaded, model, resolution, diagnostics, solutionPath);
        }
        catch
        {
            loaded.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Loads the spec DLL in a collectible ALC (the <c>Zphil.LoadBearing</c> contract resolves from
    ///     Default so type identity holds), discovers and builds the model, then best-effort unloads.
    ///     The returned model roots the spec's <c>Type</c> references, so it stays usable after
    ///     <c>Unload()</c> in this one-shot process.
    /// </summary>
    public static ArchitectureModel LoadModel(string specDllPath)
    {
        var context = new SpecLoadContext(specDllPath);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(specDllPath);
            IReadOnlyList<IArchitectureSpec> specs;
            try
            {
                specs = SpecDiscovery.FindSpecs(assembly);
            }
            catch (SpecDiscoveryException ex)
            {
                throw new UserErrorException(ex.Message, ex);
            }

            return ArchModelBuilder.Build(specs); // SpecValidationException propagates to the top handler
        }
        finally
        {
            context.Unload();
        }
    }
}