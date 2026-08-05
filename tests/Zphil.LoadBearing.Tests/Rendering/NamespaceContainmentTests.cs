using Shouldly;
using Xunit;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     <see cref="NamespaceContainment" /> facts: the two comparable glob shapes and their four
///     combinations, the self-inclusiveness the subtree operator inherits from
///     <see cref="NamespacePattern" />, the dot boundary a prefix comparison must respect, and the
///     shapes this comparison refuses to decide. The refusals are the load-bearing half: a wrong
///     "contained" nests a node under a parent that does not contain it, which is the law diagram
///     telling its reader something untrue.
/// </summary>
public sealed class NamespaceContainmentTests
{
    [Theory]
    // Exact inside exact: only itself.
    [InlineData("MyApp.Domain", "MyApp.Domain")]
    // Exact inside a subtree, including the subtree's own prefix — `.*` is self-inclusive (GRAMMAR §4.2).
    [InlineData("MyApp.Domain.Orders", "MyApp.*")]
    [InlineData("MyApp", "MyApp.*")]
    [InlineData("MyApp.Domain", "MyApp.Domain.*")]
    // Subtree inside a subtree, including itself.
    [InlineData("MyApp.Domain.*", "MyApp.*")]
    [InlineData("MyApp.*", "MyApp.*")]
    public void Implies_AContainedGlob_IsTrue(string inner, string outer)
    {
        NamespaceContainment.Implies(inner, outer).ShouldBeTrue();
    }

    [Theory]
    // An exact glob covers one namespace and nothing under it, so nothing wider fits inside it.
    [InlineData("MyApp.Domain", "MyApp")]
    [InlineData("MyApp.*", "MyApp")]
    // Wider never fits inside narrower.
    [InlineData("MyApp.*", "MyApp.Domain.*")]
    [InlineData("MyApp", "MyApp.Domain.*")]
    // The prefix comparison is dot-bounded: a shared character run is not a shared namespace.
    [InlineData("MyAppOther.Orders", "MyApp.*")]
    [InlineData("MyAppOther.*", "MyApp.*")]
    // Unrelated trees.
    [InlineData("Other.Domain.*", "MyApp.*")]
    public void Implies_AGlobOutsideTheOther_IsFalse(string inner, string outer)
    {
        NamespaceContainment.Implies(inner, outer).ShouldBeFalse();
    }

    [Theory]
    // An interior standalone `*` matches one segment, a partial-segment `*` matches within one, and a
    // lone `*` matches everything: all three are legal patterns (GRAMMAR §4.2) and none is a prefix this
    // comparison can decide. A dead subtree pattern — a `*` in a subtree's literal prefix — is the fourth.
    [InlineData("MyApp.*.Orders", "MyApp.*")]
    [InlineData("MyApp.*", "MyApp.*.Orders")]
    [InlineData("MyApp.Ord*", "MyApp.*")]
    [InlineData("MyApp.*", "MyApp.Ord*")]
    [InlineData("*", "MyApp.*")]
    [InlineData("MyApp.*", "*")]
    [InlineData("MyApp.*.Orders.*", "MyApp.*")]
    [InlineData("MyApp.Domain", "")]
    [InlineData("", "MyApp.*")]
    public void Implies_AShapeItCannotDecide_IsFalse(string inner, string outer)
    {
        // Never lie: a shape outside the two comparable ones reports no containment in either direction,
        // and the node it belongs to stays flat.
        NamespaceContainment.Implies(inner, outer).ShouldBeFalse();
    }
}
