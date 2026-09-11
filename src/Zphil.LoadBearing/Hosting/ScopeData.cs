using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     The scope-specific payload of an <see cref="ArchRule" /> (GRAMMAR §7). A quarantined scope
///     desugars into two rules that share a <see cref="ScopeId" /> — the containment rule carries the
///     boundary and baseline — and a cautioned scope into the tripwire alone; every child carries the
///     dragons prose.
/// </summary>
public sealed class ScopeData
{
    internal ScopeData(
        ScopeRole role,
        IReadOnlyList<Selection> boundary,
        string? baselinePath,
        string? dragons,
        string? dragonsDoc,
        string scopeId,
        Selection scoped)
    {
        Role = role;
        Boundary = boundary;
        Surface = SentenceRenderer.ReferenceFragments(boundary);
        BaselinePath = baselinePath;
        Dragons = dragons;
        DragonsDoc = dragonsDoc;
        ScopeId = scopeId;
        Scoped = scoped;
    }

    /// <summary>Whether this is the containment or the tripwire half.</summary>
    public ScopeRole Role { get; }

    /// <summary>
    ///     The raw scoped selection (the scope's <c>Quarantine(sel)</c>/<c>Caution(sel)</c> operand, before
    ///     the containment desugaring subtracts the boundary). Carried on every child: the renderer
    ///     evaluates it in Subject position to place the scope's directory context file, and the tripwire
    ///     maps changed files to the scoped types through it. Not public — placement and diff-matching are
    ///     Core concerns.
    /// </summary>
    internal Selection Scoped { get; }

    /// <summary>
    ///     The sanctioned surface as the spec named it (empty for a hermetic quarantine or a tripwire).
    ///     Not public, on the <see cref="Scoped" /> precedent: a selection is the model, and the
    ///     rendered answer to "what is the sanctioned surface" is <see cref="Surface" />.
    /// </summary>
    internal IReadOnlyList<Selection> Boundary { get; }

    /// <summary>
    ///     The sanctioned surface pre-rendered, in the spec's own order: one reference fragment per
    ///     operand — a backticked simple name for a type, widened where simple names collide, and the
    ///     noun's own phrase otherwise (<c>types named `CodeFormatHelper`</c>). Every surface that prints
    ///     the boundary reads this, so the scope card and <c>explain</c> cannot disagree.
    /// </summary>
    public IReadOnlyList<string> Surface { get; }

    /// <summary>The grandfather baseline path (containment only), or null.</summary>
    public string? BaselinePath { get; }

    /// <summary>The load-bearing-weirdness prose, or null.</summary>
    public string? Dragons { get; }

    /// <summary>The linked long-form dragons document path, or null.</summary>
    public string? DragonsDoc { get; }

    /// <summary>The originating scope ID (every child of the scope shares it).</summary>
    public string ScopeId { get; }
}
