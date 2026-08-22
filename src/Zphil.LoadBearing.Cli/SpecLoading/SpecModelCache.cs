using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     A session-scoped cache of loaded spec models, keyed by the spec DLL's file stamp. The warm MCP
///     server's answer to re-running the whole spec load — a fresh <see cref="SpecLoadContext" />, the DLL
///     re-read off disk, reflection over its types, the spec's own <c>Define()</c> executed, and a full
///     model build plus validation — on every single tool call, for something that changes only when the
///     spec project is rebuilt. The one-shot CLI keeps its per-process load untouched.
/// </summary>
/// <remarks>
///     <para>
///         <b>The stamp is the whole contract.</b> A hit requires <see cref="FileFreshness.CanTrust" /> —
///         same existence, same mtime, same length, and the recorded stamp captured comfortably outside the
///         filesystem's racy window — which is the same primitive the warm workspace's reconcile sweep and
///         the persisted extraction cache already trust for staleness. A rebuilt spec therefore misses and
///         reloads, and a spec rebuilt inside the racy window keeps missing (and reloading) until its stamp
///         promotes, which is the safe direction to be wrong in.
///     </para>
///     <para>
///         <b>Successes only.</b> A load that throws — a deleted DLL, a spec-validation failure, a
///         dependency that will not resolve — stores nothing, so every later call re-raises the identical
///         error the cold CLI raises rather than replaying a stale success or a cached exception.
///     </para>
///     <para>
///         <b>What this does to assembly-load-context lifetime: strictly less.</b> A collectible context
///         cannot actually collect while the model it produced roots the spec's <c>Type</c> references, so
///         caching the model keeps one such context alive instead of minting a new uncollectable one per
///         tool call. Nothing is loaded by path (<see cref="SpecLoadContext.LoadWithoutLocking" />), so no
///         build output is pinned either way.
///     </para>
/// </remarks>
internal sealed class SpecModelCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    private readonly Lock gate = new();

    /// <summary>
    ///     The model behind <paramref name="specDllPath" />: the cached one when the DLL's stamp still
    ///     proves it current, otherwise a full <see cref="ModelPipeline.LoadModel" /> whose result is
    ///     recorded for the next call.
    /// </summary>
    internal ArchitectureModel Load(string specDllPath)
    {
        string key = Path.GetFullPath(specDllPath);
        FileFreshness stamp = FileFreshness.Capture(key);

        lock (gate)
        {
            if (entries.TryGetValue(key, out Entry? cached) && cached.Stamp.CanTrust(stamp))
                return cached.Model;
        }

        // Loaded outside the lock: this runs reflection and the spec author's own Define(), so holding the
        // lock across it would serialize every tool call behind one spec's load. Two callers racing the same
        // miss both load and the last one recorded wins — a wasted load, never a wrong model.
        ArchitectureModel model = ModelPipeline.LoadModel(specDllPath);

        // The stamp recorded is the one taken BEFORE the load, so a DLL rewritten mid-load records the old
        // file's fingerprint and the next call misses on it. Recording a post-load stamp would pair the new
        // bytes' fingerprint with the old bytes' model.
        lock (gate)
        {
            entries[key] = new Entry(stamp, model);
        }

        return model;
    }

    private sealed record Entry(FileFreshness Stamp, ArchitectureModel Model);
}
