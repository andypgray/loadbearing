using System.Text;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The Mermaid text primitives both diagram renderers share: node-ID slugging with a
///     collision-safe dedupe, and label escaping for the characters Mermaid reads as markup. Extracted
///     from <see cref="GraphDiagramRenderer" /> when <see cref="LawDiagramRenderer" /> arrived — one
///     escaping rule for two fences in one artifact, so a character that is safe in the survey cannot be
///     unsafe in the law.
/// </summary>
internal static class MermaidText
{
    /// <summary>
    ///     ASCII letters and digits survive; everything else becomes an underscore. Deliberately not
    ///     <see cref="char.IsLetterOrDigit(char)" />, which is Unicode-aware and would leave accented
    ///     letters in an identifier position where Mermaid's tolerance is unknown.
    /// </summary>
    internal static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            bool ascii = (character >= 'a' && character <= 'z')
                         || (character >= 'A' && character <= 'Z')
                         || (character >= '0' && character <= '9');
            builder.Append(ascii ? character : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    ///     A label's text with the four characters Mermaid reads as markup sent out as the entities it
    ///     reads back as themselves: a double quote would end a quoted label early, angle brackets open
    ///     markup (a generic type name is full of them), and <c>#</c> opens an entity reference
    ///     (<c>#35;</c> is the documented escape for a literal one). The <c>#</c> pass must run FIRST —
    ///     reversed, it would rewrite the <c>#</c> of an emitted <c>#quot;</c> into <c>#35;quot;</c>.
    /// </summary>
    internal static string Label(string text)
    {
        return text
            .Replace("#", "#35;")
            .Replace("\"", "#quot;")
            .Replace("<", "#lt;")
            .Replace(">", "#gt;");
    }

    /// <summary>
    ///     One node ID per name, in order: the prefix plus a deterministic slug, deduped with an ordinal
    ///     suffix so two names that slug alike (<c>MyApp.Web</c> and <c>MyApp-Web</c>) still get distinct
    ///     nodes. The prefix is the caller's — it is what keeps a name from ever being lexed as something
    ///     other than a node, and what keeps an ID from starting with the <c>o</c> or <c>x</c> Mermaid's
    ///     link rules read as an arrowhead.
    /// </summary>
    internal static IReadOnlyList<string> UniqueIds(string prefix, IReadOnlyList<string> names)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>(names.Count);
        foreach (string name in names)
        {
            string slug = prefix + Slug(name);
            string candidate = slug;
            var suffix = 2;
            while (!taken.Add(candidate)) candidate = $"{slug}_{suffix++}";

            ids.Add(candidate);
        }

        return ids;
    }
}
