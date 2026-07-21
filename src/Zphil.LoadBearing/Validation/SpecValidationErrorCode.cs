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
    ForeignMember,

    /// <summary>
    ///     A closed-generic <c>.Returning</c> anchor on a member selection (§8 item 14); member
    ///     return-type matching is definition-level, so the error guides to the open definition
    ///     (<c>typeof(Task&lt;&gt;)</c>).
    /// </summary>
    MemberReturningClosedGeneric,

    /// <summary>
    ///     A blank or whitespace glob or affix (§8 item 15): a namespace/name pattern, or a
    ///     suffix/prefix, left empty. A blank affix is vacuously true and a blank glob throws at check
    ///     time — either way it is almost certainly an authoring slip. Covers the type and member sides.
    /// </summary>
    BlankPattern,

    /// <summary>
    ///     A dead namespace subtree pattern (§8 item 16): a trailing <c>.*</c> whose literal prefix
    ///     carries a <c>*</c> (e.g. <c>MyApp.*.Controllers.*</c>), which the subtree operator compares
    ///     literally and so never matches. The error steers the author to anchor the subtree on a
    ///     literal prefix. Type-name globs and affixes carry no subtree operator, so this never applies
    ///     to them (GRAMMAR §4.2).
    /// </summary>
    UnanchoredSubtreePattern,

    /// <summary>
    ///     A rule given more than one posture verb, or a scope given <c>Freeze</c> more than once (§8
    ///     item 17). The stage machine (§3.2) makes the fluent double-call uncompilable, but a stored
    ///     <c>IRuleBuilder</c>/<c>IScopeBuilder</c> reference is mutable, and a second posture call
    ///     silently overwrites the first — this catches that stored-reference re-call.
    /// </summary>
    RepeatedPosture,

    /// <summary>
    ///     A member-anchor expression lambda — <c>arch.Member&lt;T&gt;(x =&gt; x.M)</c> or
    ///     <c>arch.Member(() =&gt; Type.M)</c> — that <see cref="Internal.MemberExpressionResolver" /> could
    ///     not reduce to a declared <c>(type, name)</c> (GRAMMAR §8, the member-anchor expression class:
    ///     one code, eight poison messages — the <see cref="BlankPattern" /> precedent). Reported by
    ///     <see cref="SpecValidator" /> before item 12 (member-not-declared), which an expression anchor —
    ///     resolved from a real member and generic-normalized at mint — can never reach.
    /// </summary>
    MemberExpressionUnresolvable,

    /// <summary>
    ///     An <c>arch.Registered</c> noun used by a rule carries a <see cref="Lifetime" /> value outside the
    ///     defined set — a cast such as <c>(Lifetime)7</c> names no lifetime (§8 item 19). Reported in the
    ///     same all-at-once pass; membership resolution never sees it because the build throws first.
    /// </summary>
    UndefinedLifetime
}