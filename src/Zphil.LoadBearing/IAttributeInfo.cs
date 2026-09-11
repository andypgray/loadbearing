namespace Zphil.LoadBearing;

/// <summary>
///     One attribute written on a member, as a member predicate sees it: the entries of
///     <see cref="IMemberInfo.Attributes" />. Only attributes written on the member itself appear —
///     those on a property's <c>get</c> or <c>set</c>, and <c>[return:]</c> attributes, belong to other
///     declarations.
/// </summary>
public interface IAttributeInfo
{
    /// <summary>
    ///     The attribute type's full name with its namespace, keeping the <c>Attribute</c> suffix the class
    ///     carries (<c>MyApp.MarkAttribute</c>, never <c>MyApp.Mark</c>) and spelling a generic attribute
    ///     with its declared type-parameter names (<c>MyApp.MarkAttribute&lt;T&gt;</c>). Every use of one
    ///     generic attribute reports the same name here, whatever type arguments it was written with, so
    ///     this is the name to compare against when the type arguments do not matter.
    /// </summary>
    string DefinitionFullName { get; }

    /// <summary>
    ///     The attribute type's full name with the type arguments as written
    ///     (<c>MyApp.MarkAttribute&lt;System.Int32&gt;</c>). For a non-generic attribute there are none, so
    ///     the name is the plain full name with the <c>Attribute</c> suffix intact.
    /// </summary>
    string FullName { get; }
}
