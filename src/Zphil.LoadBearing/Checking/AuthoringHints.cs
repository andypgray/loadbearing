using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The cure that rides each authoring signal: what to change when a rule's own selection, rather than
///     the code it ranges over, is what went wrong (GRAMMAR §4.1).
/// </summary>
/// <remarks>
///     <para>
///         The advice is chosen from the selection that came up empty, and must stay that way. An empty
///         subject arrives from a namespace glob, a project name, a <c>typeof</c> anchor or any of the
///         narrowing calls, and each fails for its own reason — so one fixed sentence about glob semantics
///         would be false on most of them, at the one moment a reader is most likely to act on it.
///     </para>
///     <para>
///         Clauses are lower-cased and unterminated so one composer can stand a clause alone or hang a
///         tail on it; <see cref="Sentence" /> is the only place either happens, and a clause must
///         therefore open with a letter.
///     </para>
///     <para>
///         A partial model <em>replaces</em> every cure here rather than appending to one, and every entry
///         point therefore takes the fact. Telling a reader to check a selection against what the solution
///         declares is wrong when the reason it came up empty may be that a project never loaded, and a
///         sentence carrying both cures would break the single-semicolon shape these clauses are built to
///         keep. It is a replacement rather than a precedence call: the shape advice is not merely less
///         urgent there, it is advice about the wrong thing.
///     </para>
/// </remarks>
internal static class AuthoringHints
{
    /// <summary>
    ///     The cure on a member subject that resolved to nothing. It names both shapes that fail here,
    ///     because <c>EvaluateMember</c> dispatches ahead of the type-subject gate: a member rule whose
    ///     type selection matched nothing reports the member message, not the type one. Reached through
    ///     <see cref="ForMemberSubject" />, which is what a partial model replaces.
    /// </summary>
    internal const string EmptyMemberSubject =
        "Either the type selection matched nothing or none of the members it reached survived; check the "
        + "types first, then .Methods, .Properties or .Fields and the filters after them.";

    /// <summary>
    ///     The cure on a project subject that resolved to nothing (GRAMMAR §4.10). It opens on the naming
    ///     rule rather than on the emptiness: the message it sits under has just said that. Reached through
    ///     <see cref="ForProjectSubject" />, which is what a partial model replaces.
    /// </summary>
    internal const string EmptyProjectSubject =
        "A project is named by its csproj file name, and .Matching(...) globs that name with a `*` that has "
        + "no dot structure; check the names against the solution.";

    // Each clause states its shape's SEMANTICS and stops there; the caller adds the one action clause. The
    // split is what keeps a composed hint to a single semicolon — a clause carrying its own action reads as
    // a run-on the moment a tail joins it.
    private const string NamespaceClause =
        "a trailing `.*` covers the namespace itself and everything under it, and `Legacy*` never crosses "
        + "a dot";

    // The csproj file name, not the assembly name: an AssemblyName property or a renamed project file parts
    // the two, and the extraction keys projects on the former.
    private const string ProjectClause =
        "a project is named by its csproj file name, matched ordinally and case-sensitively, and an "
        + "AssemblyName property does not rename it";

    private const string TypeClause =
        "a typeof anchor reaches only a type this solution declares, never one supplied by a package or by "
        + "the spec project itself";

    private const string NarrowingClause =
        "every term narrows, so the set is no wider than the last one applied";

    private const string SubjectTail = "; check the selection against what the solution declares";

    // The consequence leads the choice because it is what makes the choice worth making: an inert rule is
    // green, so nothing will ever raise the question again.
    private const string InertTail =
        "; a rule whose target matches nothing passes forever, so decide before keeping it";

    /// <summary>
    ///     What to change when <paramref name="subject" /> selected nothing, or the partial-model cure where
    ///     <paramref name="model" /> says part of the codebase never loaded.
    /// </summary>
    internal static string ForSubject(Selection subject, IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? Sentence(ShapeAdvice(subject) + SubjectTail);
    }

    /// <summary>
    ///     What to change when a member subject resolved to nothing — the one fixed clause, or the partial
    ///     model where there is one. The member and project cures read off no selection shape, so these two
    ///     entry points exist only for the replacement: without them a failed project would reach half the
    ///     raise sites and leave the other half advising a spec fix for a load failure.
    /// </summary>
    internal static string ForMemberSubject(IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? EmptyMemberSubject;
    }

    /// <summary>
    ///     What to change when a project subject resolved to nothing, or the partial model where there is one
    ///     (<see cref="ForMemberSubject" />'s reasoning).
    /// </summary>
    internal static string ForProjectSubject(IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? EmptyProjectSubject;
    }

    /// <summary>
    ///     What to change when a forbidden-set verb's target selected nothing. The advice is the first
    ///     pattern operand's — the same operand the inert gate itself tests for, so the sentence describes
    ///     the selection that raised the warning rather than a sibling that did not. A partial model replaces
    ///     it, the warning being the signal a rotted target and an unextracted one both arrive on.
    /// </summary>
    internal static string ForInertTarget(IReadOnlyList<Selection> operands, IncompleteModel? model)
    {
        if (model is { } incomplete) return incomplete.EmptySelectionHint;

        Selection? pattern = operands.FirstOrDefault(SelectionEvaluator.IsPatternSelection);
        return Sentence(ShapeAdvice(pattern) + InertTail);
    }

    // Null covers an operand list with no pattern in it; the narrowing clause is true of every selection,
    // which is what makes it the safe floor.
    private static string ShapeAdvice(Selection? selection)
    {
        if (selection is null) return NarrowingClause;

        return SelectionWalk.NounOf(selection) switch
        {
            // A layer is transparent to what defines it, so its cure is its definition's. The glob form has
            // no definition to reach and is namespaces by construction.
            LayerNoun { Definition: { } definition } => ShapeAdvice(definition),
            LayerNoun => NamespaceClause,
            NamespaceNoun => NamespaceClause,
            ProjectNoun => ProjectClause,
            TypeNoun => TypeClause,
            // A union answers no noun at all, and RegionOf declines it; what is left that names a namespace
            // region is arch.Types narrowed by exactly one InNamespace.
            _ => LayerNoun.RegionOf(selection).Count > 0 ? NamespaceClause : NarrowingClause
        };
    }

    private static string Sentence(string clause)
    {
        return ProseFormat.Capitalize(clause) + ".";
    }
}
