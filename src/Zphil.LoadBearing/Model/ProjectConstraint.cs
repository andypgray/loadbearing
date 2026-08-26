using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     A reified project-artifact constraint (GRAMMAR §4.10): a <see cref="ProjectSelection" /> subject
///     plus a packaging modal verb phrase. Its inherited <see cref="Constraint.Subject" /> is
///     <see langword="null" />, because a project selection is not a type selection and there is no
///     underlying one to stand in — unlike <see cref="MemberConstraint" />, which has
///     <c>MemberSubject.Source</c> to hand up.
/// </summary>
/// <remarks>
///     That null is the whole nullability contract on <see cref="Constraint.Subject" />: it is null
///     exactly here, so every type-side walk either dispatches on the constraint type first (the sentence
///     renderer, the checker) or is already downstream of such a dispatch. The compiler's nullable-flow
///     analysis is what audits that claim — a reader that forgets the dispatch is a warning, not a
///     <see cref="NullReferenceException" /> in the field.
/// </remarks>
internal abstract class ProjectConstraint : Constraint
{
    private protected ProjectConstraint(ProjectSelection subject)
        : base(null)
    {
        ProjectSubject = subject;
    }

    /// <summary>The project selection the constraint is asserted over.</summary>
    internal ProjectSelection ProjectSubject { get; }
}
