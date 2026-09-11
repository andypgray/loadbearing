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

    /// <summary>A rule or scope missing its required <c>Because</c> (§8 item 3).</summary>
    MissingBecause,

    /// <summary>A scope of either posture missing both <c>Dragons</c> and <c>DragonsDoc</c> (§8 item 4).</summary>
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
    ///     A rule given more than one posture verb, or a scope given one more than once (§8
    ///     item 17). The stage machine (§3.2) makes the fluent double-call uncompilable, but a stored
    ///     <c>IRuleBuilder</c>/<c>IScopeBuilder</c> reference is mutable, and a second posture call
    ///     silently overwrites the first — this catches that stored-reference re-call.
    /// </summary>
    RepeatedPosture,

    /// <summary>
    ///     A member-anchor expression lambda — <c>arch.Member&lt;T&gt;(x =&gt; x.M)</c> or
    ///     <c>arch.Member(() =&gt; Type.M)</c> — that <see cref="Internal.MemberExpressionResolver" /> could
    ///     not reduce to a declared <c>(type, name)</c> (GRAMMAR §8, the member-anchor expression class:
    ///     one code, per-shape poison messages). Reported by
    ///     <see cref="SpecValidator" /> before item 12 (member-not-declared), which an expression anchor —
    ///     resolved from a real member and generic-normalized at mint — can never reach.
    /// </summary>
    MemberExpressionUnresolvable,

    /// <summary>
    ///     An <c>arch.Registered</c> noun used by a rule carries a <see cref="Lifetime" /> value outside the
    ///     defined set — a cast such as <c>(Lifetime)7</c> names no lifetime (§8 item 19). Reported in the
    ///     same all-at-once pass; membership resolution never sees it because the build throws first.
    /// </summary>
    UndefinedLifetime,

    /// <summary>
    ///     A closed-generic <c>MustAcceptParameter</c> anchor on a method selection (§8 item 20); parameter-type
    ///     matching is definition-level, so the error guides to the open definition
    ///     (<c>typeof(IProgress&lt;&gt;)</c>).
    /// </summary>
    MemberAcceptParameterClosedGeneric,

    /// <summary>
    ///     A category-invalid hierarchy anchor, both polarities (§8 item 21): a <c>Must[Not]Implement</c>
    ///     anchor must be an interface; a <c>Must[Not]DeriveFrom</c> anchor must not be an interface; a
    ///     <c>Must[Not]BeAttributedWith</c> anchor must derive from <see cref="System.Attribute" />
    ///     (<c>typeof(Attribute)</c> itself is refused — the declared-attribute matcher could never match it).
    ///     A wrong-category anchor never matches, making a positive an always-red rule and a negative an
    ///     always-pass, so the error names the anchor's FQN and steers to the right-category verb. One code
    ///     covers all three categories, fired over the positives' single anchor and every anchor in a
    ///     negative's list.
    /// </summary>
    HierarchyAnchorWrongCategory,

    /// <summary>
    ///     A project selection minted on a different <see cref="Arch" /> instance (§8 item 22) — the
    ///     project-stratum sibling of <see cref="ForeignSelection" /> and <see cref="ForeignMember" />. Its
    ///     own code rather than a widening of <see cref="ForeignSelection" /> because the message has to
    ///     name what was foreign for the author to find it.
    /// </summary>
    ForeignProjectSelection,

    /// <summary>
    ///     A blank or whitespace project name or name glob (§8 item 23): a <c>.Named</c> operand or a
    ///     <c>.Matching</c> glob left empty. A blank name matches no project and a blank glob matches every
    ///     one, so either is almost certainly an authoring slip — and the two failure shapes are far apart
    ///     enough that neither should be discovered at check time.
    /// </summary>
    BlankProjectPattern,

    /// <summary>
    ///     A blank or whitespace target framework operand on <c>MustOnlyTarget</c> (§8 item 24). A blank
    ///     moniker matches nothing, so it silently narrows the allow-list rather than widening it — the
    ///     rule stays green until a project targets the framework the author meant to permit.
    /// </summary>
    BlankTargetFramework,

    /// <summary>
    ///     A <c>MustHaveExactlyOneCounterpart</c> name template carrying no <c>{Name}</c> placeholder (§8
    ///     item 26). Substitution is ordinal, so every subject then derives the same fixed name and the
    ///     rule states a cardinality claim rather than a correspondence — and the <c>{name}</c> typo that
    ///     causes it would otherwise be discovered only as a whole subject set going red at check time.
    /// </summary>
    CounterpartTemplateWithoutPlaceholder,

    /// <summary>
    ///     A family (<c>arch.Each</c>) standing anywhere but as a rule subject (§8 item 27) — an operand,
    ///     an <c>Except</c> payload, a union operand, a layer definition, a scoped selection or a boundary.
    ///     A partition means nothing in a position that consumes a set: every one of those reads the
    ///     family's membership and nothing would read its cells, so the rule would silently be the union
    ///     of them. The message names the position, because that is what the author has to move.
    /// </summary>
    FamilyMisplaced,

    /// <summary>
    ///     <c>MustNotReferenceEachOther</c> over a subject that is not a family (§8 item 28). The verb's
    ///     targets are the subject's own cells, so a cell-free subject names nothing to forbid — and the
    ///     verb the author meant over a plain selection has a name, which the message gives.
    /// </summary>
    EachOtherWithoutFamily,

    /// <summary>
    ///     <c>MustNotHaveCircularReferences</c> over a subject that is not a family of layers (§8 item 29).
    ///     Two wordings share this code, because the two shapes are wrong for different reasons: a plain
    ///     selection has no layers to reference each other at all, and a family of projects is refused
    ///     because projects cannot have circular references — the build forbids them — so over one the law
    ///     would hold by construction, and a rule that cannot red is a false promise.
    /// </summary>
    CircularReferencesNeedLayerFamily
}
