using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 adjective vocabulary (GRAMMAR §5.2) as extension methods on <see cref="Selection" />.
///     Each appends one closed-vocabulary adjective and returns a fresh selection re-stamped with
///     the same <see cref="Arch" /> owner. Selections are immutable values; each call yields a new
///     one, so a selection can be reused and refined in different directions.
/// </summary>
public static class SelectionAdjectives
{
    /// <summary>Narrows to types declared in a namespace glob: " in `MyApp.Web.*`".</summary>
    public static Selection InNamespace(this Selection selection, string glob)
    {
        return Append(selection, new InNamespaceAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>Narrows to a kind, substituting the subject head plural ("interfaces").</summary>
    public static Selection OfKind(this Selection selection, TypeKind kind)
    {
        return Append(selection, new OfKindAdjective(kind));
    }

    /// <summary>Narrows to types whose name ends with a suffix: " named `*Controller`".</summary>
    public static Selection WithSuffix(this Selection selection, string suffix)
    {
        return Append(selection, new WithSuffixAdjective(NotNull(suffix, nameof(suffix))));
    }

    /// <summary>Narrows to types whose name starts with a prefix: " named `Legacy*`".</summary>
    public static Selection WithPrefix(this Selection selection, string prefix)
    {
        return Append(selection, new WithPrefixAdjective(NotNull(prefix, nameof(prefix))));
    }

    /// <summary>Narrows to types whose name matches a glob: " whose name matches `*Repo*`".</summary>
    public static Selection WithNameMatching(this Selection selection, string glob)
    {
        return Append(selection, new WithNameMatchingAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>Narrows to types implementing an interface (open generic = any construction).</summary>
    public static Selection Implementing(this Selection selection, Type type)
    {
        return Append(selection, new ImplementingAdjective(NotNull(type, nameof(type))));
    }

    /// <summary>Narrows to types derived from a base type: " derived from `ControllerBase`".</summary>
    public static Selection DerivedFrom(this Selection selection, Type type)
    {
        return Append(selection, new DerivedFromAdjective(NotNull(type, nameof(type))));
    }

    /// <summary>Narrows to types carrying an attribute: " attributed with `[ApiController]`".</summary>
    public static Selection AttributedWith(this Selection selection, Type type)
    {
        return Append(selection, new AttributedWithAdjective(NotNull(type, nameof(type))));
    }

    /// <summary>
    ///     Narrows to types implementing <typeparamref name="T" /> — <c>≡ Implementing(typeof(T))</c>; an open generic
    ///     stays <c>typeof</c>.
    /// </summary>
    public static Selection Implementing<T>(this Selection selection)
    {
        return selection.Implementing(typeof(T));
    }

    /// <summary>
    ///     Narrows to types derived from <typeparamref name="T" /> — <c>≡ DerivedFrom(typeof(T))</c>; an open generic
    ///     stays <c>typeof</c>.
    /// </summary>
    public static Selection DerivedFrom<T>(this Selection selection)
    {
        return selection.DerivedFrom(typeof(T));
    }

    /// <summary>Narrows to types carrying attribute <typeparamref name="T" /> — <c>≡ AttributedWith(typeof(T))</c>.</summary>
    public static Selection AttributedWith<T>(this Selection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>Excludes another selection; canonicalized to sentence-final (GRAMMAR §6).</summary>
    public static Selection Except(this Selection selection, Selection exclusion)
    {
        return Append(selection, new ExceptAdjective(NotNull(exclusion, nameof(exclusion))));
    }

    /// <summary>
    ///     The selector-position escape hatch. The predicate is stored, never evaluated;
    ///     the required <paramref name="description" /> is what renders as a sentence-final relative
    ///     clause (GRAMMAR §5.6). A blank description fails spec build (validation §8 item 5).
    /// </summary>
    public static Selection Where(this Selection selection, Func<ITypeInfo, bool> predicate, string description)
    {
        return Append(selection, new WhereAdjective(NotNull(predicate, nameof(predicate)), description));
    }

    private static Selection Append(Selection selection, SelectionAdjective adjective)
    {
        Guard.NotNull(selection, nameof(selection));
        var adjectives = new List<SelectionAdjective>(selection.Adjectives) { adjective };
        return new RefinedSelection(selection.Owner, selection.Noun, adjectives);
    }

    private static T NotNull<T>(T value, string paramName)
        where T : class
    {
        return Guard.NotNull(value, paramName);
    }
}