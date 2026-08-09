using System.Text.Json;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Readers over a check verb's <c>--json</c> document — the product's documented wire format, so a test
///     asking what a rule reported reads it exactly as a consumer would.
/// </summary>
internal static class CheckJson
{
    /// <summary>One rule's object, cloned so it outlives the parse.</summary>
    internal static JsonElement Rule(string checkJson, string ruleId)
    {
        using JsonDocument document = JsonDocument.Parse(checkJson);
        return document.RootElement.GetProperty("rules").EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == ruleId)
            .Clone();
    }

    /// <summary>One rule's violations, in report order, each cloned so it outlives the parse.</summary>
    internal static IReadOnlyList<JsonElement> Violations(string checkJson, string ruleId)
    {
        return Rule(checkJson, ruleId).GetProperty("violations").EnumerateArray()
            .Select(violation => violation.Clone())
            .ToList();
    }
}
