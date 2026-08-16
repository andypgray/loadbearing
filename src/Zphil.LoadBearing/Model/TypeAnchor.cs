using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     How a rule names a single type (GRAMMAR §5.2–§5.3): either a reflected <see cref="System.Type" />
///     (<c>typeof(ApiControllerAttribute)</c>, <c>typeof(IHandler&lt;&gt;)</c>) or the type
///     definition's fully-qualified name as a verbatim string
///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>, <c>"MyApp.Web.IHandler&lt;T&gt;"</c>).
///     Exactly one arm is populated — <see cref="Type" /> xor <see cref="DefinitionFullName" />.
/// </summary>
/// <remarks>
///     The string arm is the escape hatch for a type the spec project cannot compile against:
///     without it, naming someone else's attribute or interface forces a package reference on the
///     spec just to write the <c>typeof</c>.
///     <para>
///         String matching is <em>definition-level, exact, and verbatim</em>: the name is the type
///         definition's FQN in extraction format — <c>Attribute</c> suffix included for an attribute,
///         declared type-parameter names for a generic (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>, exactly as
///         a report prints it) — and it matches any construction of that definition, the same semantics
///         an open-generic <c>typeof</c> anchor carries. A <em>constructed</em> spelling
///         (<c>"N.MarkAttribute&lt;System.Int32&gt;"</c>,
///         <c>"MyApp.Web.IHandler&lt;MyApp.Web.InvoiceCreated&gt;"</c>) names no definition and so never
///         matches; that is the stated honesty boundary of the hatch, not a defect. Nothing is inferred
///         from the shape of the string: a dotless or suffix-less name is a legal spelling that simply
///         never matches.
///     </para>
/// </remarks>
internal sealed class TypeAnchor
{
    private TypeAnchor(Type? type, string? definitionFullName)
    {
        Type = type;
        DefinitionFullName = definitionFullName;
        PathSegments = type is not null ? TypeName.PathSegments(type) : SplitPath(definitionFullName!);
    }

    /// <summary>The reflected type of a <c>typeof</c> anchor; <c>null</c> on a string anchor.</summary>
    internal Type? Type { get; }

    /// <summary>The verbatim definition FQN of a string anchor; <c>null</c> on a <c>typeof</c> anchor.</summary>
    internal string? DefinitionFullName { get; }

    /// <summary>
    ///     The anchor's dot-separated path, whichever arm it carries — the input the
    ///     colliding-simple-name rule widens outward along (GRAMMAR §6). Because both arms produce it, a
    ///     string anchor renders byte-identically to its <c>typeof</c> twin.
    /// </summary>
    internal IReadOnlyList<string> PathSegments { get; }

    /// <summary>The unqualified display name: the anchor's last path segment.</summary>
    internal string SimpleDisplay => PathSegments[PathSegments.Count - 1];

    /// <summary>Anchors on a reflected type — the <c>typeof</c> form.</summary>
    internal static TypeAnchor FromType(Type type)
    {
        return new TypeAnchor(Guard.NotNull(type, nameof(type)), null);
    }

    /// <summary>
    ///     Anchors on the type definition's fully-qualified name — the string form. Only null is
    ///     refused here; a blank name is a spec-author mistake, collected by the validation catalog
    ///     (GRAMMAR §8 item 15) so it reports beside every other error rather than throwing first.
    /// </summary>
    internal static TypeAnchor FromName(string definitionFullName)
    {
        return new TypeAnchor(null, Guard.NotNull(definitionFullName, nameof(definitionFullName)));
    }

    /// <summary>
    ///     The <c>(first, params more)</c> anchor list of a negative hierarchy verb, each operand a
    ///     <c>typeof</c> anchor — the hierarchy-verb shape (GRAMMAR §10), stored on the node as anchors
    ///     and never wrapped as selections.
    /// </summary>
    internal static IReadOnlyList<TypeAnchor> FromTypes(Type first, Type[] more)
    {
        return OperandList.OneOrMore(first, more, FromType);
    }

    /// <summary>
    ///     The string twin of <see cref="FromTypes" />: the same shape over type-definition names, each
    ///     minted through <see cref="FromName" />.
    /// </summary>
    internal static IReadOnlyList<TypeAnchor> FromNames(string first, string[] more)
    {
        return OperandList.OneOrMore(first, more, FromName);
    }

    // Splits a fully-qualified name on the dots OUTSIDE any <...>: "N.Sub.MarkAttribute<T>" is
    // ["N", "Sub", "MarkAttribute<T>"] and "N.IHandler<System.Int32>" is ["N", "IHandler<System.Int32>"],
    // because the dots inside a type-argument list belong to the ARGUMENT's path, not this name's. Depth
    // tracking (rather than a first-'<' scan) is what keeps a nested argument list honest.
    private static IReadOnlyList<string> SplitPath(string fullName)
    {
        var segments = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < fullName.Length; i++)
            switch (fullName[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    if (depth > 0) depth--;

                    break;
                case '.' when depth == 0:
                    segments.Add(fullName.Substring(start, i - start));
                    start = i + 1;
                    break;
            }

        // The tail is always a segment, so a dotless name is one segment and a blank name is one blank
        // segment — the path is never empty, which is what lets SimpleDisplay index it unguarded.
        segments.Add(fullName.Substring(start));
        return segments;
    }
}
