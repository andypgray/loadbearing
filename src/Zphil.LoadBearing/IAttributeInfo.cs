namespace Zphil.LoadBearing;

/// <summary>
///     The read-only view of one attribute declared on a member, the entries of
///     <see cref="IMemberInfo.Attributes" /> (GRAMMAR §4.6, §5.6). The member-side analog of the type's
///     attribute facts: part of the v1 member-predicate input contract, grown additively as extraction
///     learns new facts. What a member-side attribute adjective matches against.
/// </summary>
public interface IAttributeInfo
{
    /// <summary>
    ///     The definition-level full name of the attribute type, in extraction format and with the
    ///     <c>Attribute</c> suffix intact (<c>N.MarkAttribute</c>, never <c>N.Mark</c>) — a C# 11 generic
    ///     attribute reduces to its open definition (<c>N.MarkAttribute&lt;T&gt;</c>). This is what an
    ///     open-generic-style anchor matches, and what a string full-name anchor matches (GRAMMAR §4.6).
    /// </summary>
    string DefinitionFullName { get; }

    /// <summary>
    ///     The attribute type's constructed display name. For a non-generic attribute it coincides with
    ///     <see cref="DefinitionFullName" />; for a C# 11 generic attribute it carries the substituted type
    ///     arguments (<c>N.MarkAttribute&lt;System.Int32&gt;</c>).
    /// </summary>
    string FullName { get; }
}