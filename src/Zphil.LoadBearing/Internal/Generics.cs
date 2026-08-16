namespace Zphil.LoadBearing.Internal;

/// <summary>
///     The one place that answers "is this a constructed generic, and what is its open definition?"
///     (GRAMMAR §4.1/§4.5). A member anchor is definition-level, so a constructed generic
///     (<c>Task&lt;int&gt;</c>) is normalized to its definition (<c>Task&lt;&gt;</c>) at every seam that
///     mints or validates one. Deliberately distinct from the "is this an
///     <em>open</em> definition?" question the hierarchy matchers ask
///     (<see cref="Checking.SelectionEvaluator.InterfaceMatcher" /> and its siblings).
/// </summary>
internal static class Generics
{
    internal static bool IsConstructed(Type type)
    {
        return type is { IsGenericType: true, IsGenericTypeDefinition: false };
    }

    internal static Type Definition(Type type)
    {
        return IsConstructed(type) ? type.GetGenericTypeDefinition() : type;
    }
}
