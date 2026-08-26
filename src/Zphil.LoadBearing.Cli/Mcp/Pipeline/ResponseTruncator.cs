using System.Collections.Frozen;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The last line of defence on a tool response's character count, so a large result cannot exhaust the
///     client's context window.
/// </summary>
/// <remarks>
///     <para>
///         It is deliberately a backstop and not the answer: a document cut at a line boundary is corrupt
///         JSON, which evicts a client from the tool rather than narrowing what it reads. When truncation
///         does fire, the footer says how much was dropped and — for the tools that have a narrowing knob —
///         names the knobs that would have avoided it.
///     </para>
///     <para>
///         <c>arch_graph</c> and <c>arch_check</c> both degrade down a grain ladder against this same budget
///         before reaching here, and that ladder's floor is the roster: every project name, every rule id,
///         and nothing that scales with the codebase. So neither tool arrives here on any realistic
///         solution — not because the floor is guaranteed to fit, which no rung can be against a budget a
///         client sets, but because what is left there grows with the authored and structural dimension
///         while a channel budget does not shrink with it.
///     </para>
///     <para>
///         What still lands here is a budget too small for any answer at all (the suites set 10 tokens), and
///         it is cut like any other response. The footer names the <em>subject</em> knob because grain is
///         genuinely spent by then; it does not offer to page the document out through the CLI, which the
///         served instructions forbid in as many words — a hint at the point of failure outweighs a sentence
///         in a system prompt, so the two must not disagree.
///     </para>
/// </remarks>
internal static class ResponseTruncator
{
    private const int DefaultMaxChars = 62_500;
    private const double CharsPerToken = 2.5;

    // Keyed on the neutral name constants, never on the tool class: this stage runs on the startup path and
    // must not pull the MSBuildLocator-quarantined tool type into it. arch_status, arch_explain and
    // arch_context are absent on purpose — they have no knob, so a hint would be noise at the exact moment a
    // reader is looking for something to do.
    //
    // Neither hint names a grain. Both tools coarsen their own grain against this same budget before a
    // response can reach here, so anything that still overruns has already been through the ladder and
    // overview: true would only name a rung it took. What is left in each case is the subject.
    //
    // Neither offers to redirect the CLI to a file either, though both once did. A field test caught an agent
    // quoting that clause back as its reason for abandoning the tool surface and paging the whole document
    // through the shell — the exact move the served instructions call out by name — so the trailer was the
    // product contradicting itself, and text at the moment of failure is what a reader acts on.
    private static readonly FrozenDictionary<string, string> NarrowingHints = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ArchToolNames.Graph] =
            "Narrow the subject rather than read half a survey: the grain ladder is already exhausted, so "
            + "projects: \"<name globs>\" surveys part of the solution and is the knob left. On the CLI, "
            + "loadbearing graph --projects <globs> --json.",
        [ArchToolNames.Check] =
            "Narrow the subject rather than read half a report: the grain ladder is already exhausted, so "
            + "rules: \"<rule-id globs>\" checks part of the spec and is the knob left — and arch_explain "
            + "returns one rule whole. On the CLI, loadbearing check --rules <globs> --json."
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    ///     Resolves the character cap from the MCP client's
    ///     <see cref="Zphil.LoadBearing.Roslyn.Hosting.LoadBearingEnvVars.MaxMcpOutputTokens" /> budget
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
