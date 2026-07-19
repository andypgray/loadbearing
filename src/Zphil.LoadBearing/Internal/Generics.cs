namespace Zphil.LoadBearing.Internal;

/// <summary>
///     The one place that answers "is this a constructed generic, and what is its open definition?"
///     (GRAMMAR §4.1/§4.5). A member anchor is definition-level, so a constructed generic
///     (<c>Task&lt;int&gt;</c>) is normalized to its definition (<c>Task&lt;&gt;</c>) at every seam that
///     mints or validates one: <see cref="MemberExpressionResolver" /> (the expression anchor's declaring
///     type), <see cref="Member" /> (deciding <c>IsMethod</c>), and <see cref="Validation.SpecValidator" />
///     (the member-declaration and <c>.Returning</c> checks). Deliberately distinct from the "is this an
///     <em>open</em> definition?" question the hierarchy matchers ask
///     (<see cref="Checking.SelectionEvaluator.InterfaceMatcher" /> and its siblings) — those stay as-is.
/// </summary>
internal static class Generics
{
    internal static bool IsConstructed(Type type)
    {
        return type.IsGenericType && !type.IsGenericTypeDefinition;
    }

    internal static Type Definition(Type type)
    {
        return IsConstructed(type) ? type.GetGenericTypeDefinition() : type;
    }
}