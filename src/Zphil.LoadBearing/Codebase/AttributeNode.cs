namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One attribute declared on a member in the extracted model (GRAMMAR §4.6) — the entries of
///     <see cref="MemberNode.Attributes" />. It implements <see cref="IAttributeInfo" /> so the
///     member-predicate attribute contract and this Roslyn-derived data are one surface, exactly as
///     <see cref="ParameterNode" /> implements <see cref="IParameterInfo" />.
/// </summary>
internal sealed class AttributeNode : IAttributeInfo
{
    internal AttributeNode(string definitionFullName, string fullName)
    {
        DefinitionFullName = definitionFullName;
        FullName = fullName;
    }

    /// <inheritdoc />
    public string DefinitionFullName { get; }

    /// <inheritdoc />
    public string FullName { get; }
}