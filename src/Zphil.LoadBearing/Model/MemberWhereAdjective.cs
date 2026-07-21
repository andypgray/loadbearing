namespace Zphil.LoadBearing.Model;

/// <summary>
///     The member selector-position escape hatch (<c>.Where(pred, description:)</c> on a member
///     selection, GRAMMAR §5.6, §5.7). The predicate is stored but never evaluated at spec build; the
///     description renders verbatim as a sentence-final relative clause on the member subject, exactly
///     like the type-side <see cref="WhereAdjective" />.
/// </summary>
internal sealed class MemberWhereAdjective(Func<IMemberInfo, bool> predicate, string description) : MemberAdjective
{
    /// <summary>The stored predicate (evaluated at check time).</summary>
    internal Func<IMemberInfo, bool> Predicate { get; } = predicate;

    /// <summary>The mandatory description that renders in place of the opaque lambda.</summary>
    internal string Description { get; } = description;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override string Fragment => " " + Description;
}