using System.Collections.Concurrent;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The scanning half every doc-quote gate in this folder shares: a hand-written registry of docs, a
///     scanner that lifts one shape of quoted output out of a doc's committed bytes, and the two facts
///     about the registry itself that every one of those gates has to state — that each registered doc
///     still yields something, and that no unregistered doc yields anything.
/// </summary>
/// <typeparam name="TQuote">What the scanner lifts: a rule quote, a count, a written path, an anchor, a fence.</typeparam>
/// <remarks>
///     <para>
///         <b>Why the harness owns exactly these two facts.</b> They are the ones whose absence is silent.
///         A gate whose scanner stops matching goes green over nothing, and a doc nobody registered is
///         outside the gate with no failure anywhere — which is how a sixth gate came to be written from
///         scratch, and how one of the five shipped without the registry sweep at all. Everything a gate
///         does with what it scanned — classify, exempt, pin — stays in the gate, because the root pairing
///         and the per-root hoist those need are the gate's own.
///     </para>
///     <para>
///         <b>Why the nouns are parameters.</b> Each gate's failure text is the wording its reader already
///         knows, and moving five near-identical messages behind one harness must not quietly normalize
///         them. So the emptiness header is composed from three words — the doc-set noun, the plural of
///         what is yielded, the scanner's name — and the registry sweep from three more, and each gate
///         passes its own. <see cref="DocGateTests" /> pins the composed text.
///     </para>
///     <para>
///         <b>Scanning is memoized</b> because the registry sweep reads every tracked markdown file and the
///         gate's own facts then read the registered ones again. One <see cref="ConcurrentDictionary{TKey,TValue}" />
///         per gate keeps that to one scan per doc, and keeps it safe for a gate whose facts run in parallel.
///     </para>
/// </remarks>
/// <param name="registeredDocs">The docs the gate holds, in registration order — how the emptiness failure lists them.</param>
/// <param name="scan">Lifts every quote of this gate's shape out of one doc, named by its repo-relative path.</param>
/// <param name="quotes">The plural of what the scanner yields, as the emptiness header names it ("rule quotes").</param>
/// <param name="docs">The doc-set noun in that same header, for a gate whose docs are a named kind ("anchor docs").</param>
/// <param name="scanner">
///     The scanner's name in that same header, for a gate that scans something other than quotes ("fence
///     scanner").
/// </param>
internal sealed class DocGate<TQuote>(
    IReadOnlyList<string> registeredDocs,
    Func<string, IReadOnlyList<TQuote>> scan,
    string quotes,
    string docs = "docs",
    string scanner = "scanner")
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<TQuote>> _scanned = new(StringComparer.Ordinal);

    /// <summary>
    ///     Every quote the scanner lifts from <paramref name="doc" />, scanned once however many facts ask
    ///     for it.
    /// </summary>
    internal IReadOnlyList<TQuote> Scan(string doc)
    {
        return _scanned.GetOrAdd(doc, scan);
    }

    /// <summary>
    ///     Asserts every registered doc still yields at least one quote — the guard against the scanner
    ///     silently matching nothing if a doc's quoting style changes. Every empty doc is reported, not
    ///     just the first.
    /// </summary>
    internal void ShouldYieldFromEveryRegisteredDoc()
    {
        List<string> empty = registeredDocs
            .Where(doc => Scan(doc)
                .Count == 0)
            .ToList();

        empty.ShouldReportNothing($"These {docs} yielded no {quotes}; the {scanner} may be silently matching nothing");
    }

    /// <summary>
    ///     Asserts no tracked markdown outside the registry yields a quote — the guard the hand-written
    ///     registry cannot give itself, which is a whole doc silently outside the gate.
    /// </summary>
    /// <param name="counted">The countable in the per-doc finding, singular-with-plural as it is counted ("rule sentence(s)").</param>
    /// <param name="swept">The plural of that countable, as the header names it ("rule sentences").</param>
    /// <param name="authority">What the gate holds those quotes to, as the header names it ("the spec").</param>
    /// <remarks>
    ///     Git decides the scope, as it does for every hygiene gate here, and the sweep runs over every
    ///     tracked markdown file rather than only the ones under a named root: narrowing it would leave the
    ///     registered docs' siblings unguarded for no reason.
    /// </remarks>
    internal void ShouldFindNothingOutsideTheRegistry(string counted, string swept, string authority)
    {
        HashSet<string> registered = registeredDocs.ToHashSet(StringComparer.Ordinal);
        List<string> unregistered = new();

        foreach (string path in TrackedFiles.Markdown)
        {
            if (registered.Contains(path)) continue;

            int found = Scan(path)
                .Count;
            if (found > 0) unregistered.Add($"{path} quotes {found} {counted} but is not registered.");
        }

        unregistered.ShouldReportNothing($"These tracked docs quote {swept} that nothing holds to {authority}");
    }
}
