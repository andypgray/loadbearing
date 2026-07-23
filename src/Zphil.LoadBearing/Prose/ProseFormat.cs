using System.Text;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Low-level prose formatting helpers shared by the vocabulary nodes and the renderer
///     (GRAMMAR §6): backtick wrapping, sentence-initial capitalization, kind pluralization,
///     attribute bracketing, the no-Oxford-comma reference-list join, and the colliding-simple-name
///     qualification (<see cref="ResolveTypeDisplays" />) shared by every multi-operand list.
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
    ///     <c>ApiControllerAttribute</c> → <c>[ApiController]</c> (GRAMMAR §5.2).
    /// </summary>
    internal static string AttributeName(Type type)
    {
        return BracketAttribute(TypeName.Simple(type));
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
    ///     minimal-trailing-segments rule (<see cref="ResolveTypeDisplays" />):
    ///     <c>`[Billing.Audit]` or `[Sales.Audit]`</c>.
    /// </summary>
    internal static string AttributeList(IReadOnlyList<Type> types)
    {
        var display = ResolveTypeDisplays(types);
        // Collision keys on the type's simple name; a Foo/FooAttribute pair that shares a bracket
        // display (distinct simple names) is not widened — an accepted v1 corner.
        return JoinReferences(types.Select(t => Backtick(BracketAttribute(display[t]))).ToList());
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
    ///     <c>Sales.Order</c>). The one collision primitive every multi-operand list shares — the
    ///     dependency reference/target lists (through <see cref="SentenceRenderer" />) and the
    ///     hierarchy/attribute anchor lists (<see cref="TypeList" /> / <see cref="AttributeList" />).
    /// </summary>
    internal static Dictionary<Type, string> ResolveTypeDisplays(IReadOnlyList<Type> types)
    {
        var result = new Dictionary<Type, string>();
        foreach (var group in types.GroupBy(TypeName.Simple))
        {
            var members = group.Distinct().ToList();
            if (members.Count == 1)
            {
                result[members[0]] = TypeName.Simple(members[0]);
                continue;
            }

            // Widen from the simple name outward until every colliding member is distinct.
            int maxDepth = members.Max(TypeName.SegmentDepth);
            int chosen = maxDepth;
            for (var count = 1; count <= maxDepth; count++)
                if (members.Select(t => TypeName.Qualified(t, count)).Distinct().Count() == members.Count)
                {
                    chosen = count;
                    break;
                }

            foreach (Type member in members) result[member] = TypeName.Qualified(member, chosen);
        }

        return result;
    }
}