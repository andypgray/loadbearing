using Shouldly;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     Case-sensitive string assertions, shadowing the Shouldly originals that default to
///     <see cref="Case.Insensitive" />.
/// </summary>
/// <remarks>
///     <para>
///         Almost every string this suite asserts on is a pinned message, a rendered law sentence, or a
///         symbol ID — text where case <em>is</em> the content. An insensitive default means a rename that
///         changes only capitalisation passes, which is the kind of green that costs a release. These
///         overloads take fewer parameters than Shouldly's, so a call that names no
///         <see cref="Case" /> binds here; a caller who wants the insensitive comparison asks for it by
///         passing <c>Case.Insensitive</c>, which has no overload here and falls through to Shouldly.
///     </para>
///     <para>
///         The namespace is load-bearing. Extension lookup walks outward from the innermost enclosing
///         namespace and stops at the first one holding an applicable method, so a class declared in the
///         suite's root namespace is reached before the compilation unit's <c>using Shouldly</c> — from
///         every test file, in every nested namespace, with no edit to any of them.
///         <see cref="Checking.ShouldlyExtensionsTests" /> pins that from a nested namespace.
///     </para>
///     <para>
///         The inner calls are written as qualified static calls rather than
///         <c>actual.ShouldContain(…)</c>, so they cannot re-bind to the overload being defined. The
///         ReSharper disables are load-bearing: a full cleanup rewrites a qualified static call back into
///         the extension form, which would reintroduce the recursion silently.
///     </para>
///     <para>
///         Collection assertions are untouched — a different receiver type, so nothing here is applicable
///         and lookup falls through.
///     </para>
/// </remarks>
internal static class ShouldlyExtensions
{
    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldContain(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldContain(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldContain(this string actual, string expected, string? customMessage)
    {
        ShouldBeStringTestExtensions.ShouldContain(actual, expected, Case.Sensitive, customMessage);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldNotContain(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldNotContain(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldStartWith(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldStartWith(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldEndWith(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldEndWith(actual, expected, Case.Sensitive);
    }
}
