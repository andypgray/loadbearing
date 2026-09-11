using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats one rule as the <c>explain</c> field dump: a <c>&lt;id&gt; (&lt;posture&gt;)</c>
///     header — a scope posture carries the role, e.g. <c>(quarantine/containment)</c> or
///     <c>(caution/tripwire)</c> — then each
///     present field once under check's lowercase-label style (<c>sentence:</c> / <c>because:</c> /
///     <c>fix:</c>) plus the posture payload. This is a data dump, not the voice templates:
///     <c>dragons:</c> and <c>from:</c> print verbatim, and <c>dragons-doc:</c> prints the linked path
///     only (the spec stays the index). Emitted line by line to match the check renderer.
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
            // One arm for both scope postures: every line here already self-gates on presence, so a caution
            // — no boundary, no baseline — prints the scope and its dragons and nothing else.
            case Posture.Quarantine or Posture.Caution when rule.Scope is { } scope:
                lines.Add($"  scope: {scope.ScopeId}");
                if (scope.Surface.Count > 0) lines.Add($"  boundary: {BoundaryList(scope.Surface)}");
                if (scope.BaselinePath is { } scopeBaseline) lines.Add($"  baseline: {scopeBaseline}");
                if (scope.Dragons is { } dragons) lines.Add($"  dragons: {dragons}");
                if (scope.DragonsDoc is { } dragonsDoc) lines.Add($"  dragons-doc: {dragonsDoc}");
                break;
        }

        return lines;
    }

    private static string Header(ArchRule rule)
    {
        string posture = rule.Posture switch
        {
            Posture.Quarantine or Posture.Caution when rule.Scope is { } scope =>
                $"{rule.Posture.ToString().ToLowerInvariant()}/{scope.Role.ToString().ToLowerInvariant()}",
            _ => rule.Posture.ToString().ToLowerInvariant()
        };
        return $"{rule.Id} ({posture})";
    }

    // The already-rendered surface fragments, comma-joined. Reading the same pre-rendered list the
    // scope card reads is what makes the two agree structurally rather than by coincidence — the CLI
    // cannot reach Core's prose helpers to re-derive them.
    private static string BoundaryList(IReadOnlyList<string> surface)
    {
        return string.Join(", ", surface);
    }
}
