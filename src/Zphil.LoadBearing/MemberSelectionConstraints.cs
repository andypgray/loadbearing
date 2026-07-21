using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 member modal-constraint vocabulary (GRAMMAR §5.7) as extension methods that turn a
///     <see cref="MemberSelection" /> into a terminal <see cref="Constraint" />. Polarity is lexical,
///     exactly like the type-side verbs (GRAMMAR §2). The naming verbs reuse the type-side "must be
///     named" / "must have a name matching" fragments; <see cref="MustBePrivate" /> and
///     <see cref="MustBeVirtual" /> are new member-only vocabulary. These bind by receiver type (a
///     <see cref="MemberSelection" /> is not a <see cref="Selection" />), so the identically-named
///     type-side verbs never collide on overload resolution.
/// </summary>
public static class MemberSelectionConstraints
{
    /// <summary>The subject members' names must end with a suffix.</summary>
    public static Constraint MustHaveSuffix(this MemberSelection subject, string suffix)
    {
        return new MemberMustHaveSuffixConstraint(Subject(subject), NotNull(suffix, nameof(suffix)));
    }

    /// <summary>The subject members' names must start with a prefix.</summary>
    public static Constraint MustHavePrefix(this MemberSelection subject, string prefix)
    {
        return new MemberMustHavePrefixConstraint(Subject(subject), NotNull(prefix, nameof(prefix)));
    }

    /// <summary>The subject members' names must match a glob.</summary>
    public static Constraint MustHaveNameMatching(this MemberSelection subject, string glob)
    {
        return new MemberMustHaveNameMatchingConstraint(Subject(subject), NotNull(glob, nameof(glob)));
    }

    /// <summary>The subject members must be public.</summary>
    public static Constraint MustBePublic(this MemberSelection subject)
    {
        return new MemberMustBePublicConstraint(Subject(subject));
    }

    /// <summary>The subject members must be internal.</summary>
    public static Constraint MustBeInternal(this MemberSelection subject)
    {
        return new MemberMustBeInternalConstraint(Subject(subject));
    }

    /// <summary>The subject members must be private (member-only vocabulary).</summary>
    public static Constraint MustBePrivate(this MemberSelection subject)
    {
        return new MemberMustBePrivateConstraint(Subject(subject));
    }

    /// <summary>The subject members must be static.</summary>
    public static Constraint MustBeStatic(this MemberSelection subject)
    {
        return new MemberMustBeStaticConstraint(Subject(subject));
    }

    /// <summary>The subject members must be abstract.</summary>
    public static Constraint MustBeAbstract(this MemberSelection subject)
    {
        return new MemberMustBeAbstractConstraint(Subject(subject));
    }

    /// <summary>The subject members must be virtual (member-only vocabulary).</summary>
    public static Constraint MustBeVirtual(this MemberSelection subject)
    {
        return new MemberMustBeVirtualConstraint(Subject(subject));
    }

    /// <summary>
    ///     The member constraint-position escape hatch. The predicate is stored, never evaluated at
    ///     spec build; the required <paramref name="description" /> completes "must …". A blank
    ///     description fails spec build (validation §8 item 5).
    /// </summary>
    public static Constraint Must(this MemberSelection subject, Func<IMemberInfo, bool> predicate, string description)
    {
        return new MemberMustConstraint(Subject(subject), NotNull(predicate, nameof(predicate)), description);
    }

    private static MemberSelection Subject(MemberSelection subject)
    {
        return Guard.NotNull(subject, nameof(subject));
    }

    private static T NotNull<T>(T value, string paramName)
        where T : class
    {
        return Guard.NotNull(value, paramName);
    }
}