using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     What a rule contributed by a scope carries beyond an ordinary rule: which half of the scope it is,
///     the sanctioned surface, the baseline, and what the scope says is strange inside it. A quarantine
///     contributes two rules sharing one <see cref="ScopeId" /> — the containment rule holding the
///     surface and the baseline — and a caution the tripwire alone; both halves carry the dragons prose.
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

    /// <summary>
    ///     Gets which half of the scope this rule is: the containment rule that judges references into the
    ///     scope, or the tripwire that warns about edits inside it.
    /// </summary>
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
    ///     Gets the sanctioned surface ready to print, in the order the spec named it: one phrase per
    ///     operand — a backticked simple name for a type, widened when simple names collide, and the
    ///     operand's own phrase otherwise, as in <c>types named `CodeFormatHelper`</c>. Empty for a hermetic
    ///     quarantine and for a tripwire.
    /// </summary>
    // Every surface that prints the boundary reads this, so the scope's context card and `explain` cannot
    // disagree about what the sanctioned surface is.
    public IReadOnlyList<string> Surface { get; }

    /// <summary>
    ///     Gets the file recording the references into the scope that already existed when it was declared,
    ///     or null on a tripwire. Never null on a containment rule: the path the spec gave, or
    ///     <c>arch/baselines/{scope-id}/containment.json</c> when it gave none.
    /// </summary>
    public string? BaselinePath { get; }

    /// <summary>
    ///     Gets what is load-bearing and strange inside the scope, in the spec's own words, or null when the
    ///     spec gave only a <see cref="DragonsDoc" />.
    /// </summary>
    public string? Dragons { get; }

    /// <summary>
    ///     Gets the path to the longer document about the scope, as the spec wrote it, or null when the spec
    ///     gave only a <see cref="Dragons" /> line.
    /// </summary>
    public string? DragonsDoc { get; }

    /// <summary>
    ///     Gets the ID of the scope both halves came from — this rule's own ID without its
    ///     <c>/containment</c> or <c>/tripwire</c> ending.
    /// </summary>
    public string ScopeId { get; }
}
