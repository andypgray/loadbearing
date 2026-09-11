using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     A set of projects the spec talks about, as build artifacts rather than as the types they
///     contain: the subject of a project rule. Start one from <c>arch.Projects</c>; narrow it with
///     <c>Named</c>, <c>Matching</c>, <c>Packable</c>, <c>Except</c> or <c>Where</c>, each of which
///     returns a new selection; finish it with a project verb — <c>MustOnlyTarget</c>,
///     <c>MustReferenceNoPackages</c>, <c>MustLockPackages</c>, <c>MustNotBePackable</c> or
///     <c>Must</c> — which yields the <see cref="Constraint" /> a rule takes. The facts these verbs
///     read are what a build evaluation answers, so a setting a shared props file supplies counts and a
///     project nothing evaluated passes rather than fails. For the types a project declares, use
///     <c>arch.Project(name)</c> instead. Immutable and reusable: assign one to a variable and use it
///     in as many rules as you like, on the <see cref="Arch" /> it was built from.
/// </summary>
// A closed hierarchy: the constructor is private protected, so no foreign assembly can add a node,
// and it is disjoint from both Selection and MemberSelection so the shared adjective and verb names
// bind by receiver type (GRAMMAR §3.2). Unlike a member selection there is no underlying type
// selection to borrow an owner from — a project here is the build artifact, not a set of types — so
// Owner is carried directly, and that is what the foreign-Arch walk reads (§8 item 10).
public abstract class ProjectSelection
{
    private protected ProjectSelection(Arch owner, IReadOnlyList<ProjectAdjective> adjectives)
    {
        Owner = owner;
        Adjectives = adjectives;
    }

    /// <summary>The <see cref="Arch" /> this selection was minted on (GRAMMAR §3.2 fresh-instance contract).</summary>
    internal Arch Owner { get; }

    /// <summary>The ordered project-adjective refinements applied to the all-projects head.</summary>
    internal IReadOnlyList<ProjectAdjective> Adjectives { get; }

    /// <summary>
    ///     The clone each concrete project selection returns carrying a new adjective list — the hook the
    ///     adjective extensions build on, so a refinement never mutates the selection it refines.
    /// </summary>
    private protected abstract ProjectSelection Rebuild(IReadOnlyList<ProjectAdjective> adjectives);

    /// <summary>Appends one adjective and clones (the assembly-internal entry the adjective extensions call).</summary>
    internal ProjectSelection Refined(ProjectAdjective adjective)
    {
        var adjectives = new List<ProjectAdjective>(Adjectives) { adjective };
        return Rebuild(adjectives);
    }
}
