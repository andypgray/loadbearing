using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 project modal-constraint vocabulary (GRAMMAR §4.10) as extension methods that turn a
///     <see cref="ProjectSelection" /> into a terminal <see cref="Constraint" />.
/// </summary>
/// <remarks>
///     These are the packaging laws — what a project targets, what it takes from the feed, how its
///     restore is pinned, and whether it ships — asserted over the artifact facts an evaluation reported
///     rather than over anything read out of a project file's XML. Polarity is lexical, exactly like the
///     type-side verbs (GRAMMAR §2), and every verb holds to one honesty rule: a fact nothing evaluated
///     is <see langword="null" />, and unknown always passes. A rule that reds on an unevaluated project
///     would be reporting the load's own gaps as architecture violations.
/// </remarks>
public static class ProjectSelectionConstraints
{
    /// <summary>
    ///     The subject projects may target only these frameworks — "must target only `netstandard2.0`",
    ///     or "must target only `netstandard2.0` or `net8.0`" for several. Monikers are compared ordinally
    ///     against the short forms the model normalizes to (a classic project's <c>v4.8</c> reads
    ///     <c>net48</c>). A project whose frameworks nothing evaluated passes. The <c>(first, more)</c>
    ///     shape makes a zero-framework call uncompilable.
    /// </summary>
    public static Constraint MustOnlyTarget(this ProjectSelection subject, string first, params string[] more)
    {
        NotNull(more, nameof(more));
        IReadOnlyList<string> frameworks = OperandList.OneOrMore(first, more, framework => framework);
        return new MustOnlyTargetConstraint(Subject(subject), frameworks);
    }

    /// <summary>
    ///     The subject projects must declare no <c>PackageReference</c> at all, with one violation per
    ///     package a project does declare. Deliberately zero-arity: the <c>(first, more)</c> shape exists
    ///     for the verbs where an empty operand list is meaningless, and here the empty list <em>is</em>
    ///     the law — so it takes its own verb rather than a list nobody can write. The rendered
    ///     parenthetical is the honesty boundary: the model holds what a project declares, so packages
    ///     arriving transitively through a project reference are not seen and this verb does not claim
    ///     otherwise.
    /// </summary>
    public static Constraint MustReferenceNoPackages(this ProjectSelection subject)
    {
        return new MustReferenceNoPackagesConstraint(Subject(subject));
    }

    /// <summary>
    ///     The subject projects' restore must write a lock file
    ///     (<c>RestorePackagesWithLockFile</c>) — the supply-chain law that keeps a resolved package graph
    ///     from drifting between machines. A project nothing evaluated passes.
    /// </summary>
    public static Constraint MustLockPackages(this ProjectSelection subject)
    {
        return new MustLockPackagesConstraint(Subject(subject));
    }

    /// <summary>
    ///     The subject projects must not produce a package. The SDK defaults <c>IsPackable</c> on, so this
    ///     is the verb that makes an internal project's opt-out checkable rather than assumed. A project
    ///     nothing evaluated passes.
    /// </summary>
    public static Constraint MustNotBePackable(this ProjectSelection subject)
    {
        return new MustNotBePackableConstraint(Subject(subject));
    }

    /// <summary>
    ///     The project constraint-position escape hatch. The predicate is stored, never evaluated at spec
    ///     build; the required <paramref name="description" /> completes "must …". A blank description
    ///     fails spec build (validation §8 item 5).
    /// </summary>
    public static Constraint Must(
        this ProjectSelection subject, Func<IProjectInfo, bool> predicate, string description)
    {
        return new ProjectMustConstraint(Subject(subject), NotNull(predicate, nameof(predicate)), description);
    }

    private static ProjectSelection Subject(ProjectSelection subject)
    {
        return NotNull(subject, nameof(subject));
    }
}
