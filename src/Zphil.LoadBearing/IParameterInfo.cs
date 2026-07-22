namespace Zphil.LoadBearing;

/// <summary>
///     The read-only view of one declared parameter of a method, the ordered entries of
///     <see cref="IMemberInfo.Parameters" /> (GRAMMAR §4.6, §5.6). The parameter analog of the member
///     facts: part of the v1 member-predicate input contract, grown additively as extraction learns new
///     facts. What a <c>MustAcceptParameter</c> anchor matches against.
/// </summary>
public interface IParameterInfo
{
    /// <summary>The parameter's declared name.</summary>
    string Name { get; }

    /// <summary>
    ///     The definition-level full name of the parameter's type, normalized exactly like
    ///     <see cref="IMemberInfo.ReturnTypeFullName" /> — a constructed generic reduces to its
    ///     definition, so a <c>MustAcceptParameter</c> anchor matches at the definition level (GRAMMAR §4.6).
    /// </summary>
    string TypeFullName { get; }
}