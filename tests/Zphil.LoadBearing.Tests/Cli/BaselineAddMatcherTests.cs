using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     Pins <see cref="BaselineAddMatcher" />'s resolution semantics — a name matches a type by its
///     FullName or its <c>T:</c> symbol ID, and an edge's source and target must match the same
///     violation — and the shapes of its two loud refusals: a no-match that lists the rule's current
///     grandfatherable violations (or reports it has none), and an ambiguity that lists the matched
///     identities in symbol-ID form and steers to the <c>T:</c> form. Pure over synthetic nodes.
/// </summary>
public sealed class BaselineAddMatcherTests
{
    [Fact]
    public void ResolveEdge_ByFullNameSymbolIdAndMixed_ReturnsMatchingViolation()
    {
        Violation edge = Violation.Reference(Node("N.A", "T:N.A"), Node("N.B", "T:N.B"), Array.Empty<SourceLocation>());
        Violation[] violations = [edge];

        Violation byName = BaselineAddMatcher.ResolveEdge("r", violations, "N.A", "N.B");
        Violation byId = BaselineAddMatcher.ResolveEdge("r", violations, "T:N.A", "T:N.B");
        Violation mixed = BaselineAddMatcher.ResolveEdge("r", violations, "N.A", "T:N.B");

        byName.ShouldBeSameAs(edge);
        byId.ShouldBeSameAs(edge);
        mixed.ShouldBeSameAs(edge);
    }

    [Fact]
    public void ResolveEdge_SourceAndTargetMatchDifferentViolations_NoMatchListsBothCandidates()
    {
        Violation ab = Violation.Reference(Node("N.A", "T:N.A"), Node("N.B", "T:N.B"), Array.Empty<SourceLocation>());
        Violation cd = Violation.Reference(Node("N.C", "T:N.C"), Node("N.D", "T:N.D"), Array.Empty<SourceLocation>());
        Violation[] violations = [ab, cd];

        var error = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveEdge("r", violations, "N.A", "N.D"));

        error.Message.ShouldContain("no current violation of 'r' matches --source 'N.A' --target 'N.D'");
        error.Message.ShouldContain("the baseline records observed reality");
        error.Message.ShouldContain("  N.A -> N.B");
        error.Message.ShouldContain("  N.C -> N.D");
    }

    [Fact]
    public void ResolveSubject_ByFullNameAndSymbolId_ReturnsMatchingViolation()
    {
        Violation shape = Violation.Shape(Node("N.S", "T:N.S"), Array.Empty<SourceLocation>());
        Violation[] violations = [shape];

        Violation byName = BaselineAddMatcher.ResolveSubject("r", violations, "N.S");
        Violation byId = BaselineAddMatcher.ResolveSubject("r", violations, "T:N.S");

        byName.ShouldBeSameAs(shape);
        byId.ShouldBeSameAs(shape);
    }

    [Fact]
    public void ResolveSubject_NoMatch_ListsCandidatesOrReportsNoViolations()
    {
        Violation shape = Violation.Shape(Node("N.S", "T:N.S"), Array.Empty<SourceLocation>());

        var withCandidates = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveSubject("r", [shape], "N.Other"));
        var noViolations = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveSubject("r", Array.Empty<Violation>(), "N.Other"));

        withCandidates.Message.ShouldContain("the baseline records observed reality");
        withCandidates.Message.ShouldContain("  N.S");
        noViolations.Message.ShouldContain("the baseline records observed reality; the rule currently has no violations.");
    }

    [Fact]
    public void ResolveSubject_TwoIdentitiesShareFullName_AmbiguousListsBothSymbolIds()
    {
        Violation dup1 = Violation.Shape(Node("N.Dup", "T:N.Dup`1"), Array.Empty<SourceLocation>());
        Violation dup2 = Violation.Shape(Node("N.Dup", "T:N.Dup`2"), Array.Empty<SourceLocation>());
        Violation[] violations = [dup1, dup2];

        var error = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveSubject("r", violations, "N.Dup"));

        error.Message.ShouldContain("--subject 'N.Dup' matches more than one current violation of 'r'");
        error.Message.ShouldContain("T:N.Dup`1");
        error.Message.ShouldContain("T:N.Dup`2");
        error.Message.ShouldEndWith("Use the 'T:' symbol ID form.");
    }

    [Fact]
    public void ResolveEdge_TwoIdentitiesShareFullNames_AmbiguousListsSymbolIdPairs()
    {
        Violation edge1 = Violation.Reference(Node("N.A", "T:N.A`1"), Node("N.B", "T:N.B`1"), Array.Empty<SourceLocation>());
        Violation edge2 = Violation.Reference(Node("N.A", "T:N.A`2"), Node("N.B", "T:N.B`2"), Array.Empty<SourceLocation>());
        Violation[] violations = [edge1, edge2];

        var error = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveEdge("r", violations, "N.A", "N.B"));

        error.Message.ShouldContain("--source 'N.A' --target 'N.B' matches more than one current violation of 'r'");
        error.Message.ShouldContain("T:N.A`1 -> T:N.B`1");
        error.Message.ShouldContain("T:N.A`2 -> T:N.B`2");
        error.Message.ShouldEndWith("Use the 'T:' symbol ID form.");
    }

    [Fact]
    public void ResolveEdge_TwoViolationsShareOneIdentity_ResolvesToFirstNotAmbiguous()
    {
        Violation first = Violation.Reference(Node("N.A", "T:N.A"), Node("N.B", "T:N.B"), Array.Empty<SourceLocation>());
        Violation second = Violation.Reference(Node("N.A", "T:N.A"), Node("N.B", "T:N.B"), Array.Empty<SourceLocation>());
        Violation[] violations = [first, second];

        Violation resolved = BaselineAddMatcher.ResolveEdge("r", violations, "N.A", "N.B");

        resolved.ShouldBeSameAs(first);
    }

    private static TypeNode Node(string fullName, string symbolId)
    {
        return new TypeNode(
            fullName, symbolId, fullName, "N", TypeKind.Class,
            Accessibility.Public, false, false, false, false, "Proj", false);
    }
}