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
///     violation; a member <c>--target</c> matches a MemberUse violation by member full name
///     (<c>N.T.M</c>) or member symbol ID (<c>M:</c>/<c>P:</c>…), one full name covering every overload
///     (GRAMMAR §4.5) — and the shapes of its two loud refusals: a no-match that lists the rule's current
///     grandfatherable violations (or reports it has none), and an ambiguity that lists the matched
///     identities in symbol-ID form and steers to the symbol-ID form. Pure over synthetic nodes.
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
        error.Message.ShouldEndWith("Use the symbol ID form.");
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
        error.Message.ShouldEndWith("Use the symbol ID form.");
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

    [Fact]
    public void ResolveEdge_MemberByFullNameAndSymbolId_ReturnsMatchingViolation()
    {
        Violation use = Violation.MemberUse(
            Node("App.Home", "T:App.Home"),
            Member("System.DateTime", "Now", "P:System.DateTime.Now", MemberKind.Property),
            Array.Empty<SourceLocation>());
        Violation[] violations = [use];

        Violation byFullName = BaselineAddMatcher.ResolveEdge("r", violations, "App.Home", "System.DateTime.Now");
        Violation bySymbolId = BaselineAddMatcher.ResolveEdge("r", violations, "App.Home", "P:System.DateTime.Now");

        byFullName.ShouldBeSameAs(use);
        bySymbolId.ShouldBeSameAs(use);
    }

    [Fact]
    public void ResolveEdge_OverloadedMemberFullName_AmbiguousListsBothMemberIds()
    {
        // A full-name --target naming an overloaded method matches every overload → the ambiguity lists
        // the distinct member ids to retry with (GRAMMAR §4.5).
        Violation intOverload = Violation.MemberUse(
            Node("App.Cli", "T:App.Cli"), Member("N.Svc", "M", "M:N.Svc.M(System.Int32)", MemberKind.Method),
            Array.Empty<SourceLocation>());
        Violation stringOverload = Violation.MemberUse(
            Node("App.Cli", "T:App.Cli"), Member("N.Svc", "M", "M:N.Svc.M(System.String)", MemberKind.Method),
            Array.Empty<SourceLocation>());
        Violation[] violations = [intOverload, stringOverload];

        var error = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveEdge("r", violations, "App.Cli", "N.Svc.M"));

        error.Message.ShouldContain("--source 'App.Cli' --target 'N.Svc.M' matches more than one current violation of 'r'");
        error.Message.ShouldContain("M:N.Svc.M(System.Int32)");
        error.Message.ShouldContain("M:N.Svc.M(System.String)");
        error.Message.ShouldEndWith("Use the symbol ID form.");
    }

    [Fact]
    public void ResolveEdge_MemberNoMatch_ListsSourceArrowMemberFullNameForm()
    {
        Violation use = Violation.MemberUse(
            Node("App.Home", "T:App.Home"),
            Member("System.DateTime", "Now", "P:System.DateTime.Now", MemberKind.Property),
            Array.Empty<SourceLocation>());

        var error = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveEdge("r", [use], "App.Home", "N.Other"));

        error.Message.ShouldContain("the baseline records observed reality");
        error.Message.ShouldContain("  App.Home -> System.DateTime.Now");
    }

    [Fact]
    public void ResolveSubject_MemberByFullNameAndSymbolId_ReturnsMatchingViolation()
    {
        // A --subject matches a member-shape violation by the member's full name (no parens) or member id.
        Violation shape = Violation.MemberShape(
            MemberSubject("MyApp.Web.HomeController", "Save", "M:MyApp.Web.HomeController.Save", MemberKind.Method),
            Array.Empty<SourceLocation>());
        Violation[] violations = [shape];

        Violation byFullName = BaselineAddMatcher.ResolveSubject("r", violations, "MyApp.Web.HomeController.Save");
        Violation bySymbolId = BaselineAddMatcher.ResolveSubject("r", violations, "M:MyApp.Web.HomeController.Save");

        byFullName.ShouldBeSameAs(shape);
        bySymbolId.ShouldBeSameAs(shape);
    }

    [Fact]
    public void ResolveSubject_OverloadedMemberFullName_AmbiguousListsBothMemberIds()
    {
        // A full-name --subject naming an overloaded method matches every overload → the ambiguity lists the
        // distinct member ids to retry with (GRAMMAR §4.6, mirroring the member --target side).
        Violation intOverload = Violation.MemberShape(
            MemberSubject("N.Svc", "M", "M:N.Svc.M(System.Int32)", MemberKind.Method), Array.Empty<SourceLocation>());
        Violation stringOverload = Violation.MemberShape(
            MemberSubject("N.Svc", "M", "M:N.Svc.M(System.String)", MemberKind.Method), Array.Empty<SourceLocation>());
        Violation[] violations = [intOverload, stringOverload];

        var error = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveSubject("r", violations, "N.Svc.M"));

        error.Message.ShouldContain("--subject 'N.Svc.M' matches more than one current violation of 'r'");
        error.Message.ShouldContain("M:N.Svc.M(System.Int32)");
        error.Message.ShouldContain("M:N.Svc.M(System.String)");
        error.Message.ShouldEndWith("Use the symbol ID form.");
    }

    [Fact]
    public void ResolveSubject_MemberNoMatch_ListsMemberFullNameFormWithParens()
    {
        // The no-match candidate list echoes the member as 'Save()' (parens iff method), exactly as
        // 'loadbearing check' renders it, even though matching accepts the no-parens full name.
        Violation shape = Violation.MemberShape(
            MemberSubject("MyApp.Web.HomeController", "Save", "M:MyApp.Web.HomeController.Save", MemberKind.Method),
            Array.Empty<SourceLocation>());

        var error = Should.Throw<UserErrorException>(() => BaselineAddMatcher.ResolveSubject("r", [shape], "MyApp.Web.HomeController.Other"));

        error.Message.ShouldContain("the baseline records observed reality");
        error.Message.ShouldContain("  MyApp.Web.HomeController.Save()");
    }

    private static TypeNode Node(string fullName, string symbolId)
    {
        return new TypeNode(
            fullName, symbolId, fullName, "N", TypeKind.Class,
            Accessibility.Public, false, false, false, false, "Proj", false);
    }

    private static MemberReference Member(string containingFullName, string name, string symbolId, MemberKind kind)
    {
        return new MemberReference(Node(containingFullName, $"T:{containingFullName}"), name, symbolId, kind);
    }

    private static MemberNode MemberSubject(string declaringFullName, string name, string symbolId, MemberKind kind)
    {
        return new MemberNode(
            Node(declaringFullName, $"T:{declaringFullName}"), symbolId, name, kind,
            Accessibility.Public, false, false, false, false, null, null,
            Array.Empty<SourceLocation>(), Array.Empty<string>());
    }
}