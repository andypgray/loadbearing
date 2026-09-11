namespace Zphil.LoadBearing.Checking;

/// <summary>
///     What a <see cref="Violation" /> is about, and so which of its slots carry a value. Each member
///     below names the ones its kind fills; every other slot is null, and <c>Sites</c> is where the
///     evidence is.
/// </summary>
public enum ViolationKind
{
    /// <summary>
    ///     A reference the rule forbids, or one it does not permit: <c>Source</c> references <c>Target</c>,
    ///     with every place the reference is written in <c>Sites</c>. One violation per rule, source and
    ///     target, however many sites they share.
    /// </summary>
    Reference,

    /// <summary>
    ///     A type failing a verb about the type itself: its shape, its name, what it inherits or is
    ///     attributed with, where it lives, or a <c>Must</c> predicate of the spec's own. <c>Subject</c>
    ///     is the type and <c>Sites</c> its declarations. One violation per rule and subject.
    /// </summary>
    Shape,

    /// <summary>
    ///     A use of a banned member: <c>Source</c> uses <c>Member</c>, with every place it is used in
    ///     <c>Sites</c>. One violation per rule, source and member; a ban covers every overload of the name,
    ///     and each overload actually used reports separately.
    /// </summary>
    MemberUse,

    /// <summary>
    ///     A declared member failing a verb about members: <c>SubjectMember</c> is the member and
    ///     <c>Sites</c> its declaration. One violation per rule and member, keyed by the member itself — so
    ///     renaming a grandfathered member, or adding a new one that breaks the rule, is a new violation
    ///     rather than an already-blessed one.
    /// </summary>
    MemberShape,

    /// <summary>
    ///     A <c>new</c> the rule forbids: <c>Source</c> constructs <c>Target</c>, with every object-creation
    ///     expression in <c>Sites</c>. One violation per rule, source and constructed type; which constructor
    ///     overload was used makes no difference.
    /// </summary>
    Construction,

    /// <summary>
    ///     A constructor parameter the rule forbids: <c>Source</c> injects <c>Target</c> through a declared
    ///     constructor parameter, with every such parameter in <c>Sites</c>. One violation per rule, source
    ///     and injected type; neither the constructor overload nor the parameter's name makes a difference.
    /// </summary>
    Injection,

    /// <summary>
    ///     A <c>catch</c> the rule forbids: <c>Source</c> catches <c>Target</c>, with every clause in
    ///     <c>Sites</c>. A bare <c>catch</c> counts as catching <c>System.Exception</c>. One violation per
    ///     rule, source and caught type, however many clauses.
    /// </summary>
    Catch,

    /// <summary>
    ///     A <c>throw</c> the rule forbids, or one it does not permit: <c>Source</c> throws <c>Target</c>,
    ///     with every <c>throw</c> statement or expression in <c>Sites</c>. One violation per rule, source
    ///     and thrown type.
    /// </summary>
    Throw,

    /// <summary>
    ///     A public signature the rule forbids: <c>Source</c> exposes <c>Target</c> in a public signature
    ///     position, with every such declaration in <c>Sites</c>. One violation per rule, source and exposed
    ///     type.
    /// </summary>
    Expose,

    /// <summary>
    ///     A project failing a verb about projects: <c>SubjectProject</c> is the project, and <c>Sites</c>
    ///     points at where the offending fact is declared — which may be a shared props file above the
    ///     project, and may be empty where the fact has nowhere to point. One violation per rule and project,
    ///     except <c>MustReferenceNoPackages</c>, which reports one per declared package reference with
    ///     <c>Package</c> naming it and the package's own declaration as the site.
    /// </summary>
    ProjectShape,

    /// <summary>
    ///     The rule's subject matched nothing at all — no types, or on a rule about members or projects
    ///     none of those — which fails the rule rather than passing it vacuously. <c>Detail</c> names what
    ///     was empty, <c>Hint</c> says what to change, and <c>Sites</c> is empty.
    /// </summary>
    EmptySubject,

    /// <summary>
    ///     The rule could not be evaluated: a type that cannot be represented, a closed generic where a
    ///     definition was required, or a predicate that threw. <c>Detail</c> carries the explanation,
    ///     and <c>Sites</c> is empty.
    /// </summary>
    RuleError
}
