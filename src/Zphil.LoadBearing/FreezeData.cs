namespace Zphil.LoadBearing;

/// <summary>
///     The Freeze-specific payload of an <see cref="ArchRule" /> (DESIGN.md §5, GRAMMAR §7). A
///     frozen scope desugars into two rules that share a <see cref="ScopeId" />: the containment
///     rule carries the boundary and baseline; both carry the dragons prose.
/// </summary>
public sealed class FreezeData
{
    internal FreezeData(
        FreezeRole role,
        IReadOnlyList<Type> boundary,
        string? baselinePath,
        string? dragons,
        string? dragonsDoc,
        string scopeId,
        Selection? frozen)
    {
        Role = role;
        Boundary = boundary;
        BaselinePath = baselinePath;
        Dragons = dragons;
        DragonsDoc = dragonsDoc;
        ScopeId = scopeId;
        Frozen = frozen;
    }

    /// <summary>Whether this is the containment or the tripwire half.</summary>
    public FreezeRole Role { get; }

    /// <summary>
    ///     The raw frozen selection (the scope's <c>Freeze(sel)</c> operand, before the containment
    ///     desugaring subtracts the boundary). Carried on both children: the renderer evaluates it in
    ///     Subject position to place the scope's directory context file (R3), and the tripwire maps
    ///     changed files to the frozen types through it. Not public — placement and diff-matching are
    ///     Core concerns.
    /// </summary>
    internal Selection? Frozen { get; }

    /// <summary>The sanctioned surface types (empty for a hermetic freeze or a tripwire).</summary>
    public IReadOnlyList<Type> Boundary { get; }

    /// <summary>The grandfather baseline path (containment only), or null.</summary>
    public string? BaselinePath { get; }

    /// <summary>The load-bearing-weirdness prose, or null.</summary>
    public string? Dragons { get; }

    /// <summary>The linked long-form dragons document path, or null.</summary>
    public string? DragonsDoc { get; }

    /// <summary>The originating scope ID (both children share it).</summary>
    public string ScopeId { get; }
}