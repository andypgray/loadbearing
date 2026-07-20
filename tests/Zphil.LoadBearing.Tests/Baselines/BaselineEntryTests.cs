using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Baselines;

/// <summary>
///     Value semantics for <see cref="BaselineEntry" /> — the identity a baseline keys on (GRAMMAR §4.3).
///     Edge and subject entries are ordinal-equal only when every slot matches, and equal entries share
///     a hash code (so a <see cref="HashSet{T}" /> answers membership correctly). An optional
///     <see cref="BaselineEntry.Because" /> attribution is excluded from equality, so an attributed entry
///     and its unattributed twin dedupe as one.
/// </summary>
public sealed class BaselineEntryTests
{
    [Fact]
    public void ForEdge_SamePair_AreEqualAndShareHashCode()
    {
        BaselineEntry a = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        BaselineEntry b = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ForEdge_DifferentTarget_AreNotEqual()
    {
        BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .ShouldNotBe(BaselineEntry.ForEdge("T:N.Src", "T:N.Other"));
    }

    [Fact]
    public void ForEdge_ConstructionViolationIdentity_IsAnOrdinaryEdgeEntry()
    {
        // A construction violation's identity is a plain (source, constructed) ForEdge entry (GRAMMAR §4.3) —
        // the very shape a reference uses — so grandfathering construction needs zero new baseline format: the
        // identity is value-equal to, hash-equal to, and set-dedupes with a hand-built ForEdge and its twin.
        BaselineEntry identity = Violation
            .Construction(Node("N.Factory"), Node("N.Widget"), Array.Empty<SourceLocation>())
            .BaselineIdentity()!;

        identity.ShouldBe(BaselineEntry.ForEdge("T:N.Factory", "T:N.Widget"));
        identity.GetHashCode().ShouldBe(BaselineEntry.ForEdge("T:N.Factory", "T:N.Widget").GetHashCode());
        identity.Subject.ShouldBeNull();
        new HashSet<BaselineEntry> { identity }
            .Contains(BaselineEntry.ForEdge("T:N.Factory", "T:N.Widget").WithBecause("INC-1")).ShouldBeTrue();
    }

    [Fact]
    public void ForEdge_SwappedSourceAndTarget_AreNotEqual()
    {
        BaselineEntry.ForEdge("T:N.A", "T:N.B")
            .ShouldNotBe(BaselineEntry.ForEdge("T:N.B", "T:N.A"));
    }

    [Fact]
    public void ForSubject_SameId_AreEqualAndShareHashCode()
    {
        BaselineEntry a = BaselineEntry.ForSubject("T:N.Type");
        BaselineEntry b = BaselineEntry.ForSubject("T:N.Type");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ForSubject_AndForEdge_WithOverlappingStrings_AreNotEqual()
    {
        // A subject "T:N.Src" must never collide with an edge whose source is "T:N.Src".
        BaselineEntry.ForSubject("T:N.Src")
            .ShouldNotBe(BaselineEntry.ForEdge("T:N.Src", ""));
    }

    [Fact]
    public void HashSet_UsesValueEquality()
    {
        var set = new HashSet<BaselineEntry> { BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt") };

        set.Contains(BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")).ShouldBeTrue();
        set.Contains(BaselineEntry.ForEdge("T:N.Src", "T:N.Other")).ShouldBeFalse();
    }

    [Fact]
    public void WithBecause_SameIdentity_AreEqualAndShareHashCode()
    {
        BaselineEntry plain = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        BaselineEntry attributed = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt").WithBecause("INC-1234");

        attributed.ShouldBe(plain);
        attributed.GetHashCode().ShouldBe(plain.GetHashCode());
    }

    [Fact]
    public void WithBecause_InHashSet_DedupesAgainstUnattributedTwin()
    {
        BaselineEntry plain = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        BaselineEntry attributed = plain.WithBecause("INC-1234");

        new HashSet<BaselineEntry> { plain }.Contains(attributed).ShouldBeTrue();
        new HashSet<BaselineEntry> { attributed }.Contains(plain).ShouldBeTrue();
    }

    [Fact]
    public void WithBecause_PreservesIdentitySlots_AndSetsBecause()
    {
        BaselineEntry original = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        BaselineEntry attributed = original.WithBecause("keep until INC-1234");

        attributed.Source.ShouldBe("T:N.Src");
        attributed.Target.ShouldBe("T:N.Tgt");
        attributed.Subject.ShouldBeNull();
        attributed.Because.ShouldBe("keep until INC-1234");
        original.Because.ShouldBeNull();
    }

    [Fact]
    public void WithBecause_BlankOrMultiline_Throws()
    {
        BaselineEntry entry = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        Should.Throw<ArgumentException>(() => entry.WithBecause("   ")).Message.ShouldContain("non-blank single line");
        Should.Throw<ArgumentException>(() => entry.WithBecause("")).Message.ShouldContain("non-blank single line");
        Should.Throw<ArgumentException>(() => entry.WithBecause("a\rb")).Message.ShouldContain("non-blank single line");
        Should.Throw<ArgumentException>(() => entry.WithBecause("a\nb")).Message.ShouldContain("non-blank single line");
    }

    // A shallow TypeNode whose SymbolId is `T:` + FullName — the construction identity reads only those.
    private static TypeNode Node(string fullName)
    {
        return new TypeNode(
            fullName, $"T:{fullName}", fullName, "N", TypeKind.Class, Accessibility.Public,
            false, false, false, false, "Proj", false);
    }
}