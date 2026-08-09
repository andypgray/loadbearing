using System.Collections.Frozen;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The last line of defence on a tool response's character count, so a large result cannot exhaust the
///     client's context window. It is deliberately a backstop and not the answer: a document cut at a line
///     boundary is corrupt JSON, which evicts a client from the tool rather than narrowing what it reads.
///     When truncation does fire, the footer says how much was dropped and — for the tools that have a
///     narrowing knob — names the knobs that would have avoided it.
///     <para>
///         <c>arch_graph</c> degrades itself down a grain ladder against this same budget before reaching
///         here, so on any solution whose skeleton fits it never arrives. That is a ladder with a last rung,
///         not a guarantee: a survey still over budget at its coarsest grain lands here and is cut like any
///         other response. Reaching that point is the signal to narrow the <em>subject</em> — the knob the
///         footer names — because no grain left will help.
///     </para>
/// </summary>
internal static class ResponseTruncator
{
    private const int DefaultMaxChars = 62_500;
    private const double CharsPerToken = 2.5;

    // Keyed on the neutral name constants, never on the tool class: this stage runs on the startup path and
    // must not pull the MSBuildLocator-quarantined tool type into it. arch_status, arch_explain and
    // arch_context are absent on purpose — they have no knob, so a hint would be noise at the exact moment a
    // reader is looking for something to do.
    private static readonly FrozenDictionary<string, string> NarrowingHints = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Deliberately not a grain hint. arch_graph coarsens its own grain against this same budget before a
        // response can reach here, so a survey that still overruns has already been through the ladder and
        // overview: true would only name a rung it took. What is left is the subject.
        [ArchToolNames.Graph] =
            "Narrow the subject rather than read half a survey: the grain ladder is already exhausted, so "
            + "projects: \"<name globs>\" surveys part of the solution and is the knob left. On the CLI, "
            + "loadbearing graph --projects <globs> --json, or redirect loadbearing graph --json to a file "
            + "and slice it there.",
        [ArchToolNames.Check] =
            "Narrow the call rather than read half a report: rules: \"<rule-id globs>\" checks a subset, and "
            + "arch_explain returns one rule whole. For the report entire, redirect "
            + "loadbearing check --json to a file and slice it there."
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    ///     Resolves the character cap from the MCP client's
    ///     <see cref="Zphil.LoadBearing.Roslyn.LoadBearingEnvVars.MaxMcpOutputTokens" /> budget
    ///     (× 2.5 chars/token), falling back to 62,500 — the same multiple of a 25,000-token default — when
    ///     the value is unset, blank, or non-positive.
    /// </summary>
    internal static int ComputeMaxChars(string? maxMcpOutputTokens)
    {
        if (int.TryParse(maxMcpOutputTokens, out int tokens) && tokens > 0) return (int)(tokens * CharsPerToken);

        return DefaultMaxChars;
    }

    /// <summary>
    ///     Returns <paramref name="text" /> unchanged when it fits within <paramref name="maxChars" />;
    ///     otherwise returns a truncated copy with a "RESPONSE TRUNCATED" footer. When
    ///     <paramref name="toolName" /> is one of the tools that can be narrowed, the footer ends with the
    ///     hint naming its knobs and their CLI twins.
    /// </summary>
    public static string TruncateIfNeeded(string text, string? toolName, int maxChars)
    {
        if (text.Length <= maxChars) return text;

        int cutPoint = text.LastIndexOf('\n', maxChars - 1);
        if (cutPoint <= 0) cutPoint = maxChars;

        // Never cut between the high and low half of a UTF-16 surrogate pair (that strands a lone surrogate —
        // a broken astral char, e.g. an emoji). When the character kept just before the cut is a high
        // surrogate and the one dropped at the cut is its low surrogate, step back one so the pair stays whole.
        if (cutPoint > 0 && cutPoint < text.Length
                         && char.IsHighSurrogate(text[cutPoint - 1]) && char.IsLowSurrogate(text[cutPoint]))
            cutPoint--;

        string truncated = text[..cutPoint];
        int droppedChars = text.Length - cutPoint;
        string hint = Hint(toolName);

        return $"{truncated}\n\n--- RESPONSE TRUNCATED ---\nOutput was {text.Length:N0} characters, limit is {maxChars:N0} ({droppedChars:N0} characters omitted).\nThe results above are incomplete.{hint}";
    }

    private static string Hint(string? toolName)
    {
        if (toolName is null) return string.Empty;

        return NarrowingHints.TryGetValue(toolName, out string? hint) ? $"\n{hint}" : string.Empty;
    }
}
