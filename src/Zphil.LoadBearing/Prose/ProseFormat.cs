using System.Text;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Low-level prose formatting helpers shared by the vocabulary nodes and the renderer
///     (GRAMMAR §6): backtick wrapping, sentence-initial capitalization, kind pluralization,
///     attribute bracketing, and the no-Oxford-comma reference-list join.
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
        string simple = TypeName.Simple(type);
        const string suffix = "Attribute";
        if (simple.Length > suffix.Length && simple.EndsWith(suffix, StringComparison.Ordinal)) simple = simple.Substring(0, simple.Length - suffix.Length);

        return "[" + simple + "]";
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
}