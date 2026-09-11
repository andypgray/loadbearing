using System.Text.Json;
using Shouldly;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over a check verb's <c>--json</c> document, beside the
///     <see cref="CheckJson" /> readers it builds on: each assertion is phrased as the outcome one rule
///     reported, and carries that rule's whole JSON object into its failure message.
/// </summary>
/// <remarks>
///     <para>
///         The receiver is the document rather than the <c>CliResult</c> that carried it, because the same
///         document reaches this suite two ways — as a CLI run's stdout and as an MCP tool's text — and an
///         assertion keyed to one of them could not serve the other.
///     </para>
///     <para>
///         A bare <c>GetProperty("status").GetString().ShouldBe("failed")</c> reds with two bare words and
///         says nothing about which rule, which violations, or whether the rule was in the report at all.
///         Passing <see cref="Describe" /> as the Shouldly reason puts the rule object in front of the
///         reader instead.
///     </para>
///     <para>
///         The outcome verbs mirror <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />
///         name for name, so one claim reads the same whether a row holds the model or the wire document.
///         Kinds and slot names stay wire strings rather than
///         <see cref="Zphil.LoadBearing.Checking.ViolationKind" />: these assertions read the document
///         exactly as a consumer does, and the golden test already pins the spellings.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class CheckJsonAssertions
{
    /// <summary>Asserts <paramref name="ruleId" /> is green in <paramref name="checkJson" />.</summary>
    internal static string ShouldHavePassed(this string checkJson, string ruleId)
    {
        JsonElement rule = RuleOrFail(checkJson, ruleId);
        rule.GetProperty("status")
            .GetString()
            .ShouldBe("passed", Describe(rule));

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> is red in <paramref name="checkJson" />. Says nothing about
    ///     which violations did it — for the rows whose subject is the status alone.
    /// </summary>
    internal static string ShouldHaveFailed(this string checkJson, string ruleId)
    {
        JsonElement rule = RuleOrFail(checkJson, ruleId);
        rule.GetProperty("status")
            .GetString()
            .ShouldBe("failed", Describe(rule));

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> reports <paramref name="count" /> in its
    ///     <c>violationCount</c> — the key a consumer scripts against, present at every grain, so the claim
    ///     reads the same off a full, overview or skeleton document. Says nothing about status: zero on a
    ///     passing rule is as much the contract as a red's tally.
    /// </summary>
    /// <remarks>
    ///     Presence is asserted through <c>TryGetProperty</c> rather than read bare, because the key going
    ///     absent is the exact regression this verb guards — a <see cref="KeyNotFoundException" /> would
    ///     report it as an error naming neither the rule nor what it did carry.
    /// </remarks>
    internal static string ShouldReportViolationCount(this string checkJson, string ruleId, int count)
    {
        JsonElement rule = RuleOrFail(checkJson, ruleId);
        string report = Describe(rule);

        rule.TryGetProperty("violationCount", out JsonElement violationCount)
            .ShouldBeTrue(report);
        violationCount.GetInt32()
            .ShouldBe(count, report);

        return checkJson;
    }

    /// <summary>
    ///     Asserts the run reached no verdict for <paramref name="ruleId" /> and said why: status
    ///     <c>skipped</c> carrying <paramref name="reason" /> (GRAMMAR §4.1).
    /// </summary>
    internal static string ShouldHaveSkipped(this string checkJson, string ruleId, string reason)
    {
        JsonElement rule = RuleOrFail(checkJson, ruleId);
        string report = Describe(rule);

        rule.GetProperty("status")
            .GetString()
            .ShouldBe("skipped", report);
        Slot(rule, "skipReason")
            .ShouldBe(reason, report);

        return checkJson;
    }

    /// <summary>
    ///     Asserts the ratchet grandfathered exactly <paramref name="count" /> violations of
    ///     <paramref name="ruleId" /> (GRAMMAR §4.6). Says nothing about status: a grandfathered rule that
    ///     also has a new red is failed and still carries its blessed debt.
    /// </summary>
    internal static string ShouldHaveGrandfathered(this string checkJson, string ruleId, int count)
    {
        JsonElement rule = RuleOrFail(checkJson, ruleId);
        rule.GetProperty("baseline")
            .GetProperty("grandfathered")
            .GetInt32()
            .ShouldBe(count, Describe(rule));

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" />'s subject swept <paramref name="types" /> types, of which
    ///     <paramref name="generated" /> were generator output (GRAMMAR §5.2). Says nothing about status: a
    ///     rule aimed at generated code is as often green as red, which is the reason the pair is reported
    ///     for both.
    /// </summary>
    internal static string ShouldHaveSubjectCoverage(
        this string checkJson, string ruleId, int types, int generated)
    {
        JsonElement rule = RuleOrFail(checkJson, ruleId);
        string report = Describe(rule);

        rule.GetProperty("subjectTypes")
            .GetInt32()
            .ShouldBe(types, report);
        rule.GetProperty("subjectGeneratedTypes")
            .GetInt32()
            .ShouldBe(generated, report);

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> states nothing about its subject — both keys absent, which is
    ///     what a rule whose subject holds no generator output reports.
    /// </summary>
    /// <remarks>
    ///     Its own verb rather than <see cref="ShouldHaveSubjectCoverage" /> with a zero: the pair is written
    ///     only when something was generated, so there would be no denominator to pass and the argument would
    ///     read as a claim while asserting nothing. Both absences are checked, because half a pair is a
    ///     defect in its own right — a denominator with no numerator is a fact about nothing.
    /// </remarks>
    internal static string ShouldReportNoSubjectCoverage(this string checkJson, string ruleId)
    {
        JsonElement rule = RuleOrFail(checkJson, ruleId);
        string report = Describe(rule);

        rule.TryGetProperty("subjectTypes", out _)
            .ShouldBeFalse(report);
        rule.TryGetProperty("subjectGeneratedTypes", out _)
            .ShouldBeFalse(report);

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> is red in <paramref name="checkJson" /> with exactly the
    ///     violations <paramref name="expected" /> names — each read off the violation's
    ///     <paramref name="endpoint" /> field, which is the kind's own identity (the caught, injected or
    ///     exposed type; the member DocId a member rule keys on). In any order.
    /// </summary>
    internal static string ShouldHaveFailedWith(
        this string checkJson, string ruleId, string endpoint, string[] expected)
    {
        JsonElement red = RuleOrFail(checkJson, ruleId);
        string report = Describe(red);

        red.GetProperty("status")
            .GetString()
            .ShouldBe("failed", report);
        red.GetProperty("violations")
            .EnumerateArray()
            .Select(violation => violation.GetProperty(endpoint)
                .GetString())
            .ShouldBe(expected, ignoreOrder: true, customMessage: report);

        return checkJson;
    }

    /// <summary>
    ///     Asserts every one of <paramref name="ruleId" />'s violations carries <paramref name="detail" />
    ///     — the circle the circular-references verb names, which the JSON channel alone reads (GRAMMAR
    ///     §5.3). Says nothing about the edges themselves, so a row can state the detail beside a separate
    ///     claim about which pairs are red.
    /// </summary>
    /// <remarks>
    ///     Reds when the rule reported nothing: an empty violation array would satisfy "every one of them"
    ///     vacuously, and a rule that stopped reporting is exactly what this verb has to catch.
    /// </remarks>
    internal static string ShouldHaveDetailOnEveryViolation(this string checkJson, string ruleId, string detail)
    {
        JsonElement red = RuleOrFail(checkJson, ruleId);
        string report = Describe(red);

        List<JsonElement> violations = red.GetProperty("violations")
            .EnumerateArray()
            .ToList();
        violations.ShouldNotBeEmpty(report);

        Action[] checks = violations
            .Select<JsonElement, Action>(violation => () => Slot(violation, "detail")
                .ShouldBe(detail, report))
            .ToArray();
        red.ShouldSatisfyAllConditions(checks);

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> is green in <paramref name="checkJson" /> on
    ///     <paramref name="count" /> grandfathered violations — captured, not fixed.
    /// </summary>
    internal static string ShouldHaveCaptured(this string checkJson, string ruleId, int count)
    {
        JsonElement captured = RuleOrFail(checkJson, ruleId);
        string report = Describe(captured);

        captured.GetProperty("status")
            .GetString()
            .ShouldBe("passed", report);
        captured.GetProperty("baseline")
            .GetProperty("grandfathered")
            .GetInt32()
            .ShouldBe(count, report);

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> is red on exactly one violation of <paramref name="kind" />,
    ///     running from <paramref name="source" /> to <paramref name="target" /> — the type-pair shape a
    ///     reference, construction, injection, catch, throw or expose violation carries.
    /// </summary>
    /// <remarks>
    ///     One method rather than one per kind, and the kind leads the argument list because it is often
    ///     what is under test: a verb that regresses emits the wrong kind over the very same endpoints.
    /// </remarks>
    internal static string ShouldHaveSingleViolationOnEdge(
        this string checkJson, string ruleId, string kind, string source, string target)
    {
        JsonElement red = RuleOrFail(checkJson, ruleId);
        string report = Describe(red);
        JsonElement violation = SoleViolationOrFail(red, report);

        violation.ShouldSatisfyAllConditions(
            () => Slot(violation, "kind")
                .ShouldBe(kind, report),
            () => Slot(violation, "source")
                .ShouldBe(source, report),
            () => Slot(violation, "target")
                .ShouldBe(target, report));

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> is red on exactly one violation of <paramref name="kind" />
    ///     naming <paramref name="member" /> in <paramref name="slot" /> — the member DocId shape, where a
    ///     <c>memberUse</c> keys on <c>targetMember</c> and a <c>memberShape</c> on <c>subjectMember</c>
    ///     (GRAMMAR §4.5, §4.6).
    /// </summary>
    /// <remarks>
    ///     Split from <see cref="ShouldHaveSingleViolationOnEdge" /> by slot shape rather than derived from
    ///     the kind: which slot a kind fills is a mapping the model side deliberately keeps in one place,
    ///     and deriving it here would be a second copy of it.
    /// </remarks>
    internal static string ShouldHaveSingleViolationOnMember(
        this string checkJson, string ruleId, string kind, string slot, string member)
    {
        JsonElement red = RuleOrFail(checkJson, ruleId);
        string report = Describe(red);
        JsonElement violation = SoleViolationOrFail(red, report);

        violation.ShouldSatisfyAllConditions(
            () => Slot(violation, "kind")
                .ShouldBe(kind, report),
            () => Slot(violation, slot)
                .ShouldBe(member, report));

        return checkJson;
    }

    /// <summary>
    ///     Asserts <paramref name="ruleId" /> is red on exactly one violation whose
    ///     <paramref name="identity" /> slot carries the named value, occurring at <paramref name="sites" />
    ///     sites — the claim a rule with several violations needs, where the endpoint scopes which one is
    ///     under test and the site count is what the edit moved.
    /// </summary>
    internal static string ShouldHaveViolationAtSites(
        this string checkJson, string ruleId, (string Slot, string Value) identity, int sites)
    {
        JsonElement red = RuleOrFail(checkJson, ruleId);
        string report = Describe(red);
        red.GetProperty("status")
            .GetString()
            .ShouldBe("failed", report);

        JsonElement violation = red.GetProperty("violations")
            .EnumerateArray()
            .Where(candidate => Slot(candidate, identity.Slot) == identity.Value)
            .ToList()
            .ShouldHaveSingleItem(report);
        violation.GetProperty("sites")
            .GetArrayLength()
            .ShouldBe(sites, report);

        return checkJson;
    }

    /// <summary>
    ///     The one violation a red rule reported, failing by count when it reported any other number.
    /// </summary>
    /// <remarks>
    ///     The status assertion comes first and is not batched: a passing rule reports an empty array, and
    ///     "0 items" is a worse answer to why the row went red than "the rule passed".
    /// </remarks>
    private static JsonElement SoleViolationOrFail(JsonElement rule, string report)
    {
        rule.GetProperty("status")
            .GetString()
            .ShouldBe("failed", report);

        return rule.GetProperty("violations")
            .EnumerateArray()
            .ToList()
            .ShouldHaveSingleItem(report);
    }

    /// <summary>
    ///     One rule's object, failing by name when the report does not carry it.
    /// </summary>
    /// <remarks>
    ///     <see cref="CheckJson.Rule(JsonDocument, string)" /> selects with <c>Single</c>, whose miss is an
    ///     <see cref="InvalidOperationException" /> naming neither the rule asked for nor the ones present
    ///     — and "the rule is absent" is exactly the regression these assertions exist to catch.
    /// </remarks>
    private static JsonElement RuleOrFail(string checkJson, string ruleId)
    {
        using JsonDocument document = ParseOrFail(checkJson);

        try
        {
            return CheckJson.Rule(document, ruleId);
        }
        catch (InvalidOperationException)
        {
            IEnumerable<string?> present = document.RootElement.GetProperty("rules")
                .EnumerateArray()
                .Select(rule => rule.GetProperty("id")
                    .GetString());

            throw new ShouldAssertException(
                $"the report carries no rule '{ruleId}'. It reported: {string.Join(", ", present)}");
        }
    }

    /// <summary>The document, or a red naming what arrived instead. The caller owns it.</summary>
    /// <remarks>
    ///     A truncated or non-JSON payload is a regression in its own right, and a raw
    ///     <see cref="JsonException" /> reports it as an error rather than a failure, with none of the text
    ///     that would say what was written. <c>CliResultAssertions.ShouldHaveJsonStdout</c> holds this for a
    ///     CLI run's stdout; here it also covers the MCP tool text, which nothing else parses.
    /// </remarks>
    private static JsonDocument ParseOrFail(string checkJson)
    {
        try
        {
            return JsonDocument.Parse(checkJson);
        }
        catch (JsonException exception)
        {
            throw new ShouldAssertException(
                $"the check report should have been one JSON document but did not parse: {exception.Message}"
                + Environment.NewLine + checkJson);
        }
    }

    /// <summary>One optional string slot, or null when the document does not carry it.</summary>
    /// <remarks>
    ///     Every null slot is omitted from the wire document rather than written as null, so a slot the
    ///     violation's kind does not fill has to read as "does not match" — a
    ///     <see cref="KeyNotFoundException" /> would abandon the batch instead of failing one condition in it.
    /// </remarks>
    private static string? Slot(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement slot) ? slot.GetString() : null;
    }

    /// <summary>The rule's whole JSON object — its status, every violation and its baseline counts.</summary>
    private static string Describe(JsonElement rule)
    {
        string rendered = JsonSerializer.Serialize(rule, new JsonSerializerOptions { WriteIndented = true });
        return $"The rule reported:{Environment.NewLine}{rendered}";
    }
}
