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
///         <see cref="Admits" /> is the edge test, and it parts from <see cref="Contains" /> only on a
///         node several projects declare. An edge whose source compiles its own copy of the target is
///         intra-project — attributed to the projects both ends declare — so a project-headed operand
///         admits the target only when the compiling project is one it named. Every other edge is
///         attributed to the winner alone (<see cref="TypeNode.ProjectName" />), so a project-headed
///         operand admits the target only when it named the winner. Heads that are not projects (a
///         <c>typeof</c>, a namespace, a layer, <c>Registered</c>) are attribution-insensitive:
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
    ///     test every position that is not an edge end takes, and the one an edge's <em>source</em>
    ///     operand takes — every declarer's copy genuinely makes the reference.
    /// </summary>
    internal bool Contains(TypeNode node)
    {
        return Members.Contains(node);
    }

    /// <summary>
    ///     Whether the selection names <paramref name="node" /> <em>for an edge</em> the compilation of
    ///     <paramref name="edgeSource" /> made — membership plus the §4.1 attribution rule stated in the
    ///     remarks on this type.
    /// </summary>
    internal bool Admits(TypeNode node, TypeNode edgeSource)
    {
        // Nothing conflated, nothing to attribute: the fast path is the whole degenerate case, and it is
        // reached without a dictionary probe on every ordinary codebase.
        if (_conflated is null || node.AlsoDeclaredBy.Count == 0) return Contains(node);
        if (!_conflated.TryGetValue(node, out Provenance? provenance)) return false;
        if (provenance.NonProjectAdmitted) return true;

        // Which projects the edge is attributed to is a property of the edge, not of the head that
        // admitted the node, so it is decided once: the source declaring the target itself makes the edge
        // intra-project, and every other edge belongs to the declarer whose facts the node carries.
        bool intraProject = DeclaresAny(edgeSource, node);
        List<string> heads = provenance.ProjectHeads;
        for (var i = 0; i < heads.Count; i++)
        {
            string head = heads[i];
            bool attributed = intraProject
                ? edgeSource.IsDeclaredBy(head)
                : string.Equals(head, node.ProjectName, StringComparison.Ordinal);

            if (attributed) return true;
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
        var collected = new List<SelectionAdmission>(operands.Count);
        foreach (Selection operand in operands) collected.Add(Collect(selections, operand, position));

        return Merged(collected);
    }

    /// <summary>
    ///     One selection resolved in one position, carrying the heads that admitted each conflated node it
    ///     matched. A union is collected part by part and folded through <see cref="United" />, so the
    ///     heads survive a level of nesting the union's own noun could never state.
    /// </summary>
    internal static SelectionAdmission Collect(
        SelectionEvaluator selections, Selection selection, SelectionPosition position)
    {
        // The union arm comes first because a union has no single noun — reading Selection.Noun on one
        // throws by design (GRAMMAR §5.1) — and because its heads are its parts'.
        if (selection is UnionSelection union)
        {
            var parts = new List<SelectionAdmission>(union.Parts.Count);
            foreach (Selection part in union.Parts) parts.Add(Collect(selections, part, position));

            return United(selections, union, parts);
        }

        HashSet<TypeNode> members = selections.Evaluate(selection, position);
        return new SelectionAdmission(members, Stage(selections, selection, members));
    }

    /// <summary>
    ///     A union folded from parts already collected in the same position — the union's own adjectives
    ///     applied to the unioned membership (GRAMMAR §5.1), and the parts' heads gated on what survives
    ///     them. Exposed so the subject path can report per-operand emptiness (GRAMMAR §9) before the fold.
    /// </summary>
    internal static SelectionAdmission United(
        SelectionEvaluator selections, UnionSelection union, IReadOnlyList<SelectionAdmission> parts)
    {
        List<HashSet<TypeNode>> sets = parts.Select(part => part.Members).ToList();
        HashSet<TypeNode> members = selections.Unite(union, sets);
        return new SelectionAdmission(members, Merge(parts, members));
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
    // stages nothing at all, which is what makes Admits reduce to Contains there rather than by agreement.
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

    // D(S) ∩ D(T) ≠ ∅ — whether the referencing compilation declares the target type itself, which is
    // what makes an edge intra-project however the target node's facts were attributed.
    private static bool DeclaresAny(TypeNode edgeSource, TypeNode node)
    {
        if (edgeSource.IsDeclaredBy(node.ProjectName)) return true;

        IReadOnlyList<string> alsoDeclaredBy = node.AlsoDeclaredBy;
        for (var i = 0; i < alsoDeclaredBy.Count; i++)
            if (edgeSource.IsDeclaredBy(alsoDeclaredBy[i]))
                return true;

        return false;
    }

    // What admitted one conflated node into a selection: the names of the project-headed selections that
    // reached it, and whether any head that is not a project did. Mutable because it is accumulated
    // through the fold, and read-only in effect afterwards — nothing outside this file ever holds one.
    private sealed class Provenance
    {
        internal List<string> ProjectHeads { get; } = [];

        internal bool NonProjectAdmitted { get; set; }
    }
}
