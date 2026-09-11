using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>
///     The <c>arch_context</c> pipeline (the MCP <c>arch_context</c> tool's core): refuse a path that is a
///     JSON array written as text (<see cref="Refusals" />) → load the model → if
///     nothing is scoped to place, emit the pointer line and stop (no extraction — the cost gate
///     <see cref="RenderRunner" /> consults too) → otherwise extract, ask
///     <see cref="ContextFileComposer.Placements" /> for the cards <c>render</c> would splice, and write the
///     ones whose resolved directory contains the query path (layer card(s) before quarantine card(s)). No
///     card covers the path ⇒ the same pinned pointer line.
/// </summary>
/// <remarks>
///     <para>
///         <b>Never a gate.</b> No finding about the codebase changes the exit code: context is a lookup, so
///         an incomplete model is announced rather than enforced. The answer opens with a caveat block naming
///         the load failures, because a card whose project failed to load places nowhere and the pinned
///         pointer line would otherwise read as a clean "not dragon territory". A solution filter that left
///         declared projects unchecked is announced the same way and for the same reason, in a
///         <see cref="NarrowedUniverseNotice" /> stamp below that caveat.
///     </para>
///     <para>
///         <b>A malformed path is not a finding.</b> The one refusal here is a <c>path</c> that arrived as a
///         JSON array written as text, and it throws ahead of the load — before the caveat block exists — so
///         no incomplete-model preamble prints above a refusal it does not qualify. This verb is the one an
///         agent is told to call before editing unfamiliar territory, and its no-coverage answer is a
///         well-formed success, so a bracketed value would otherwise read as a proven negative rather than a
///         question that was never asked.
///     </para>
///     <para>The card body carries no provenance line — that is a <c>render</c> file-splice concern.</para>
/// </remarks>
internal sealed class ContextRunner(TextWriter output, ISolutionSource? source = null) : WorkspaceRunner(source)
{
    public async Task<int> RunAsync(ContextRequest request, CancellationToken ct)
    {
        // Ahead of the load, so the incomplete-model caveat below never prints above a refusal it does not
        // qualify. The other two shape refusals fire on no match; this verb has no no-match state — "no card
        // covers this path" is a legitimate answer — so a bracketed value would come back as a well-formed
        // proven negative on the exact call an agent makes before editing unfamiliar territory.
        RefuseAStringifiedArrayPath(request.Path);

        // Cache-free, and the one verb whose policy is not about what it reads absence as: arch_context has
        // no CLI verb at all (MCP-only), and the warm tool path deliberately leaves cache.json untouched so
        // the file and the warm WorkspaceSession keep independent lifetimes and never race on it. There is
        // no environment to read a cache root from for the same reason.
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, environment: null, request.Solution, request.Spec, request.WorkingDirectory,
            noCache: true, ct);

        // Ahead of every exit below, because each of them can be the false all-clear: the body is context's
        // only channel, so the load failures ride it or reach nobody.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        if (diagnostics.IsIncomplete)
        {
            string caveat = IncompleteModelGate.ContextCaveat(diagnostics);
            LineBlocks.Write(output, caveat);
            await output.WriteLineAsync();
        }

        // Beside that caveat rather than instead of it — both can be true of one run, and a broken model
        // outranks a small one, so the gate's block goes first. Context still exits 0: a narrowed universe is
        // a smaller true answer, and this says which paths the pointer line below cannot speak for. There is
        // no --json to suppress it for; the stamp writes itself only when the run was narrowed.
        NarrowingNotices.Stamp(output, source, NarrowedUniverseNotice.ContextStamp);

        // Nothing scoped to place — no quarantined scope and no anchored layer — ⇒ skip the extraction cost
        // and point at the root block.
        if (!ContextFileComposer.HasAnythingToPlace(source.Model))
        {
            await output.WriteLineAsync(PointerLine(request.Path));
            return 0;
        }

        CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct);

        string queryFullPath = ResolveQueryPath(request.Path, source.SolutionDirectory);

        // The composer's own placements, filtered to the ones covering the query path: layer local-rules
        // card(s) ahead of quarantined-scope card(s), the same cards in the same order render splices. An
        // unplaceable card carries a null directory and so covers nothing, which is the drop it always was.
        List<string> cards = ContextFileComposer.Placements(source.Model, codebase)
            .Where(card => card.DirectoryPath is not null && PathFormat.Contains(card.DirectoryPath, queryFullPath))
            .Select(card => card.Body)
            .ToList();

        if (cards.Count == 0)
        {
            await output.WriteLineAsync(PointerLine(request.Path));
            return 0;
        }

        WriteCards(cards);
        return 0;
    }

    // Each matching card — layer cards before quarantine cards — blank line between cards.
    private void WriteCards(IReadOnlyList<string> cards)
    {
        var first = true;
        foreach (string card in cards)
        {
            if (!first) output.WriteLine();
            first = false;

            LineBlocks.Write(output, card);
        }
    }

    // Canonicalize the query path to the same standard as the model's declaration-site paths (both
    // symlink-resolved), so a user/agent path spelled through a symlinked root still matches a scope.
    private static string ResolveQueryPath(string path, string solutionDirectory)
    {
        string full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(solutionDirectory, path));
        return PathCanonicalizer.Resolve(full);
    }

    // A reader, never a repair: a directory genuinely named [weird] is refused with its own text quoted back
    // rather than silently rewritten into something the caller did not ask for. Paths this verb has never
    // seen stay legitimate — it answers what rules WILL apply somewhere — and a JSON array is not one of
    // those, so nothing here narrows what a path may be.
    private static void RefuseAStringifiedArrayPath(string path)
    {
        var lead = $"Cannot find architecture scope for '{path}'";
        string? refusal = Refusals.StringifiedArrayRefusal(
            path, lead, elements => Refusals.SingleValueAdvice("pass one path", elements));

        if (refusal is not null) throw new UserErrorException(refusal);
    }

    private static string PointerLine(string path)
    {
        return $"No architecture scope covers '{path}'. Architecture context for this solution lives in the root " +
               "AGENTS.md managed block; expand any rule with 'loadbearing explain <rule-id>'.";
    }
}
