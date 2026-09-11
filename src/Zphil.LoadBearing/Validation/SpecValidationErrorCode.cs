namespace Zphil.LoadBearing.Validation;

/// <summary>
///     Which mistake in a spec was found. Every way a spec can be wrong has a code here, and
///     <see cref="SpecValidationException" /> carries one <see cref="SpecValidationError" /> per mistake:
///     the whole set is reported together when the spec is loaded, so a spec can be fixed in one pass
///     rather than one error at a time.
/// </summary>
public enum SpecValidationErrorCode
{
    /// <summary>
    ///     Two rules or scopes were declared with the same ID. IDs must be unique across every spec class in
    ///     the build, counting the rules each scope contributes of its own, <c>{scope-id}/containment</c> and
    ///     <c>{scope-id}/tripwire</c>. Rename one of them.
    /// </summary>
    DuplicateId,

    /// <summary>
    ///     A rule ID sits beneath a scope's ID: a scope reserves <c>{scope-id}/</c> for the rules it
    ///     contributes, <c>containment</c> and <c>tripwire</c>. Give the rule an ID outside that prefix.
    /// </summary>
    IdExtendsScope,

    /// <summary>
    ///     An <c>arch.Rule(id)</c> that never reached <c>Enforce</c> or <c>Migrate</c>, or an
    ///     <c>arch.Scope(id)</c> that never reached <c>Quarantine</c> or <c>Caution</c>. Every rule and scope
    ///     takes exactly one posture; the message names the two the declaration could have taken.
    /// </summary>
    MissingPosture,

    /// <summary>
    ///     A rule or scope with no <c>Because</c>. Every rule and every scope, whichever posture it carries,
    ///     must say in one line of prose why it exists.
    /// </summary>
    MissingBecause,

    /// <summary>
    ///     A scope with neither <c>Dragons</c> nor <c>DragonsDoc</c>. Say in one line what is strange inside
    ///     the scope, link a longer document, or do both; the message names the scope's posture.
    /// </summary>
    MissingDragons,

    /// <summary>
    ///     A prose value that is empty or only whitespace: a <c>Because</c>, a <c>Fix</c>, a
    ///     <c>Citation</c>, a migration's <c>from</c> description, a scope's <c>Dragons</c> or
    ///     <c>DragonsDoc</c>, a layer's <c>Purpose</c>, or the description a <c>Where</c> or a custom
    ///     <c>Must</c> requires. Write the line or drop the call.
    /// </summary>
    BlankProse,

    /// <summary>
    ///     A prose value carrying a line break. Every prose value is a single line; link a longer document
    ///     with a scope's <c>DragonsDoc</c> instead.
    /// </summary>
    MultiLineProse,

    /// <summary>
    ///     The same call made twice where it is allowed at most once: two <c>Because</c>s, two
    ///     <c>Baseline</c>s, two <c>Citation</c>s on one rule, a layer's <c>Purpose</c> twice. Keep one.
    /// </summary>
    RepeatedCall,

    /// <summary>
    ///     A rule or scope ID that is not lowercase letters, digits and hyphens in segments joined by
    ///     <c>/</c> — the message quotes the pattern, <c>^[a-z0-9-]+(/[a-z0-9-]+)*$</c>. The convention is
    ///     <c>area/rule-name</c>.
    /// </summary>
    MalformedId,

    /// <summary>
    ///     <c>BoundaryOnlyVia()</c> called with no types. Name the types that may keep referencing into the
    ///     quarantine, or omit the call entirely for a hermetic quarantine that nothing outside may reference.
    /// </summary>
    EmptyBoundary,

    /// <summary>Two layers declared with the same name. Layer names are unique within the spec.</summary>
    DuplicateLayerName,

    /// <summary>
    ///     A rule or scope used a selection built on a different <see cref="Arch" />. Every selection a spec
    ///     uses must come from the <see cref="Arch" /> its <c>Define</c> was handed. A layer's selection
    ///     definition is checked the same way and reported against the layer, whether or not any rule names
    ///     that layer.
    /// </summary>
    ForeignSelection,

    /// <summary>
    ///     An <c>arch.Member(typeof(T), name)</c> used by a rule was given an empty member name. Pass the
    ///     name the type declares, ideally as <c>nameof(...)</c>.
    /// </summary>
    BlankMemberName,

    /// <summary>
    ///     The type named in an <c>arch.Member(typeof(T), name)</c> does not declare that member. It must be
    ///     declared on that type itself, not inherited: when the member comes from a base type the message
    ///     names the base and the <c>typeof</c> to write instead.
    /// </summary>
    MemberNotDeclared,

    /// <summary>
    ///     A rule used a member built on a different <see cref="Arch" /> — an <c>arch.Member(...)</c> from
    ///     another model. Build it on the <see cref="Arch" /> the rule is declared on.
    /// </summary>
    ForeignMember,

    /// <summary>
    ///     A <c>Returning</c> given a closed generic such as <c>typeof(Task&lt;int&gt;)</c>. Return types are
    ///     matched by definition, so name the open definition, <c>typeof(Task&lt;&gt;)</c>, which matches
    ///     every construction of it. A non-generic type is accepted as written.
    /// </summary>
    MemberReturningClosedGeneric,

    /// <summary>
    ///     A glob, name or affix left empty: a namespace glob, a type-name glob, an exact name on
    ///     <c>Named</c>, a project name on <c>arch.Project</c> or <c>MustResideInProject</c>, a suffix or
    ///     prefix, a <c>MustHaveExactlyOneCounterpart</c> name template, or a string-named attribute,
    ///     interface, base type, return type or parameter type. A blank affix is true of every name and a
    ///     blank glob fails at check time, so neither is likely to be what was meant. Type and member sides
    ///     alike, layer globs included.
    /// </summary>
    BlankPattern,

    /// <summary>
    ///     A namespace glob ending in <c>.*</c> whose text before that ending also contains a <c>*</c>, such
    ///     as <c>MyApp.*.Controllers.*</c>. Everything before a trailing <c>.*</c> is matched literally, so
    ///     such a glob can never match: anchor the subtree on a literal prefix
    ///     (<c>MyApp.Web.Controllers.*</c>), or drop the trailing <c>.*</c> and let the interior <c>*</c>
    ///     match one segment. Type-name globs and affixes have no subtree form, so this never reports them.
    /// </summary>
    UnanchoredSubtreePattern,

    /// <summary>
    ///     A rule given both <c>Enforce</c> and <c>Migrate</c>, or a scope given a posture twice. A chained
    ///     spec cannot write this (neither posture verb is on the step that follows either), but the value
    ///     <c>arch.Rule(id)</c> returns can be held in a variable and called again, and the second call
    ///     silently replaces the first. Keep one posture per rule and one per scope.
    /// </summary>
    RepeatedPosture,

    /// <summary>
    ///     An <c>arch.Member&lt;T&gt;(x =&gt; ...)</c> or <c>arch.Member(() =&gt; ...)</c> lambda that does
    ///     not name one declared member. Each shape has its own message naming the form to write instead: a
    ///     body that is not a member access, a method group without its parentheses (write
    ///     <c>x =&gt; x.M()</c>), a member not reached directly on the lambda's parameter (a chained access
    ///     <c>x =&gt; x.A.B</c>, a captured local or field), a static member reached through the instance
    ///     form or an instance member through the parameterless one, an indexer, a compile-time constant, and
    ///     an object creation (use <c>MustNotConstruct</c> instead).
    /// </summary>
    MemberExpressionUnresolvable,

    /// <summary>
    ///     An <c>arch.Registered</c> given a lifetime outside the defined set, such as a cast
    ///     <c>(Lifetime)7</c>. The message names the value and the three that are defined:
    ///     <c>Lifetime.Singleton</c>, <c>Lifetime.Scoped</c> and <c>Lifetime.Transient</c>.
    /// </summary>
    UndefinedLifetime,

    /// <summary>
    ///     A <c>MustAcceptParameter</c> given a closed generic such as <c>typeof(IProgress&lt;int&gt;)</c>.
    ///     Parameter types are matched by definition, so name the open definition,
    ///     <c>typeof(IProgress&lt;&gt;)</c>. A non-generic type is accepted as written.
    /// </summary>
    MemberAcceptParameterClosedGeneric,

    /// <summary>
    ///     A hierarchy verb given a type of the wrong category: <c>MustImplement</c> and
    ///     <c>MustNotImplement</c> take an interface, <c>MustDeriveFrom</c> and <c>MustNotDeriveFrom</c> take
    ///     something that is not an interface, and <c>MustBeAttributedWith</c> and
    ///     <c>MustNotBeAttributedWith</c> take a type deriving from <see cref="System.Attribute" /> (bare
    ///     <c>typeof(Attribute)</c> is refused, as no declared attribute could match it). Such a type never
    ///     matches, which makes the positive verb always fail and the negative always pass, so the message
    ///     names the type and the verb to use instead. Reported for <c>typeof</c> only: a name given as a
    ///     string carries no type to categorize.
    /// </summary>
    HierarchyAnchorWrongCategory,

    /// <summary>
    ///     A rule or scope used a project selection built on a different <see cref="Arch" />, including one
    ///     nested inside an <c>Except</c>. Build it on the <see cref="Arch" /> the rule is declared on.
    /// </summary>
    ForeignProjectSelection,

    /// <summary>
    ///     An empty project name on <c>arch.Projects.Named</c>, or an empty glob on
    ///     <c>arch.Projects.Matching</c>. A blank name matches no project and a blank glob matches every one,
    ///     so neither is likely to be what was meant.
    /// </summary>
    BlankProjectPattern,

    /// <summary>
    ///     An empty target framework on <c>MustOnlyTarget</c>. A blank moniker matches nothing, so it narrows
    ///     the permitted list instead of widening it and the rule stays green until some project targets the
    ///     framework that was meant to be allowed.
    /// </summary>
    BlankTargetFramework,

    /// <summary>
    ///     A <c>MustHaveExactlyOneCounterpart</c> name template with no <c>{Name}</c> placeholder, such as
    ///     <c>IService</c>. Every subject would derive the same fixed name, making the rule a claim about how
    ///     many types carry that one name rather than about correspondence. Write a template such as
    ///     <c>I{Name}</c>; substitution is case-sensitive, so <c>{name}</c> is the usual cause.
    /// </summary>
    CounterpartTemplateWithoutPlaceholder,

    /// <summary>
    ///     An <c>arch.Each</c> family used anywhere but as a rule subject — as a verb operand, an
    ///     <c>Except</c> payload, a union operand, a layer definition, a scoped selection or a boundary. Each
    ///     of those positions takes a plain set and would read the family as everything in it, losing the
    ///     division into cells that is the point of a family; the message names the position to move it out
    ///     of. A family reached through a member projection such as <c>.Methods</c> is still a subject and is
    ///     accepted.
    /// </summary>
    FamilyMisplaced,

    /// <summary>
    ///     <c>MustNotReferenceEachOther</c> over a subject that is not an <c>arch.Each</c> family. The verb
    ///     forbids the family's own cells to reference one another, so a subject with no cells names nothing
    ///     to forbid. Over a plain selection write <c>MustNotReference</c> with the target named.
    /// </summary>
    EachOtherWithoutFamily,

    /// <summary>
    ///     <c>MustNotHaveCircularReferences</c> over a subject that is not an <c>arch.Each</c> family of
    ///     layers, in two wordings. Over a plain selection there are no layers to reference one another at
    ///     all; over a family of projects the build itself already refuses circular project references, so
    ///     the rule could never fail and would be a promise the check never keeps. Write
    ///     <c>MustNotReferenceEachOther</c>, or an ordering rule that names the direction.
    /// </summary>
    CircularReferencesNeedLayerFamily,

    /// <summary>
    ///     A <c>Citation</c> that is not an absolute <c>http</c> or <c>https</c> URL — a pasted page title, a
    ///     repository-relative path, or a URI on another scheme. Every surface renders the value as a link
    ///     and hands it on unread (the generated agent context autolinks it, SARIF output publishes it as the
    ///     rule's help URI), so nothing downstream could report a bad one.
    /// </summary>
    MalformedCitation
}
