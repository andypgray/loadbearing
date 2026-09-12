using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     A resolved selection's membership, together with the attribution an <em>edge</em> position needs
///     where one source file compiles into several projects (GRAMMAR §4.1). Membership itself is N-way —
///     such a file is one node, and a project selection over any of its declarers names it — so
///     <see cref="Contains" /> is the plain set test every non-edge position takes.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CountsEdge" /> is the edge test, and it parts from <see cref="Contains" /> only
///         where an endpoint is a node several projects declare. It runs over
///         <see cref="EdgeInstances" />: the subject selection bounds which instances the rule owns at
///         whichever end it sits, and the operand selection is the predicate at the far end, asked at
///         that instance's project for that end. Which project a selection names a node <em>at</em> is
///         <see cref="NamingOf" />, and heads that are not projects (a <c>typeof</c>, a namespace, a
///         layer, <c>Registered</c>) name it at every project — so
///         <c>arch.Project("P").MustNotReference(typeof(Shared))</c> reds on P's own compiled-in copy.
///     </para>
///     <para>
///         The provenance map holds an entry only for a member several projects declare, and is null
///         outright wherever the model conflates nothing
///         (<see cref="SelectionEvaluator.AnyMultiplyDeclared" />). So on an ordinary codebase every path
///         through this type reduces to <see cref="Contains" /> by construction, and the verdicts are the
///         ones single attribution gave.
///     </para>
/// </remarks>
internal sealed class SelectionAdmission
{
    private readonly Dictionary<TypeNode, Provenance>? _conflated;

    private SelectionAdmission(HashSet<TypeNode> members, Dictionary<TypeNode, Provenance>? conflated)
    {
        Members = members;
        _conflated = conflated;
    }

    /// <summary>
    ///     The nodes the selection matched, by reference identity — the set the shape verbs range over
    ///     and the subject coverage is measured from.
    /// </summary>
    internal HashSet<TypeNode> Members { get; }

    /// <summary>How many nodes matched — the arity the empty-subject and inert-target gates read.</summary>
    internal int Count => Members.Count;

    /// <summary>
    ///     Plain N-way membership: whether the selection names this node at all, attribution aside. The
    ///     test every position that is not an edge end takes, and what <see cref="CountsEdge" /> collapses
    ///     to on an edge neither of whose endpoints more than one project declares.
    /// </summary>
    internal bool Contains(TypeNode node)
    {
        return Members.Contains(node);
    }

    /// <summary>
    ///     Whether one edge counts against a rule: whether any instance the <paramref name="subject" />
    ///     owns puts the <paramref name="operand" /> at the far end, or (for <c>MustOnly*</c>, which asks
    ///     with <paramref name="wantHit" /> false) fails to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The whole §4.1 edge rule, stated once for every verb at either end. The subject bounds
    ///         ownership at the end it sits — <paramref name="subjectAtSource" /> says which — and the
    ///         operand is the predicate at the other, each asked at that instance's project for that end
    ///         (<see cref="NamingOf" />). A <c>MustNot*</c> verb violates on the first owned instance
    ///         whose predicate holds; a <c>MustOnly*</c> verb on the first whose predicate fails.
    ///     </para>
    ///     <para>
    ///         Returns on the first satisfying instance rather than counting them. Violation identity is
    ///         the (source, target) type pair (§4.3), which is also what a baseline entry keys on, so an
    ///         edge that satisfied a rule twice must still mint one violation.
    ///     </para>
    /// </remarks>
    internal static bool CountsEdge(
        SelectionAdmission subject, SelectionAdmission operand,
        TypeNode source, TypeNode target, bool subjectAtSource, bool wantHit)
    {
        // Nothing conflated at either end, nothing to attribute: one instance, and it is the edge itself.
        // Edge-local rather than solution-wide, so a codebase with one linked file still takes it for
        // every other edge — and it keeps an external endpoint away from the declarer walk below, whose
        // ProjectName there is a supplying assembly's name rather than a project's.
        if (source.AlsoDeclaredBy.Count == 0 && target.AlsoDeclaredBy.Count == 0)
        {
            TypeNode farEnd = subjectAtSource ? target : source;
            return operand.Contains(farEnd) == wantHit;
        }

        SelectionAdmission atSource = subjectAtSource ? subject : operand;
        SelectionAdmission atTarget = subjectAtSource ? operand : subject;
        Naming sourceNaming = atSource.NamingOf(source);
        Naming targetNaming = atTarget.NamingOf(target);

        foreach (EdgeInstance instance in EdgeInstances.Of(source, target))
        {
            bool namesSource = sourceNaming.Names(instance.SourceProject);
            bool namesTarget = targetNaming.Names(instance.TargetProject);
            bool owned = subjectAtSource ? namesSource : namesTarget;
            if (!owned) continue;

            bool predicateHolds = subjectAtSource ? namesTarget : namesSource;
            if (predicateHolds == wantHit) return true;
        }

        return false;
    }

    /// <summary>
    ///     The target-position fold behind every forbidden set and allow-list (GRAMMAR §5.3): each operand
    ///     resolved once, their memberships unioned, and each operand's head recorded against the
    ///     conflated nodes it admitted.
    /// </summary>
    internal static SelectionAdmission Operands(
        SelectionEvaluator selections, IReadOnlyList<Selection> operands, SelectionPosition position)
    {
        return Merged(CollectAll(selections, operands, position));
    }

    /// <summary>
    ///     One selection resolved in one position, carrying the heads that admitted each conflated node it
    ///     matched. A compound selection is collected part by part and folded through <see cref="Folded" />,
    ///     so the heads survive a level of nesting the enclosing noun could never state.
    /// </summary>
    internal static SelectionAdmission Collect(
        SelectionEvaluator selections, Selection selection, SelectionPosition position)
    {
        // Every compound selection takes its parts' stance and folds them (GRAMMAR §5.1, §4.1), which is
        // why one arm serves all three: a union's heads are its operands', a family's are its cells' — so a
        // project cell stages its project head and a file compiled into two cells is judged at each — and a
        // layer's are its definition's, so a project-defined layer names its nodes at that project exactly
        // as the bare project noun would. The enclosing selection's own adjectives gate what survives.
        // Totality over the noun hierarchy is the point rather than any one caller: spec-build item 27
        // keeps a family out of every position but the rule subject, and the subject path collects its
        // cells itself. A compound shape that fell through to the leaf below would lose its parts' heads,
        // which is the silent wrong answer rather than a loud one.
        if (selections.Composite(selection) is { } parts)
            return Folded(selections, selection, CollectAll(selections, parts, position));

        HashSet<TypeNode> members = selections.Evaluate(selection, position);
        return new SelectionAdmission(members, Stage(selections, selection, members));
    }

    /// <summary>
    ///     Every one of <paramref name="parts" /> but the one at <paramref name="skip" />, folded into one
    ///     admission — a family's <em>other</em> cells, as declared (GRAMMAR §5.1). A one-cell family
    ///     folds to nothing, which is what makes the cross-cell ban vacuous there.
    /// </summary>
    internal static SelectionAdmission AllBut(IReadOnlyList<SelectionAdmission> parts, int skip)
    {
        var others = new List<SelectionAdmission>(Math.Max(parts.Count - 1, 0));
        for (var i = 0; i < parts.Count; i++)
            if (i != skip)
                others.Add(parts[i]);

        return Merged(others);
    }

    /// <summary>
    ///     This admission narrowed to the nodes <paramref name="keep" /> also holds — a family's cell
    ///     intersected with the family's own membership, which is the subject one cell's law ranges over
    ///     (GRAMMAR §5.1).
    /// </summary>
    /// <remarks>
    ///     The heads survive the narrowing (through the same <see cref="Merge" /> a union fold takes), so
    ///     the cell still names its nodes at exactly the projects it named them at — which is what makes
    ///     per-cell attribution the whole-family attribution restricted, rather than a second answer.
    /// </remarks>
    internal SelectionAdmission Restricted(HashSet<TypeNode> keep)
    {
        var members = new HashSet<TypeNode>();
        foreach (TypeNode node in Members)
            if (keep.Contains(node))
                members.Add(node);

        return new SelectionAdmission(members, Merge([this], members));
    }

    /// <summary>
    ///     A union or a family folded from parts already collected in the same position — the enclosing
    ///     selection's own adjectives applied to the united membership (GRAMMAR §5.1), and the parts' heads
    ///     gated on what survives them. Exposed so the subject path can report per-part emptiness
    ///     (GRAMMAR §9) before the fold.
    /// </summary>
    /// <remarks>
    ///     One fold for both, because a union's operands and a family's cells differ in what they mean and
    ///     not in how they combine. A family folded separately could drift from the union it is defined to
    ///     equal.
    /// </remarks>
    internal static SelectionAdmission Folded(
        SelectionEvaluator selections, Selection enclosing, IReadOnlyList<SelectionAdmission> parts)
    {
        var united = new HashSet<TypeNode>();
        foreach (SelectionAdmission part in parts) united.UnionWith(part.Members);

        HashSet<TypeNode> members = selections.Narrow(enclosing, united);
        return new SelectionAdmission(members, Merge(parts, members));
    }

    // Every part of a compound selection, collected in the same position — a union's operands, a family's cells.
    private static IReadOnlyList<SelectionAdmission> CollectAll(
        SelectionEvaluator selections, IReadOnlyList<Selection> parts, SelectionPosition position)
    {
        var collected = new List<SelectionAdmission>(parts.Count);
        foreach (Selection part in parts) collected.Add(Collect(selections, part, position));

        return collected;
    }

    /// <summary>
    ///     This allow-set with the rule's own subject folded in as one more entry — the implicit
    ///     self-allowance the <c>MustOnly*</c> reference verbs carry (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     The subject arrives already resolved in <see cref="SelectionPosition.Subject" /> position, and
    ///     that is the whole of what "self" means: the refined membership the rule ranges over, so an
    ///     <c>Except</c> the subject spells narrows what the verb allows exactly as it narrows what the
    ///     verb governs. Folding that admission rather than re-resolving the subject selection in target
    ///     position is also what keeps the entry's §4.1 naming: the subject allows a node at precisely the
    ///     projects it names it at, so an intra-copy edge is allowed at the compiling project a
    ///     project-headed subject anchors and nowhere else.
    /// </remarks>
    internal SelectionAdmission IncludingSelf(SelectionAdmission subject)
    {
        return Merged([this, subject]);
    }

    // The operand fold's membership half: one operand is already its own admission, and the rest union.
    private static SelectionAdmission Merged(IReadOnlyList<SelectionAdmission> parts)
    {
        if (parts.Count == 1) return parts[0];

        var members = new HashSet<TypeNode>();
        foreach (SelectionAdmission part in parts) members.UnionWith(part.Members);

        return new SelectionAdmission(members, Merge(parts, members));
    }

    // The parts' staged heads folded into one map and gated on the enclosing membership: a union's
    // adjectives are node-local filters, so a node an Except dropped must not carry into the fold the
    // heads that admitted it to a part. Heads accumulate and the non-project flag ORs, because a node two
    // operands both reach is admitted by either.
    private static Dictionary<TypeNode, Provenance>? Merge(
        IReadOnlyList<SelectionAdmission> parts, HashSet<TypeNode> members)
    {
        Dictionary<TypeNode, Provenance>? merged = null;
        foreach (SelectionAdmission part in parts)
        {
            if (part._conflated is null) continue;

            foreach (KeyValuePair<TypeNode, Provenance> staged in part._conflated)
            {
                TypeNode node = staged.Key;
                if (!members.Contains(node)) continue;

                merged ??= new Dictionary<TypeNode, Provenance>();
                Provenance entry = EntryFor(merged, node);
                entry.NonProjectAdmitted |= staged.Value.NonProjectAdmitted;
                entry.ProjectHeads.AddRange(staged.Value.ProjectHeads);
            }
        }

        return merged;
    }

    // What one leaf selection admitted its conflated members under: the project name where the noun is a
    // project, the attribution-insensitive flag under every other noun. A model that conflates nothing
    // stages nothing at all, which is what makes NamingOf answer "at every project" there by construction.
    private static Dictionary<TypeNode, Provenance>? Stage(
        SelectionEvaluator selections, Selection selection, HashSet<TypeNode> members)
    {
        if (!selections.AnyMultiplyDeclared) return null;

        string? projectHead = (selection.Noun as ProjectNoun)?.Name;
        Dictionary<TypeNode, Provenance>? staged = null;
        foreach (TypeNode member in members)
        {
            if (member.AlsoDeclaredBy.Count == 0) continue;

            staged ??= new Dictionary<TypeNode, Provenance>();
            Provenance entry = EntryFor(staged, member);
            if (projectHead is null) entry.NonProjectAdmitted = true;
            else entry.ProjectHeads.Add(projectHead);
        }

        return staged;
    }

    private static Provenance EntryFor(Dictionary<TypeNode, Provenance> entries, TypeNode node)
    {
        if (entries.TryGetValue(node, out Provenance? entry)) return entry;

        entry = new Provenance();
        entries[node] = entry;
        return entry;
    }

    // Where this selection names one node — at every project, at the ones its project-headed selections
    // spelled, or nowhere at all. Read once per end and then asked per instance, because it is a property
    // of the (selection, node) pair and the instances only vary the project it is asked about.
    private Naming NamingOf(TypeNode node)
    {
        if (!Members.Contains(node)) return Naming.Nowhere;

        // A node one project declares is named at that project by anything that names it at all, and a
        // model that conflates nothing stages no heads to narrow with.
        if (_conflated is null || node.AlsoDeclaredBy.Count == 0) return Naming.Anywhere;
        if (!_conflated.TryGetValue(node, out Provenance? provenance)) return Naming.Nowhere;

        return provenance.NonProjectAdmitted ? Naming.Anywhere : Naming.At(provenance.ProjectHeads);
    }

    // What admitted one conflated node into a selection: the names of the project-headed selections that
    // reached it, and whether any head that is not a project did. Mutable because it is accumulated
    // through the fold, and read-only in effect afterwards — nothing outside this file ever holds one.
    private sealed class Provenance
    {
        internal List<string> ProjectHeads { get; } = [];

        internal bool NonProjectAdmitted { get; set; }
    }

    /// <summary>
    ///     Where one selection names one node: at every project, at the listed ones, or nowhere. The
    ///     per-node half of the §4.1 edge rule — <see cref="CountsEdge" /> resolves one per end and then
    ///     asks it the project each instance reached.
    /// </summary>
    /// <remarks>
    ///     "At every project" covers three different reasons that need no telling apart here: the node has
    ///     one declarer, the model conflates nothing, or a head that is not a project admitted it
    ///     (attribution-insensitive by §4.1). <see langword="default" /> is <see cref="Nowhere" />, which
    ///     is what a node outside the selection gets — and the right answer for both polarities, since an
    ///     operand that names nothing neither forbids nor allows.
    /// </remarks>
    private readonly struct Naming
    {
        private readonly IReadOnlyList<string>? _projectHeads;

        private Naming(bool everyProject, IReadOnlyList<string>? projectHeads)
        {
            EveryProject = everyProject;
            _projectHeads = projectHeads;
        }

        internal static Naming Nowhere => default;

        internal static Naming Anywhere => new(true, null);

        private bool EveryProject { get; }

        internal static Naming At(IReadOnlyList<string> projectHeads)
        {
            return new Naming(false, projectHeads);
        }

        internal bool Names(string project)
        {
            if (EveryProject) return true;
            if (_projectHeads is not { } heads) return false;

            for (var i = 0; i < heads.Count; i++)
                if (string.Equals(heads[i], project, StringComparison.Ordinal))
                    return true;

            return false;
        }
    }
}
