namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     The used member of a member-use edge (GRAMMAR §4.5): a specific method, property, field, or
///     event that some type's source binds a name to. <see cref="ContainingType" /> is the declaring
///     type; <see cref="Name" /> and <see cref="Kind" /> are the (declaring type, name) coordinates a
///     ban matches on — one ban covers every overload, so there is no signature here — while
///     <see cref="SymbolId" /> is the <em>specific</em> member's <c>DocumentationCommentId</c>.
/// </summary>
/// <remarks>
///     <para>
///         Matching versus identity are deliberately different granularities (GRAMMAR §4.3, §4.5):
///         <c>MustNotUse(arch.Member(typeof(Task), nameof(Task.Wait)))</c> matches on
///         (<c>System.Threading.Tasks.Task</c>, <c>Wait</c>) and so covers every <c>Wait</c> overload,
///         but a baseline entry keys on <see cref="SymbolId" /> — the <c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c>
///         form of the observed member — so a grandfathered <c>Wait()</c> rewritten as
///         <c>Wait(timeout)</c> is a NEW red (the ratchet blesses the observed violation, not the ban).
///     </para>
///     <para>
///         <see cref="ContainingType" /> is the same <see cref="TypeNode" /> instance held by
///         <see cref="CodebaseModel.Types" /> (reference equality, not just name equality), exactly like a
///         <see cref="ReferenceEdge" /> endpoint — so a member's declaring type and its own type node are
///         one object. Members are definition-level: a constructed generic member resolves to its
///         original definition, so <c>Task&lt;int&gt;.Result</c> and <c>Task&lt;string&gt;.Result</c>
///         share one reference to <c>P:System.Threading.Tasks.Task`1.Result</c>.
///     </para>
/// </remarks>
public sealed class MemberReference
{
    internal MemberReference(TypeNode containingType, string name, string symbolId, MemberKind kind)
    {
        ContainingType = containingType;
        Name = name;
        SymbolId = symbolId;
        Kind = kind;
    }

    /// <summary>The declaring type node — the same instance held by <see cref="CodebaseModel.Types" />.</summary>
    public TypeNode ContainingType { get; }

    /// <summary>The member's simple name (no signature) — half of the (declaring type, name) match key.</summary>
    public string Name { get; }

    /// <summary>
    ///     The stable identity a baseline keys on (GRAMMAR §4.3): the Roslyn
    ///     <c>DocumentationCommentId</c> of the member's original definition — the <c>M:</c> form for a
    ///     method (parameters and, for a reduced extension, the receiver included), <c>P:</c> for a
    ///     property, <c>F:</c> for a field or enum member, <c>E:</c> for an event — or an
    ///     <c>unresolved:{declaring FQN}.{name}</c> fallback when the symbol has no DocID.
    /// </summary>
    public string SymbolId { get; }

    /// <summary>The member kind (accessors folded into their property/event).</summary>
    public MemberKind Kind { get; }
}