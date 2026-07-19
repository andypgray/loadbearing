using Shouldly;
using Xunit;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Tests.Internal;

/// <summary>
///     The shared generic-definition helper (<see cref="Generics" />) that the resolver, the
///     <see cref="Member" /> leaf, and the spec validator use to normalize a constructed generic anchor to
///     its definition (GRAMMAR §4.1/§4.5). One transform, one predicate — pinned here so the DRY extraction
///     stays behavior-preserving.
/// </summary>
public class GenericsTests
{
    [Fact]
    public void Definition_ConstructedGeneric_ReturnsOpenDefinition()
    {
        // A constructed generic collapses to its open definition (Task<int> → Task<>, Dictionary<,>).
        Generics.Definition(typeof(Task<int>)).ShouldBe(typeof(Task<>));
        Generics.Definition(typeof(Dictionary<string, int>)).ShouldBe(typeof(Dictionary<,>));

        // An open definition and a non-generic type are returned unchanged.
        Generics.Definition(typeof(Task<>)).ShouldBe(typeof(Task<>));
        Generics.Definition(typeof(string)).ShouldBe(typeof(string));
    }

    [Fact]
    public void IsConstructed_DistinguishesClosedFromOpenAndNonGeneric()
    {
        // Only a closed/constructed generic is "constructed"; an open definition and a non-generic are not.
        Generics.IsConstructed(typeof(Task<int>)).ShouldBeTrue();
        Generics.IsConstructed(typeof(Dictionary<string, int>)).ShouldBeTrue();

        Generics.IsConstructed(typeof(Task<>)).ShouldBeFalse();
        Generics.IsConstructed(typeof(string)).ShouldBeFalse();
        Generics.IsConstructed(typeof(int)).ShouldBeFalse();
    }
}