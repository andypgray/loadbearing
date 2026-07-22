namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One declared parameter of a method in the extracted model (GRAMMAR §4.6) — the ordered entries of
///     <see cref="MemberNode.Parameters" />. It implements <see cref="IParameterInfo" /> so the
///     member-predicate parameter contract and this Roslyn-derived data are one surface, exactly as
///     <see cref="MemberNode" /> implements <see cref="IMemberInfo" />.
/// </summary>
internal sealed class ParameterNode : IParameterInfo
{
    internal ParameterNode(string name, string typeFullName)
    {
        Name = name;
        TypeFullName = typeFullName;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string TypeFullName { get; }
}