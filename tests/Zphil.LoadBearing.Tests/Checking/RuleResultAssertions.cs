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
    ///     Asserts the rule failed on exactly one edge violation of <paramref name="kind" />, running from
    ///     <paramref name="source" /> to <paramref name="target" /> and carrying at least one site.
    /// </summary>
    /// <remarks>
    ///     One method rather than one per verb, because the kind is often what is under test — a verb that
    ///     regresses emits a <see cref="ViolationKind.Reference" /> where a <see cref="ViolationKind.Catch" />
    ///     belongs, so it belongs in the argument list. Which slot a kind fills is the same mapping
    ///     <see cref="Violation.BaselineIdentity" /> reads, so no second copy of it lives here.
    /// </remarks>
    internal static Violation ShouldHaveFailedWithEdge(
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

        var checks = fragments
            .Select<string, Action>(fragment => () => detail.ShouldContain(fragment, customMessage: report))
            .ToArray();
        violation.ShouldSatisfyAllConditions(checks);

        return violation;
    }

    /// <summary>
    ///     The whole result as failure text: what the rule reported, every violation and warning in
    ///     report order, and the ratchet counts.
    /// </summary>
    private static string Describe(RuleResult result)
    {
        var lines = new List<string> { $"Rule '{result.Rule.Id}' ({result.Rule.Posture}) reported {result.Status}." };

        if (result.SkipReason is not null)
            lines.Add($"  skipped: {result.SkipReason}");

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
        string what = violation.Detail ?? Edge(violation) ?? "(nothing populated)";
        string sites = violation.Sites.Count == 0
            ? " (no sites)"
            : " @ " + string.Join(", ", violation.Sites);

        return $"    {violation.Kind} {what}{sites}";
    }

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
