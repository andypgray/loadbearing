using Shouldly;
using Xunit;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Tests.Correspondence;

/// <summary>
///     <see cref="TypeName.Simple" /> — the unqualified, generic-aware display name used in prose (GRAMMAR
///     §5–6). L5: the leaf of a nested generic chain owns only the type parameters it introduces, so
///     <c>Simple</c> must distribute arity the way <c>FullDisplay</c> does (via <c>IntroducedArity</c>)
///     rather than emit the whole <c>GetGenericArguments()</c> list against the leaf name.
/// </summary>
public sealed class TypeNameSimpleTests
{
    [Fact]
    public void Simple_TopLevelOpenGeneric_UsesDeclaredParameterName()
    {
        // Unchanged baseline: a top-level generic still renders its own parameters.
        TypeName.Simple(typeof(IDictionary<,>)).ShouldBe("IDictionary<TKey, TValue>");
    }

    [Fact]
    public void Simple_NestedGenericChain_RendersOnlyTheLeafsOwnArgument()
    {
        // GenericInner introduces one parameter (TInner); TOuter belongs to GenericOuter. The buggy Simple
        // rendered "GenericInner<String, Int32>" — the whole chain's arguments against the leaf name.
        TypeName.Simple(typeof(GenericOuter<string>.GenericInner<int>)).ShouldBe("GenericInner<Int32>");
    }

    [Fact]
    public void Simple_NonGenericTypeNestedInClosedGeneric_HasNoTypeArguments()
    {
        // PlainInner introduces zero parameters though it inherits the outer's; the buggy Simple emitted
        // "PlainInner<String>".
        TypeName.Simple(typeof(GenericOuter<string>.PlainInner)).ShouldBe("PlainInner");
    }

    private sealed class GenericOuter<TOuter>
    {
        public sealed class GenericInner<TInner>;

        public sealed class PlainInner;
    }
}