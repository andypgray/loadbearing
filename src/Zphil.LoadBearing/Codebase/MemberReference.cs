namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A member some type's source used: the far end of a <see cref="MemberEdge" />. It carries the
///     declaring type and the simple name (the pair a rule banning a member matches on) beside
///     <see cref="SymbolId" />, which identifies the single member observed.
/// </summary>
/// <remarks>
///     <para>
///         Matching and identity are deliberately different grains. A ban is written against a declaring
///         type and a name, so one ban covers every overload and there is no signature here; a baseline
///         records <see cref="SymbolId" /> instead, so a grandfathered <c>Wait()</c> rewritten as
///         <c>Wait(timeout)</c> is a new violation and fails the check.
///     </para>
///     <para>
///         <see cref="ContainingType" /> is the very type instance <see cref="CodebaseModel.Types" /> lists,
///         so a member and its declaring type are one object graph. Members are recorded at their
///         definition, so <c>Task&lt;int&gt;.Result</c> and <c>Task&lt;string&gt;.Result</c> are one member
///         here. An accessor is recorded as its property or event, and a call written in extension-method
///         form is recorded against the static method that declares it.
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

    /// <summary>Gets the type that declares the member.</summary>
    public TypeNode ContainingType { get; }

    /// <summary>
    ///     Gets the member's simple name, with no signature: half of the (declaring type, name) pair a ban
    ///     matches on.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the identity a baseline records for this member: its Roslyn documentation comment ID —
    ///     <c>M:</c> for a method (its parameters, and for a call in extension-method form its receiver,
    ///     included), <c>P:</c> for a property, <c>F:</c> for a field or enum member, <c>E:</c> for an event —
    ///     or <c>unresolved:{declaring type}.{name}</c> where the compiler gives the member no such ID. It
    ///     survives file moves, renames and reformatting, where a <c>file:line</c> does not.
    /// </summary>
    public string SymbolId { get; }

    /// <summary>
    ///     Gets which kind of member this is. An accessor never appears in its own right: it is recorded as the
    ///     property or event it belongs to.
    /// </summary>
    public MemberKind Kind { get; }
}
