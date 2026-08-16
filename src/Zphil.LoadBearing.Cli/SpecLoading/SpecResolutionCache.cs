namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     What one spec resolution produced for the run that asked: the <see cref="Resolution" /> itself and
///     the solution's declared <see cref="DeclaredMembers" /> it was computed against — which extraction
///     needs next, and which resolution had to read anyway.
/// </summary>
/// <param name="Resolution">The resolved spec: the DLL to load, the spec project, the excluded projects.</param>
/// <param name="DeclaredMembers">
///     The solution's declared <c>.csproj</c> members, or <see langword="null" /> when membership could not
///     be read. Travels with the resolution so one read serves resolution and extraction both, exactly as
///     the cold path's shared <see cref="Lazy{T}" /> did.
/// </param>
internal readonly record struct SessionSpecResolution(
    SpecResolution Resolution,
    IReadOnlySet<string>? DeclaredMembers);

/// <summary>
///     A session-scoped cache of spec resolutions, keyed by the normalized <c>--spec</c> argument and
///     stamped with the workspace generation each was computed under. The warm MCP server's answer to
///     re-running the whole resolution — the solution-file membership parse, a canonicalized walk over every
///     project, the convention's candidate scan, and the spec project's <c>ProjectReference</c> closure — on
///     every single tool call, for something that cannot change while the workspace stands. The one-shot CLI
///     keeps its per-process resolution untouched.
/// </summary>
/// <remarks>
///     <para>
///         <b>The generation is the grain, and it is the only sound one.</b> A
///         <see cref="Roslyn.WorkspaceSession" /> mints a fresh snapshot instance per changed document, so
///         keying on snapshot identity would miss on every <c>.cs</c> edit — precisely the per-edit hook this
///         cache exists for — and spare nothing. Everything resolution reads is structural: the project set,
///         their <c>.csproj</c> paths, their evaluated output paths, and the solution's declared membership.
///         None of it can change without a full reload, which bumps the generation; and the solution file
///         itself is one of the structural fingerprints the session's reconcile sweep stats, so a project
///         added to or removed from it arrives here as a miss. It is the same grain
///         <see cref="Roslyn.SessionFragmentStore" /> already flushes on, for the same reason.
///     </para>
///     <para>
///         <b>Only the candidate half is cached; the built output is resolved live on every call.</b> A hit
///         replays through <see cref="SpecResolver.Replay" />, the one owner the persisted extraction cache's
///         hit path also goes through — so a spec DLL that has since been rebuilt, moved or deleted answers
///         in the cold run's words rather than with a path recorded before it went. That is a per-call
///         <c>File.Exists</c>, against the walk it spares.
///     </para>
///     <para>
///         <b>A prebuilt-DLL <c>--spec</c> never gets here.</b> It resolves with no workspace and no walk, so
///         there is nothing to spare — and replaying it would refuse in the built-output search's words,
///         which name a spec project a DLL spec does not have. The caller answers it first, exactly as the
///         persisted cache's hit path does.
///     </para>
///     <para>
///         <b>Successes only, and the expensive work stays outside the lock.</b> A resolution that throws —
///         no spec project, an ambiguous one, nothing built — stores nothing, so every later call re-raises
///         the identical error the cold CLI raises rather than replaying a stale success. Neither the full
///         resolve nor the replay's disk search runs under the gate, which would otherwise serialize every
///         tool call behind one solution's walk; two callers racing the same miss both resolve and the last
///         one recorded wins, which costs a walk and never a wrong answer.
///     </para>
/// </remarks>
internal sealed class SpecResolutionCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    private readonly Lock gate = new();

    /// <summary>
    ///     How many resolutions this cache sent to a full walk rather than replaying — one per
    ///     <c>--spec</c> argument per workspace generation in the steady state. Internal test observable;
    ///     never printed.
    /// </summary>
    internal int FullResolveCount { get; private set; }

    /// <summary>
    ///     The resolution for <paramref name="normalizedSpecArgument" /> under
    ///     <paramref name="generation" />: the recorded candidate half replayed against disk when one was
    ///     computed under this same generation, otherwise <paramref name="resolveFully" /> — whose result is
    ///     recorded for the next call.
    /// </summary>
    /// <param name="normalizedSpecArgument">
    ///     The <c>--spec</c> value made absolute, or <c>""</c> for the convention default. The same key the
    ///     persisted cache records its resolutions under, so the two agree on what "the same spec" means.
    /// </param>
    /// <param name="generation">The workspace load generation this call's snapshot carries.</param>
    /// <param name="resolveFully">The cold resolution, run on a miss.</param>
    internal SessionSpecResolution Resolve(
        string normalizedSpecArgument, long generation, Func<SessionSpecResolution> resolveFully)
    {
        Entry? cached;
        lock (gate)
        {
            entries.TryGetValue(normalizedSpecArgument, out cached);
        }

        if (cached is not null && cached.Generation == generation)
        {
            SpecResolution recorded = cached.Resolution;
            SpecResolution replayed = SpecResolver.Replay(
                recorded.SpecProjectName, normalizedSpecArgument, recorded.ExcludeProjectNames,
                recorded.OutputFilePaths, recorded.IntermediateAssemblyPath);
            return new SessionSpecResolution(replayed, cached.DeclaredMembers);
        }

        SessionSpecResolution resolved = resolveFully();

        lock (gate)
        {
            entries[normalizedSpecArgument] = new Entry(generation, resolved.Resolution, resolved.DeclaredMembers);
            FullResolveCount++;
        }

        return resolved;
    }

    private sealed record Entry(long Generation, SpecResolution Resolution, IReadOnlySet<string>? DeclaredMembers);
}
