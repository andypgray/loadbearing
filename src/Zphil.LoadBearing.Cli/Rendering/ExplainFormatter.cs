namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats one rule as the <c>explain</c> field dump (R4): a <c>&lt;id&gt; (&lt;posture&gt;)</c>
///     header — the posture carries the Freeze role, e.g. <c>(freeze/containment)</c> — then each
///     present field once under check's lowercase-label style (<c>sentence:</c> / <c>because:</c> /
///     <c>fix:</c>) plus the posture payload. This is a data dump, not the Phase 5/6 voice templates:
///     <c>dragons:</c> and <c>from:</c> print verbatim, and <c>dragons-doc:</c> prints the linked path
///     only (the spec stays the index — DESIGN.md §6). Emitted line by line to match the check renderer.
/// </summary>
internal static class ExplainFormatter
{
    public static IReadOnlyList<string> Lines(ArchRule rule)
    {
        var lines = new List<string> { Header(rule) };

        if (rule.Sentence.Length > 0) lines.Add($"  sentence: {rule.Sentence}");
        lines.Add($"  because: {rule.Because}");
        if (rule.Fix is { } fix) lines.Add($"  fix: {fix}");

        switch (rule.Posture)
        {
            case Posture.Migrate when rule.Migrate is { } migrate:
                lines.Add($"  from: {migrate.From}");
                lines.Add($"  policy: {migrate.Policy}");
                lines.Add($"  baseline: {migrate.BaselinePath}"); // never null post-build (GRAMMAR §4.4)
                break;
            case Posture.Freeze when rule.Freeze is { } freeze:
                lines.Add($"  scope: {freeze.ScopeId}");
                if (freeze.Boundary.Count > 0) lines.Add($"  boundary: {BoundaryList(freeze.Boundary)}");
                if (freeze.BaselinePath is { } freezeBaseline) lines.Add($"  baseline: {freezeBaseline}");
                if (freeze.Dragons is { } dragons) lines.Add($"  dragons: {dragons}");
                if (freeze.DragonsDoc is { } dragonsDoc) lines.Add($"  dragons-doc: {dragonsDoc}");
                break;
        }

        return lines;
    }

    private static string Header(ArchRule rule)
    {
        string posture = rule.Posture switch
        {
            Posture.Freeze when rule.Freeze is { } freeze => $"freeze/{freeze.Role.ToString().ToLowerInvariant()}",
            _ => rule.Posture.ToString().ToLowerInvariant()
        };
        return $"{rule.Id} ({posture})";
    }

    // Simple type names, backticked and comma-joined. Uses Type.Name (the CLI cannot reach Core's
    // internal prose helpers); boundary facades are non-generic, so this matches the scope card's list.
    private static string BoundaryList(IReadOnlyList<Type> boundary)
    {
        return string.Join(", ", boundary.Select(type => $"`{type.Name}`"));
    }
}