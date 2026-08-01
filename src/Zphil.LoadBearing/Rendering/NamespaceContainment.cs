namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Glob-vs-glob containment, the one question the law diagram's nesting rests on: does every
///     namespace one glob matches also match the other? <see cref="NamespacePattern" /> answers the
///     namespace-vs-glob question and is the semantics mirrored here — a trailing <c>.*</c> is the
///     self-inclusive subtree operator, and its literal prefix is compared literally.
///     <para>
///         Only two glob shapes are containment-comparable: an exact name (<c>Zphil.LoadBearing</c>)
///         and a subtree whose prefix carries no wildcard (<c>MyApp.Web.*</c>). Every other legal
///         shape — an interior standalone <c>*</c>, a partial-segment <c>*</c>, the lone <c>*</c> —
///         matches a set no prefix comparison can decide, so it is reported as not contained. That is
///         the deliberate asymmetry: a false negative costs a drawing one flat node, and a false
///         positive nests a node under a parent that does not contain it, which is the diagram
///         telling the reader something untrue.
///     </para>
/// </summary>
internal static class NamespaceContainment
{
    /// <summary>
    ///     Whether every namespace <paramref name="inner" /> matches is also matched by
    ///     <paramref name="outer" />. Reflexive on identical comparable globs; false whenever either
    ///     glob is a shape this comparison cannot decide.
    /// </summary>
    internal static bool Implies(string inner, string outer)
    {
        if (inner is null || outer is null) return false;

        if (!TryParse(inner, out string innerPrefix, out bool innerSubtree)) return false;

        if (!TryParse(outer, out string outerPrefix, out bool outerSubtree)) return false;

        // A subtree covers its own prefix and every descendant of it, so an inner glob is inside it
        // exactly when the inner prefix is the outer prefix or sits below it.
        if (outerSubtree)
            return string.Equals(innerPrefix, outerPrefix, StringComparison.Ordinal)
                   || innerPrefix.StartsWith(outerPrefix + ".", StringComparison.Ordinal);

        // An exact outer covers one namespace and nothing under it. Only an identical exact inner fits;
        // a subtree inner also covers descendants the exact glob never reaches.
        return !innerSubtree && string.Equals(innerPrefix, outerPrefix, StringComparison.Ordinal);
    }

    // The comparable shapes, reduced to (literal prefix, is-subtree). Anything else fails to parse and
    // the caller reports no containment in either direction.
    private static bool TryParse(string glob, out string prefix, out bool subtree)
    {
        if (glob.EndsWith(".*", StringComparison.Ordinal))
        {
            prefix = glob.Substring(0, glob.Length - 2);
            subtree = true;
            return prefix.Length > 0 && prefix.IndexOf('*') < 0;
        }

        prefix = glob;
        subtree = false;
        return glob.Length > 0 && glob.IndexOf('*') < 0;
    }
}