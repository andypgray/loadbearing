namespace Zphil.LoadBearing.Tests.Dogfood;

/// <summary>
///     Every <c>Must*</c> verb on Core's public surface that the self-spec leaves unused, with the reason it
///     stays unused. <see cref="SelfSpecTests.VerbLedger_AccountsForEveryUnusedVerb" /> holds this list equal
///     to the unused set, so a new verb is either used on this repository's real code or named here with its
///     reason, and an entry whose verb the spec takes up comes out in the same change. The keys are
///     <c>nameof</c> expressions, so a renamed or removed verb fails the build rather than the gate.
/// </summary>
internal static class VerbLedger
{
    private const string CatchAxisUnrefined =
        "An unrefined form on the catch axis: the law here is MustNotSwallow, which passes the house catch " +
        "shapes this would red, the when-filtered catch and the clause that ends in a throw.";

    private const string NoUniformTypeShape =
        "No layer here has a uniform type shape: the two of this family tried against the real code each " +
        "went red on legitimate members and needed an Except list longer than the rule.";

    private const string MemberShapeTwin =
        "A member-level twin of the type-shape modals, and like them it constrains nothing here.";

    private const string SeamsAreInjected =
        "The seams here are consumed by injection rather than inheritance, and the one true hierarchy " +
        "statement, that every *Constraint derives from Constraint, is already model/constraint-nodes.";

    /// <summary>The unused verbs, each keyed to the sentence saying why the self-spec does not use it.</summary>
    public static IReadOnlyDictionary<string, string> Unused { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(SelectionConstraints.MustNotCatch)] = CatchAxisUnrefined,
            [nameof(SelectionConstraints.MustNotCatchUnfiltered)] = CatchAxisUnrefined,
            [nameof(SelectionConstraints.MustBeSealed)] = NoUniformTypeShape,
            [nameof(SelectionConstraints.MustBeAbstract)] = NoUniformTypeShape,
            [nameof(SelectionConstraints.MustBeStatic)] = NoUniformTypeShape,
            [nameof(SelectionConstraints.MustBePublic)] = NoUniformTypeShape,
            [nameof(SelectionConstraints.MustBeInternal)] = NoUniformTypeShape,
            [nameof(MemberSelectionConstraints.MustBePrivate)] = MemberShapeTwin,
            [nameof(MemberSelectionConstraints.MustBeVirtual)] = MemberShapeTwin,
            [nameof(SelectionConstraints.MustImplement)] = SeamsAreInjected,
            [nameof(SelectionConstraints.MustNotImplement)] = SeamsAreInjected,
            [nameof(SelectionConstraints.MustDeriveFrom)] = SeamsAreInjected,
            [nameof(SelectionConstraints.MustNotDeriveFrom)] = SeamsAreInjected,
            [nameof(SelectionConstraints.MustNotBeAttributedWith)] =
                "No attribute is forbidden here, and inventing a ban to exercise a verb is the contrivance " +
                "this ledger refuses.",
            [nameof(SelectionConstraints.MustHaveNameMatching)] =
                "The two naming laws here are a prefix and a suffix, which say it more exactly than a pattern.",
            [nameof(SelectionConstraints.MustOnlyReferenceItself)] =
                "Core is this solution's only reference-graph leaf and the temptation it faces is a package; " +
                "the leaf verb exempts external targets, so layering/core-no-roslyn says what it could not.",
            [nameof(SelectionConstraints.MustOnlyBeReferencedByItself)] =
                "Nothing here is hermetic: the test project reaches into every layer by design, so an " +
                "inbound leaf over any of them would be red for the reason the tests exist.",
            [nameof(SelectionConstraints.MustBeRegistered)] =
                "Nothing here is registered by convention: the composition root wires a hand-written list " +
                "of singletons, so a completeness rule over them could only restate that list at itself.",
            [nameof(SelectionConstraints.MustBelongTo)] =
                "The five shipping projects are the five assembly-shaped layers, so every type they declare " +
                "belongs to one by construction and the rule could never red; a sixth shipping project " +
                "surfaces under packaging/only-the-four-ship instead.",
            [nameof(SelectionConstraints.MustHaveExactlyOneCounterpart)] =
                "This suite organizes tests by behavior rather than per type, so a rule demanding a " +
                "{Name}Tests class each would record a convention the tree does not follow as unpaid debt."
        };
}
