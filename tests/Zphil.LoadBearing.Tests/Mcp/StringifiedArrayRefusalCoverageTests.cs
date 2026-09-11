using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.DocHygiene;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The gate over which bound string parameters refuse a JSON array a client serialized into them.
///     Every one of them does, with no recorded exception yet — and the guarded set is derived from the
///     live tool surface rather than listed, so the only thing kept by hand is the exemption ledger.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this is an inventory rather than a pin on one path.</b> The refusal is wired per verb, by
///         hand, at five call sites, and four of them arrived one commit at a time. A fact that only said
///         "arch_explain refuses a bracketed rule ID" would say nothing about the next string parameter to
///         land, and the failure it allows is quiet: the client's array binds as a legitimate-looking token,
///         reaches the runner intact, and comes back either as plumbing from whatever consumed it or — worse
///         — as a well-formed negative answer to a question nobody asked. Phrased as "every string parameter
///         refuses the shape", a new one reds on the day it ships.
///     </para>
///     <para>
///         <b>What it proves, and what it deliberately leaves alone.</b> It proves the refusal by driving
///         it: a real <c>tools/call</c> carrying one bracketed probe, and the clause every refusal shares in
///         the result. It does not pin any refusal's wording, or where inside a verb the guard sits — those
///         are <see cref="CheckCommandE2ETests" />, <see cref="GraphCommandTests" />,
///         <see cref="ExplainCommandTests" />, <see cref="ContextRunnerTests" /> and
///         <see cref="GlobalCallToolFilterTests" />, and naming them here is what keeps this gate from
///         restating them badly. Recognition stays per verb for the reason
///         <see cref="Zphil.LoadBearing.Cli.Rendering.Refusals" /> records: each surface answers on its own
///         no-match path with its own advice, and none of that wording may name a flag or a parameter.
///     </para>
///     <para>
///         <b>Where it does not reach.</b> The tool surface only:
///         <see cref="ToolAttributeDiscovery" /> walks <c>[McpServerToolType]</c>, and a prompt binds from a
///         <c>prompts/get</c> envelope rather than a <c>tools/call</c> one. That costs nothing today because
///         <c>derive_spec</c> declares no parameters at all, and a gate over an empty surface would be a
///         claim about nothing — but a prompt that grows a string parameter grows it unwatched, and this is
///         the sentence that should send whoever adds one back here.
///     </para>
///     <para>
///         <b>Why the description arm quotes its sentence instead of sharing a const.</b> These descriptions
///         are prose written for a model and woven differently into each parameter; a shared fragment would
///         turn them into assembled strings. The property worth holding is that they say it in the same
///         words, so a reworded description is meant to meet this assertion deliberately, in the commit that
///         rewords it, where a reviewer sees both.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class StringifiedArrayRefusalCoverageTests
{
    // Two elements deliberately. It matches no rule glob, no project glob and no rule ID, so every tool
    // reaches its refusal instead of an answer; and SingleValueAdvice takes its no-echo branch, so one
    // literal serves every parameter and the assertion lands on the clause they all share. A real
    // one-element JSON array never gets this far (StringCoercerFactory unwraps it) — a string that merely
    // looks like one survives untouched, which is the value under test.
    private const string StringifiedArray = """["a","b"]""";

    private const string SharedClause = "That is a JSON array written as text";

    private const string ShapeSentence = "as one string, not an array";

    /// <summary>
    ///     Bound string parameters whose legitimate values may open with <c>[</c> and close with <c>]</c>,
    ///     keyed <c>tool.parameter</c>, each with the reason it cannot take the refusal. Empty today, and the
    ///     occupant it waits for is a real shape rather than a speculative one:
    ///     <c>Refusals.StringifiedArrayElements</c> reads any bracketed value as an array, so a future regex
    ///     or character-class parameter (<c>[a-z]</c>) would belong here rather than be refused.
    /// </summary>
    /// <remarks>
    ///     The tool half of a key is compile-checked through <see cref="ArchToolNames" />; the parameter half
    ///     cannot be, because C# has no <c>nameof</c> for another method's parameter. That is what
    ///     <see cref="ExemptionLedger_NamesOnlyLiveStringParameters" /> is for — a renamed parameter orphans
    ///     its entry and reds, instead of leaving it to rot while reading as though it still exempts
    ///     something.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> ValuesMayOpenWithABracket =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public async Task EveryJsonBoundStringParameter_RefusesAStringifiedArray()
    {
        // One harness for every parameter: the server holds the workspace warm, so only the first call that
        // needs one pays for it.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        List<string> accepted = [];
        List<string> driven = [];

        foreach ((string tool, ParameterInfo parameter) in LiveStringParameters())
        {
            string where = Key(tool, parameter);
            if (ValuesMayOpenWithABracket.ContainsKey(where)) continue;

            driven.Add(where);
            CallToolResult result = await harness.Client.CallToolAsync(
                tool,
                new Dictionary<string, object?> { [parameter.Name!] = StringifiedArray },
                cancellationToken: Ct);

            string text = result.ShouldHaveTextContent();
            bool named = result.IsError == true && text.Contains(SharedClause, StringComparison.Ordinal);
            if (!named) accepted.Add($"{where} answered: {text}");
        }

        accepted.ShouldReportNothing(
            "String parameter(s) that did not name the shape when a JSON array was written into them — wire "
            + "the refusal through Refusals.StringifiedArrayRefusal beside the verb's own no-match answer, "
            + "or record the parameter in the exemption ledger with the reason its values may legitimately "
            + "open with a bracket");
        // Otherwise a reflection slip that found no parameters at all would read as a pass.
        driven.ShouldNotBeEmpty("the live arch_* tools declare no JSON-bound string parameters to drive");
    }

    [Fact]
    public void EveryJsonBoundStringParameter_NamesTheShapeInItsDescription()
    {
        // The preventive half, and the only half reflection can see. Both directions, because a description
        // that disagrees with the behaviour is wrong whichever way it disagrees.
        List<string> disagreements = [];
        List<string> checkedParameters = [];

        foreach ((string tool, ParameterInfo parameter) in LiveStringParameters())
        {
            string where = Key(tool, parameter);
            checkedParameters.Add(where);

            string description = parameter.GetCustomAttribute<DescriptionAttribute>()
                ?.Description ?? "";
            bool warns = description.Contains(ShapeSentence, StringComparison.Ordinal);
            bool exempt = ValuesMayOpenWithABracket.ContainsKey(where);

            if (!exempt && !warns)
                disagreements.Add(
                    $"{where} is not exempt, so it refuses a stringified array — but its description "
                    + "never warns of one");
            if (exempt && warns)
                disagreements.Add($"{where} is exempt, but its description promises a refusal it will not give");
        }

        disagreements.ShouldReportNothing(
            "Schema description(s) that disagree with what the parameter does when a JSON array is written "
            + "into it — the description is the only half of this a client reads before making the call");
        checkedParameters.ShouldNotBeEmpty("the live arch_* tools declare no JSON-bound string parameters");
    }

    [Fact]
    public void ExemptionLedger_NamesOnlyLiveStringParameters()
    {
        HashSet<string> live = LiveStringParameters()
            .Select(parameter => Key(parameter.Tool, parameter.Parameter))
            .ToHashSet(StringComparer.Ordinal);

        List<string> orphans = ValuesMayOpenWithABracket.Keys
            .Where(key => !live.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        orphans.ShouldReportNothing(
            "Exemption ledger key(s) naming no live bound string parameter — a renamed or removed parameter "
            + "leaves its entry behind, where it exempts nothing while reading as though it still does");
    }

    /// <summary>
    ///     Every parameter the server binds from a <c>tools/call</c> argument object whose type is exactly
    ///     <see cref="string" />, as the tool's wire name plus the parameter. Exactly <c>string</c> rather
    ///     than assignable to it: a future <c>string[]</c> parameter is legitimately array-shaped and has
    ///     nothing to refuse.
    /// </summary>
    /// <remarks>
    ///     Derived here rather than shared with <see cref="CoercerCoverageTests" />, which walks the same
    ///     surface for a different projection — it unwraps <see cref="Nullable{T}" /> and keeps only the
    ///     type, where this needs the <see cref="ParameterInfo" /> itself and must not unwrap. The one
    ///     definition both read is <see cref="ToolAttributeDiscovery" />; a shared enumerator beside it would
    ///     be a second one.
    /// </remarks>
    private static IEnumerable<(string Tool, ParameterInfo Parameter)> LiveStringParameters()
    {
        foreach (MethodInfo method in ToolAttributeDiscovery.GetToolMethods())
        {
            // Attribute-gated, not just "public method on a tool type": without this, object's own
            // Equals(object) would count as a live parameter.
            if (method.GetCustomAttribute<McpServerToolAttribute>()
                    ?.Name is not { } toolName) continue;

            IEnumerable<ParameterInfo> bound = method.GetParameters()
                .Where(ToolAttributeDiscovery.IsJsonBoundParameter)
                .Where(parameter => parameter.ParameterType == typeof(string));

            foreach (ParameterInfo parameter in bound) yield return (toolName, parameter);
        }
    }

    private static string Key(string tool, ParameterInfo parameter)
    {
        return $"{tool}.{parameter.Name}";
    }
}
