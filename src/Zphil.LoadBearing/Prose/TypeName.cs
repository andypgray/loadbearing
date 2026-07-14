using System.Text;

namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Renders a <see cref="Type" /> to prose (GRAMMAR §5–6). Simple names are generic-aware and
///     use declared type-parameter names (<c>IHandler&lt;T&gt;</c>,
///     <c>
///         IDictionary&lt;TKey,
///         TValue&gt;
///     </c>
///     ); the full-path helpers back the colliding-simple-name qualification rule.
/// </summary>
internal static class TypeName
{
    /// <summary>The simple (unqualified) display name, generic-aware: <c>IHandler&lt;T&gt;</c>, <c>Order</c>.</summary>
    internal static string Simple(Type type)
    {
        if (!type.IsGenericType) return type.Name;

        string baseName = StripArity(type.Name);
        string arguments = string.Join(", ", type.GetGenericArguments().Select(Simple));
        return baseName + "<" + arguments + ">";
    }

    /// <summary>
    ///     The extraction-format fully-qualified name for a reflection type — byte-identical to what
    ///     Roslyn's <c>SymbolDisplayFormat</c> (omitted global namespace, name-and-containing-types-
    ///     and-namespaces, include-type-parameters, no special-type keywords) produces for the
    ///     equivalent symbol. This is the load-bearing correspondence that lets a spec's
    ///     <c>typeof(...)</c> match an extracted <see cref="Codebase.TypeNode.FullName" /> /
    ///     <see cref="Codebase.TypeConstruction.FullName" /> (GRAMMAR §4.1, §5.2). Forms:
    ///     <list type="bullet">
    ///         <item>non-generic: namespace + <see cref="Type.DeclaringType" /> chain dotted (<c>MyApp.Domain.Order.Line</c>)</item>
    ///         <item>open generic: declared parameter names (<c>MyApp.Web.IHandler&lt;T&gt;</c>)</item>
    ///         <item>
    ///             closed generic: fully-qualified recursive arguments (<c>IHandler&lt;MyApp.Web.InvoiceCreated&gt;</c>,
    ///             <c>IHandler&lt;System.Int32&gt;</c>)
    ///         </item>
    ///         <item>arrays: <c>Elem[]</c>; global-namespace types: bare simple name</item>
    ///     </list>
    /// </summary>
    /// <exception cref="UnrepresentableTypeException">
    ///     The type is a pointer, by-ref, or partially-open construction — no source-level form exists.
    /// </exception>
    internal static string FullDisplay(Type type)
    {
        if (type.IsByRef || type.IsPointer) throw new UnrepresentableTypeException(type);

        if (type.IsArray) return FullDisplay(type.GetElementType()!) + "[]";

        if (type.IsGenericParameter) return type.Name;

        // A partially-open construction (some arguments bound, some free) is not source-writable and
        // has no extraction analog — the definition or a fully-closed construction do.
        if (type.ContainsGenericParameters && !type.IsGenericTypeDefinition) throw new UnrepresentableTypeException(type);

        // The containing-type chain, outermost-first (includes the type itself). Namespace is taken
        // once from the outermost link; nested types inherit it.
        var chain = new List<Type>();
        for (Type? link = type; link is not null; link = link.DeclaringType) chain.Insert(0, link);

        var arguments = type.IsGenericType ? type.GetGenericArguments() : Array.Empty<Type>();

        var builder = new StringBuilder();
        string? @namespace = chain[0].Namespace;
        if (!string.IsNullOrEmpty(@namespace)) builder.Append(@namespace).Append('.');

        var consumed = 0;
        for (var i = 0; i < chain.Count; i++)
        {
            if (i > 0) builder.Append('.');
            builder.Append(StripArity(chain[i].Name));

            // Roslyn distributes the leaf's generic arguments across the chain: each link consumes
            // the number of parameters it introduces (its own arity minus its parent's).
            int arity = IntroducedArity(chain[i]);
            if (arity == 0) continue;

            builder.Append('<');
            for (var a = 0; a < arity; a++)
            {
                if (a > 0) builder.Append(", ");
                builder.Append(FullDisplay(arguments[consumed + a]));
            }

            builder.Append('>');
            consumed += arity;
        }

        return builder.ToString();
    }

    private static int IntroducedArity(Type type)
    {
        if (!type.IsGenericType) return 0;

        int own = type.GetGenericArguments().Length;
        int parent = type.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments().Length : 0;
        return own - parent;
    }

    /// <summary>The last <paramref name="segmentCount" /> dot-segments of the type's full path.</summary>
    internal static string Qualified(Type type, int segmentCount)
    {
        var segments = PathSegments(type);
        int take = Math.Min(Math.Max(segmentCount, 1), segments.Count);
        return string.Join(".", segments.Skip(segments.Count - take));
    }

    /// <summary>The number of dot-segments available (namespace depth plus the simple name).</summary>
    internal static int SegmentDepth(Type type)
    {
        return PathSegments(type).Count;
    }

    private static List<string> PathSegments(Type type)
    {
        var segments = new List<string>();
        if (!string.IsNullOrEmpty(type.Namespace)) segments.AddRange(type.Namespace!.Split('.'));

        segments.Add(Simple(type));
        return segments;
    }

    private static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name.Substring(0, tick);
    }
}