using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     How a rule names an attribute (GRAMMAR §5.2–§5.3): either a reflected <see cref="System.Type" />
///     (<c>typeof(ApiControllerAttribute)</c>) or the attribute definition's fully-qualified name as a
///     verbatim string (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>). Exactly one arm is
///     populated — <see cref="Type" /> xor <see cref="DefinitionFullName" />.
///     <para>
///         The string arm is the escape hatch for an attribute the spec project cannot compile against:
///         without it, naming someone else's attribute forces a package reference on the spec just to write
///         the <c>typeof</c>. One union rather than a twin node per arm, because every attribute-bearing
///         node — the adjective and both verbs, on the type side and the member side — anchors through this
///         one type.
///     </para>
///     <para>
///         String matching is <em>definition-level, exact, and verbatim</em>: the name is the attribute
///         definition's FQN in extraction format, <c>Attribute</c> suffix included, and it matches any
///         construction of that definition — the same semantics an open-generic <c>typeof</c> anchor
///         carries. A <em>constructed</em> spelling (<c>"N.MarkAttribute&lt;System.Int32&gt;"</c>) names no
///         definition and so never matches; that is the stated honesty boundary of the hatch, not a defect.
///         Nothing is inferred from the shape of the string: a dotless or suffix-less name is a legal
///         spelling that simply never matches.
///     </para>
/// </summary>
internal sealed class AttributeAnchor
{
    private AttributeAnchor(Type? type, string? definitionFullName)
    {
        Type = type;
        DefinitionFullName = definitionFullName;
        PathSegments = type is not null ? TypeName.PathSegments(type) : SplitPath(definitionFullName!);
    }

    /// <summary>The reflected attribute type of a <c>typeof</c> anchor; <c>null</c> on a string anchor.</summary>
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

    /// <summary>Anchors on a reflected attribute type — the <c>typeof</c> form.</summary>
    internal static AttributeAnchor FromType(Type type)
    {
        return new AttributeAnchor(Guard.NotNull(type, nameof(type)), null);
    }

    /// <summary>
    ///     Anchors on the attribute definition's fully-qualified name — the string form. Only null is
    ///     refused here; a blank name is a spec-author mistake, collected by the validation catalog
    ///     (GRAMMAR §8 item 15) so it reports beside every other error rather than throwing first.
    /// </summary>
    internal static AttributeAnchor FromName(string definitionFullName)
    {
        return new AttributeAnchor(null, Guard.NotNull(definitionFullName, nameof(definitionFullName)));
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