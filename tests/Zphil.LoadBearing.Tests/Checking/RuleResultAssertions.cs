using Shouldly;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The Shouldly surface over <see cref="RuleResult" />: each assertion is phrased as the outcome a
///     rule reported (GRAMMAR §4.1) rather than as the property reads that establish it, and every one
///     of them carries the whole result into its failure message.
/// </summary>
/// <remarks>
///     <para>
///         A bare <c>Status.ShouldBe(RuleStatus.Failed)</c> reds with two enum names and not one word
///         about which violation actually fired, or whether any did. Passing
///         <see cref="Describe(RuleResult)" /> as the Shouldly reason puts the violations, the warnings
///         and the ratchet counts in front of the reader instead, which is the first thing a red raises.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>. Shouldly derives the "actual"
///         half of its message from the first stack frame after the last attributed one and reads that
///         line off disk, so leaving the attribute off makes an inner failure read
///         <c>violation.Target!.FullName should be …</c> — naming the property under test. Attributed,
///         the same failure would name the helper's own local and say nothing.
///     </para>
/// </remarks>
internal static class RuleResultAssertions
{
    private const string InertTargetMessage = "This rule is inert: its target selection matched no types.";

    private const string NothingPopulated = "(nothing populated)";

    /// <summary>Asserts the rule passed. Says nothing about warnings — an inert-target warning still passes.</summary>
    internal static RuleResult ShouldHavePassed(this RuleResult result)
    {
        result.Status.ShouldBe(RuleStatus.Passed, Describe(result));
        return result;
    }

    /// <summary>Asserts the rule passed with nothing to say: no violations and no warnings.</summary>
    internal static RuleResult ShouldHavePassedClean(this RuleResult result)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Passed, report);

        result.ShouldSatisfyAllConditions(
            () => result.Violations.ShouldBeEmpty(report),
            () => result.Warnings.ShouldBeEmpty(report));

        return result;
    }

    /// <summary>
    ///     Asserts the rule passed carrying exactly the inert-target warning — a target selection that
    ///     matched no types is a warning, never a failure (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     Holds the warning's pinned wording, which is why the sites that call this no longer spell it
    ///     out. One site still does, so the string keeps a literal pin that does not run through here.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedInertTarget(this RuleResult result)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Passed, report);
        result.Violations.ShouldBeEmpty(report);
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem(report);

        warning.ShouldSatisfyAllConditions(
            () => warning.Kind.ShouldBe(CheckWarningKind.InertTarget, report),
            () => warning.Message.ShouldBe(InertTargetMessage, report));

        return result;
    }

    /// <summary>
    ///     Asserts the run reached no verdict for the rule and said why: <see cref="RuleStatus.Skipped" />
    ///     carrying <paramref name="reason" />, with nothing red to show for it.
    /// </summary>
    /// <remarks>
    ///     Says nothing about the ratchet counts — a skip that zeroed them and a skip that did not are both
    ///     Skipped, so the rows whose subject is those counts read them in their own right.
    /// </remarks>
    internal static RuleResult ShouldHaveSkipped(this RuleResult result, string reason)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Skipped, report);

        result.ShouldSatisfyAllConditions(
            () => result.SkipReason.ShouldBe(reason, report),
            () => result.Violations.ShouldBeEmpty(report));

        return result;
    }

    /// <summary>
    ///     Asserts the ratchet grandfathered exactly <paramref name="count" /> violations (GRAMMAR §4.6).
    ///     Says nothing about status: a grandfathered rule that also has a new red is Failed and still
    ///     carries its blessed debt.
    /// </summary>
    internal static RuleResult ShouldHaveGrandfathered(this RuleResult result, int count)
    {
        result.Grandfathered.Count.ShouldBe(count, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the rule failed. Says nothing about which violations did it — for the rows whose subject is
    ///     the violation list itself, which then read it in its own right.
    /// </summary>
    internal static RuleResult ShouldHaveFailed(this RuleResult result)
    {
        result.Status.ShouldBe(RuleStatus.Failed, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on an edge violation of <paramref name="kind" />, running from
    ///     <paramref name="source" /> to <paramref name="target" /> and carrying at least one site.
    /// </summary>
    /// <remarks>
    ///     One method rather than one per verb, because the kind is often what is under test — a verb that
    ///     regresses emits a <see cref="ViolationKind.Reference" /> where a <see cref="ViolationKind.Catch" />
    ///     belongs, so it belongs in the argument list. Which slot a kind fills is the same mapping
    ///     <see cref="Violation.BaselineIdentity" /> reads, so no second copy of it lives here.
    /// </remarks>
    internal static Violation ShouldHaveFailedWithSingleEdge(
        this RuleResult result, ViolationKind kind, string source, string target)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(kind, report),
            () => violation.Source!.FullName.ShouldBe(source, report),
            () => violation.Target!.FullName.ShouldBe(target, report),
            () => violation.Sites.ShouldNotBeEmpty(report));

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly the edge violations of <paramref name="kind" /> that
    ///     <paramref name="edges" /> names, each rendered <c>Source -&gt; Target</c>. In any order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The set is exhaustive for the kind and silent about every other kind, which is what lets one
    ///         row state a desugared rule's whole inbound edge set without spelling its shape violations too.
    ///     </para>
    ///     <para>
    ///         Unordered, because <see cref="Describe(RuleResult)" /> already prints every violation in report
    ///         order: an ordered comparison would add nothing to the message and would report every later
    ///         index as wrong when one early element is missing. Report order is a product behaviour pinned in
    ///         its own right by <c>CheckerSemanticsTests.ViolationOrder_IsOrdinalBySourceThenTarget</c>, over a
    ///         kind-agnostic sort, so nothing goes uncovered by this tolerance.
    ///     </para>
    ///     <para>
    ///         Takes an array rather than <c>params</c>, so the brackets carry the exhaustiveness at the call
    ///         site: under <c>params</c> a one-edge assertion is written as a bare string, which reads as
    ///         naming one edge among however many the rule found. <c>["A -&gt; B"]</c> reads as the whole set,
    ///         which is what it claims.
    ///     </para>
    /// </remarks>
    internal static RuleResult ShouldHaveFailedWithEdges(
        this RuleResult result, ViolationKind kind, string[] edges)
    {
        string report = Describe(result);
        RequireExpectation(edges, report);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Rendered(result, kind)
            .ShouldBe(edges, ignoreOrder: true, customMessage: report);

        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly the shape violations <paramref name="subjects" /> names, each
    ///     rendered as the offending type's FullName (GRAMMAR §4.4). In any order, for the reason given on
    ///     <see cref="ShouldHaveFailedWithEdges" />.
    /// </summary>
    internal static RuleResult ShouldHaveFailedWithSubjects(this RuleResult result, string[] subjects)
    {
        string report = Describe(result);
        RequireExpectation(subjects, report);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Rendered(result, ViolationKind.Shape)
            .ShouldBe(subjects, ignoreOrder: true, customMessage: report);

        return result;
    }

    /// <summary>
    ///     Asserts the rule failed carrying every one of <paramref name="edges" /> as a violation of
    ///     <paramref name="kind" /> — and says nothing about what else it carries, for the rows whose fixture
    ///     is a whole solution and whose claim is that one edge is in the report.
    /// </summary>
    /// <remarks>
    ///     Batched, so a row naming several edges reds on all the absent ones at once rather than on the
    ///     first — the same reason <see cref="ShouldHaveFailedWithDetailContaining" /> batches its fragments,
    ///     and why this one keeps <c>params</c> as that verb does.
    /// </remarks>
    internal static RuleResult ShouldHaveFailedWithEdgesIncluding(
        this RuleResult result, ViolationKind kind, params string[] edges)
    {
        string report = Describe(result);
        RequireExpectation(edges, report);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        IReadOnlyList<string> rendered = Rendered(result, kind);

        Action[] checks = edges
            .Select<string, Action>(edge => () => rendered.ShouldContain(edge, report))
            .ToArray();
        result.ShouldSatisfyAllConditions(checks);

        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly one violation of <paramref name="kind" /> whose detail is
    ///     <paramref name="detail" /> — the shape of an empty subject or a rule error, which carry free
    ///     text and no edge (GRAMMAR §4.3).
    /// </summary>
    internal static Violation ShouldHaveFailedWithDetail(
        this RuleResult result, ViolationKind kind, string detail)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(kind, report),
            () => violation.Detail.ShouldBe(detail, report));

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly one violation of <paramref name="kind" /> whose detail
    ///     contains every one of <paramref name="fragments" /> — for a refusal whose text names a symbol
    ///     or an exception the test cannot spell in full.
    /// </summary>
    internal static Violation ShouldHaveFailedWithDetailContaining(
        this RuleResult result, ViolationKind kind, params string[] fragments)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);
        violation.Kind.ShouldBe(kind, report);
        string detail = violation.Detail.ShouldNotBeNull(report);

        Action[] checks = fragments
            .Select<string, Action>(fragment => () => detail.ShouldContain(fragment, customMessage: report))
            .ToArray();
        violation.ShouldSatisfyAllConditions(checks);

        return violation;
    }

    /// <summary>
    ///     The violations of one <paramref name="kind" />, each rendered the way the failure text renders it,
    ///     in report order.
    /// </summary>
    /// <remarks>
    ///     Renders through <see cref="Edge" /> rather than dereferencing the slots a kind is expected to fill,
    ///     for the reason <see cref="Edge" /> exists: a violation whose slots are not the ones the caller
    ///     expected has to red by mismatch, naming what it does carry, rather than by
    ///     <see cref="NullReferenceException" />. For every kind these assertions take, this is the same
    ///     string <c>Checker</c>'s readers project.
    /// </remarks>
    private static IReadOnlyList<string> Rendered(RuleResult result, ViolationKind kind)
    {
        return result.Violations.Where(violation => violation.Kind == kind)
            .Select(violation => Edge(violation) ?? NothingPopulated)
            .ToList();
    }

    /// <summary>Reds when the caller expected nothing, which no collection claim here could red on.</summary>
    /// <remarks>
    ///     An empty expectation is a silent total pass: a rule that failed on some <em>other</em> kind
    ///     satisfies the status assertion and then matches empty against empty. The rows that mean it —
    ///     "failed, but not on this kind" — have <see cref="ShouldHaveFailed" /> to say so.
    /// </remarks>
    private static void RequireExpectation(string[] expected, string report)
    {
        if (expected.Length > 0) return;

        throw new ShouldAssertException(
            "the expected set is empty, which cannot red: a rule that failed on a different kind would "
            + "satisfy it. Assert ShouldHaveFailed() if the status is the whole claim." + Environment.NewLine
            + report);
    }

    /// <summary>
    ///     The whole result as failure text: what the rule reported, the subject it swept, every violation
    ///     and warning in report order, and the ratchet counts.
    /// </summary>
    /// <remarks>
    ///     The subject line is carried unconditionally, including its zero — unlike the report, which stays
    ///     silent when nothing is generated. A red raises the question "what was this rule even looking at",
    ///     and "0 types" is the answer that ends the search fastest.
    /// </remarks>
    private static string Describe(RuleResult result)
    {
        var lines = new List<string> { $"Rule '{result.Rule.Id}' ({result.Rule.Posture}) reported {result.Status}." };

        if (result.SkipReason is not null)
            lines.Add($"  skipped: {result.SkipReason}");

        lines.Add($"  subject: {result.SubjectTypes} types, {result.SubjectGeneratedTypes} generated");
        lines.Add($"  violations ({result.Violations.Count}):");
        lines.AddRange(result.Violations.Select(Describe));
        lines.Add($"  warnings ({result.Warnings.Count}):");
        lines.AddRange(result.Warnings.Select(warning => $"    {warning.Kind}: {warning.Message}"));
        lines.Add($"  ratchet: {result.Grandfathered.Count} grandfathered, "
                  + $"{result.StaleBaselineEntries} stale, captured: {result.BaselineCaptured}");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>One violation as <c>Kind Source -&gt; Target @ file:line</c>, whichever slots it fills.</summary>
    /// <remarks>
    ///     Reads the slots rather than switching on <see cref="Violation.Kind" />: this renders the
    ///     failure text for assertions that are themselves about a wrong kind, so it has to stay true
    ///     for a violation whose slots are not the ones the caller expected.
    /// </remarks>
    private static string Describe(Violation violation)
    {
        string what = violation.Detail ?? Edge(violation) ?? NothingPopulated;
        string sites = violation.Sites.Count == 0
            ? " (no sites)"
            : " @ " + string.Join(", ", violation.Sites);

        return $"    {violation.Kind} {what}{sites}";
    }

    /// <summary>The edge a violation names, from whichever slots it fills, or null when it fills none.</summary>
    /// <remarks>
    ///     Renders the same <c>Source -&gt; Target</c> string as <c>Checker</c>'s private <c>Pair</c> and must
    ///     not be merged with it: that one dereferences the slots its kind promises, which is right for a
    ///     reader whose caller chose the kind, while this one tolerates every slot being absent, which is
    ///     right for text that has to survive a violation of the wrong kind.
    /// </remarks>
    private static string? Edge(Violation violation)
    {
        string? source = violation.Source?.FullName
                         ?? violation.Subject?.FullName
                         ?? violation.SubjectMember?.SymbolId;
        string? target = violation.Target?.FullName ?? violation.Member?.SymbolId;

        if (source is null) return null;

        return target is null ? source : $"{source} -> {target}";
    }
}
