using System.Text;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Low-level prose formatting helpers shared by the vocabulary nodes and the renderer
///     (GRAMMAR §6): backtick wrapping, sentence-initial capitalization, kind pluralization,
///     attribute bracketing, the no-Oxford-comma reference-list join, and the colliding-simple-name
///     qualification (<see cref="ResolvePathDisplays" />) shared by every multi-operand list.
/// </summary>
internal static class ProseFormat
{
    /// <summary>Wraps an identifier or glob in backticks: <c>SqlConnection</c> → <c>`SqlConnection`</c>.</summary>
    internal static string Backtick(string value)
    {
        return "`" + value + "`";
    }

    /// <summary>Upper-cases the first character of a lowercase fragment for sentence-initial use.</summary>
    internal static string Capitalize(string phrase)
    {
        if (string.IsNullOrEmpty(phrase)) return phrase;

        return char.ToUpperInvariant(phrase[0]) + phrase.Substring(1);
    }

    /// <summary>The plural noun a <see cref="TypeKind" /> substitutes for the subject head (GRAMMAR §5.2).</summary>
    internal static string KindPlural(TypeKind kind)
    {
        return kind switch
        {
            TypeKind.Class => "classes",
            TypeKind.Interface => "interfaces",
            TypeKind.Struct => "structs",
            TypeKind.Enum => "enums",
            TypeKind.Delegate => "delegates",
            _ => "types"
        };
    }

    /// <summary>
    ///     The registration noun's reference fragment (GRAMMAR §5.1): the lifetime-prefixed head, or the
    ///     bare "registered types" for any lifetime (<c>null</c>). This fragment is also the noun's subject
    ///     head — it survives adjectives, so a qualified <c>Registered</c> subject keeps the qualifier
    ///     instead of collapsing to a false bare "types". An undefined lifetime (refused at spec build,
    ///     GRAMMAR §8 item 19) never reaches a render, so it falls back to the bare fragment.
    /// </summary>
    internal static string RegisteredFragment(Lifetime? lifetime)
    {
        return lifetime switch
        {
            Lifetime.Singleton => "singleton-registered types",
            Lifetime.Scoped => "scoped-registered types",
            Lifetime.Transient => "transient-registered types",
            _ => "registered types"
        };
    }

    /// <summary>The plural noun a member <see cref="MemberKindFilter" /> projection heads with (GRAMMAR §5.7).</summary>
    internal static string MemberKindPlural(MemberKindFilter kind)
    {
        return kind switch
        {
            MemberKindFilter.Method => "methods",
            MemberKindFilter.Property => "properties",
            MemberKindFilter.Field => "fields",
            MemberKindFilter.Event => "events",
            _ => "members"
        };
    }

    /// <summary>
    ///     The bracketed attribute form with a trailing <c>Attribute</c> stripped:
    ///     <c>ApiControllerAttribute</c> → <c>[ApiController]</c> (GRAMMAR §5.2). Reads the anchor's
    ///     simple display, so a <c>typeof</c> anchor and a string anchor naming the same attribute
    ///     render the same bracketed form.
    /// </summary>
    internal static string AttributeName(AttributeAnchor anchor)
    {
        return BracketAttribute(anchor.SimpleDisplay);
    }

    /// <summary>
    ///     Brackets an attribute display with a trailing <c>Attribute</c> stripped from its final
    ///     segment — <c>Billing.AuditAttribute</c> → <c>[Billing.Audit]</c>. Takes an already-resolved
    ///     name so a colliding attribute list can bracket its widened, namespace-qualified form.
    /// </summary>
    private static string BracketAttribute(string name)
    {
        const string suffix = "Attribute";
        if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length);

        return "[" + name + "]";
    }

    /// <summary>
    ///     Joins backticked type names as an or-list — <c>`A` or `B`</c> (GRAMMAR §5.3, §6) — for the
    ///     <c>MustNotImplement</c> / <c>MustNotDeriveFrom</c> anchor lists. Each anchor renders its
    ///     simple name, widening to the minimal distinguishing trailing namespace segments when
    ///     anchors collide (<see cref="ResolveTypeDisplays" />) — the same rule the dependency target
    ///     lists use. An open generic renders declared type-parameter names (<c>IHandler&lt;T&gt;</c>).
    /// </summary>
    internal static string TypeList(IReadOnlyList<Type> types)
    {
        var display = ResolveTypeDisplays(types);
        return JoinReferences(types.Select(t => Backtick(display[t])).ToList());
    }

    /// <summary>
    ///     Joins backticked, <c>Attribute</c>-stripped, bracketed attribute names as an or-list —
    ///     <c>`[Table]` or `[ComplexType]`</c> (GRAMMAR §5.3, §6) — for the <c>MustNotBeAttributedWith</c>
    ///     anchor list. Colliding attribute names widen inside the brackets by the shared
    ///     minimal-trailing-segments rule (<see cref="ResolvePathDisplays" />):
    ///     <c>`[Billing.Audit]` or `[Sales.Audit]`</c>. A string anchor widens exactly as its
    ///     <c>typeof</c> twin does, because both supply the same path.
    /// </summary>
    internal static string AttributeList(IReadOnlyList<AttributeAnchor> anchors)
    {
        // Anchors carry their own path, so a typeof and a string anchor widen through the identical
        // primitive. Collision keys on the anchor's simple name; a Foo/FooAttribute pair that shares a
        // bracket display (distinct simple names) is not widened — an accepted v1 corner.
        var paths = anchors.Select(anchor => anchor.PathSegments).ToList();
        var displays = ResolvePathDisplays(paths);
        return JoinReferences(displays.Select(display => Backtick(BracketAttribute(display))).ToList());
    }

    /// <summary>
    ///     Joins already-rendered reference fragments with no Oxford comma (GRAMMAR §6):
    ///     2 → <c>`A` or `B`</c>; 3+ → <c>`A`, `B` or `C`</c>.
    /// </summary>
    internal static string JoinReferences(IReadOnlyList<string> references)
    {
        switch (references.Count)
        {
            case 0:
                return string.Empty;
            case 1:
                return references[0];
            case 2:
                return references[0] + " or " + references[1];
            default:
                var builder = new StringBuilder();
                for (var i = 0; i < references.Count - 1; i++)
                {
                    if (i > 0) builder.Append(", ");

                    builder.Append(references[i]);
                }

                builder.Append(" or ");
                builder.Append(references[references.Count - 1]);
                return builder.ToString();
        }
    }

    /// <summary>
    ///     Maps each type to its display name, qualifying colliding simple names with the minimal
    ///     distinguishing trailing namespace segments (GRAMMAR §6): a lone simple name stays simple
    ///     (<c>Order</c>); a colliding set widens outward until distinct (<c>Billing.Order</c> /
    ///     <c>Sales.Order</c>). The <see cref="Type" />-keyed face of
    ///     <see cref="ResolvePathDisplays" />, for the lists whose operands are reflected types — the
    ///     dependency reference/target lists (through <see cref="SentenceRenderer" />) and the
    ///     hierarchy anchor list (<see cref="TypeList" />).
    /// </summary>
    internal static Dictionary<Type, string> ResolveTypeDisplays(IReadOnlyList<Type> types)
    {
        var paths = types.Select(TypeName.PathSegments).ToList();
        var displays = ResolvePathDisplays(paths);

        // Keyed back by Type for the callers that render out of order (a target list interleaves bare
        // types with pattern selections); a type listed twice re-assigns its own display.
        var result = new Dictionary<Type, string>();
        for (var i = 0; i < types.Count; i++) result[types[i]] = displays[i];

        return result;
    }

    /// <summary>
    ///     The collision primitive itself (GRAMMAR §6), over bare dot-separated paths: each path renders
    ///     as its last segment, and a set of paths sharing that last segment widens outward together —
    ///     by the minimal number of trailing segments that tells them apart — until distinct. Returns
    ///     displays positionally aligned with <paramref name="paths" />. Taking paths rather than
    ///     <see cref="Type" />s is what lets a string attribute anchor widen identically to its
    ///     <c>typeof</c> twin.
    /// </summary>
    internal static IReadOnlyList<string> ResolvePathDisplays(IReadOnlyList<IReadOnlyList<string>> paths)
    {
        var displays = new string[paths.Count];
        foreach (var group in Enumerable.Range(0, paths.Count).GroupBy(index => Leaf(paths[index])))
        {
            var indices = group.ToList();

            // Dedupe by full path before judging the group: the SAME operand written twice is one
            // member, not a collision — and widening could never separate it, so a naive count would
            // qualify every repeated operand to its full path.
            int memberCount = indices.Select(index => Qualify(paths[index], paths[index].Count)).Distinct().Count();
            if (memberCount == 1)
            {
                foreach (int index in indices) displays[index] = Leaf(paths[index]);

                continue;
            }

            // Widen from the simple name outward until every colliding member is distinct.
            int maxDepth = indices.Max(index => paths[index].Count);
            int chosen = maxDepth;
            for (var count = 1; count <= maxDepth; count++)
                if (indices.Select(index => Qualify(paths[index], count)).Distinct().Count() == memberCount)
                {
                    chosen = count;
                    break;
                }

            foreach (int index in indices) displays[index] = Qualify(paths[index], chosen);
        }

        return displays;
    }

    /// <summary>The path's last segment — the simple name every collision group keys on.</summary>
    private static string Leaf(IReadOnlyList<string> segments)
    {
        return segments[segments.Count - 1];
    }

    /// <summary>The last <paramref name="segmentCount" /> segments of a path, dotted.</summary>
    private static string Qualify(IReadOnlyList<string> segments, int segmentCount)
    {
        int take = Math.Min(Math.Max(segmentCount, 1), segments.Count);
        return string.Join(".", segments.Skip(segments.Count - take));
    }
}