namespace Zphil.LoadBearing.Validation;

/// <summary>The spec-build validation catalog (GRAMMAR §8). Errors are collected and reported all at once.</summary>
public enum SpecValidationErrorCode
{
    /// <summary>Duplicate ID over the post-desugar set, across all spec classes (§8 item 1).</summary>
    DuplicateId,

    /// <summary>A declared ID equals or extends a scope ID's reserved namespace (§8 item 1, §7).</summary>
    IdExtendsScope,

    /// <summary>A <c>Rule</c>/<c>Scope</c> anchor with no posture verb (§8 item 2).</summary>
    DanglingAnchor,

    /// <summary>A rule or frozen scope missing its required <c>Because</c> (§8 item 3).</summary>
    MissingBecause,

    /// <summary>A frozen scope missing both <c>Dragons</c> and <c>DragonsDoc</c> (§8 item 4).</summary>
    MissingDragons,

    /// <summary>Blank or whitespace prose anywhere, including escape-hatch descriptions (§8 item 5).</summary>
    BlankProse,

    /// <summary>Multi-line prose in a single-line field (§8 item 5).</summary>
    MultiLineProse,

    /// <summary>A trailer or option supplied more than once (§8 item 6).</summary>
    RepeatedTrailer,

    /// <summary>An ID that does not match <c>^[a-z0-9-]+(/[a-z0-9-]+)*$</c> (§8 item 7).</summary>
    MalformedId,

    /// <summary><c>BoundaryOnlyVia()</c> called with zero types (§8 item 8).</summary>
    EmptyBoundary,

    /// <summary>Two layers declared with the same name (§8 item 9).</summary>
    DuplicateLayerName,

    /// <summary>A selection minted on a different <see cref="Arch" /> instance (§8 item 10).</summary>
    ForeignSelection,

    /// <summary>Blank or whitespace member name on an <c>arch.Member</c> used by a rule (§8 item 11).</summary>
    BlankMemberName,

    /// <summary>
    ///     A member not declared on its anchored type (reflection <c>DeclaredOnly</c> typo guard); when
    ///     the member is declared on a base type the error names that base and the <c>typeof</c> to use
    ///     (§8 item 12).
    /// </summary>
    MemberNotDeclared,

    /// <summary>A <see cref="Member" /> minted on a different <see cref="Arch" /> instance (§8 item 13).</summary>
    ForeignMember
}