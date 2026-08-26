using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     A reified member-shape constraint (GRAMMAR §4.6): a <see cref="MemberSelection" /> subject plus
///     a member modal verb phrase. Carries the <see cref="MemberSubject" /> for member-subject
///     assembly (GRAMMAR §6) and the checker, but its inherited <see cref="Constraint.Subject" /> is
///     the underlying <b>type</b> selection (<c>Subject =&gt; MemberSubject.Source</c>), so every walk
///     that reaches through the base subject — foreign-<see cref="Arch" /> detection (§8 item 10),
///     Quarantine desugaring (§7) — keeps working unchanged on the type side. That is the point of
///     departure from <see cref="ProjectConstraint" />, which has no such selection to hand up and passes
///     <see langword="null" />.
/// </summary>
internal abstract class MemberConstraint : Constraint
{
    private protected MemberConstraint(MemberSelection subject)
        : base(subject.Source)
    {
        MemberSubject = subject;
    }

    /// <summary>The member selection the constraint is asserted over.</summary>
    internal MemberSelection MemberSubject { get; }
}
