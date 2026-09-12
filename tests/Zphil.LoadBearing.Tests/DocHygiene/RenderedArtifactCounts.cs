using System.Text.RegularExpressions;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The checker the rendered-artifact count gate runs on. Prose that says how much this repository's
///     own render writes is a hand-maintained number with a derivable answer sitting beside it, so this
///     lifts every such claim out of a doc's committed bytes and counts the artifacts git actually
///     carries. All of the logic lives here so the gate and its facts exercise the same code path.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this gate exists.</b> The root <c>AGENTS.md</c> and <c>CONTRIBUTING.md</c> both said
///         "nine committed artifacts … seven directories under <c>src/</c>" until 2026-09-10, while the
///         circular-reference rule had placed two more cards on 2026-09-07 and neither sentence moved.
///         It is the failure <see cref="SpecRuleCounts" /> was written for, one claim over: every other
///         number in those paragraphs is held to something, and this one had nothing behind it — inside
///         the sentences that promise this repository practices what it publishes.
///     </para>
///     <para>
///         <b>The committed set is the authority, not the renderer.</b> Counting what a render would
///         place needs a workspace, a spec build and an extraction; the committed files need none of
///         that, and that the two agree is already proved by the dogfood self-spec tests and by CI
///         re-rendering and requiring a zero diff. So this reads tracked bytes alone and stays cheap,
///         cross-platform and parallel-safe, like every other gate in this folder. The cost it inherits
///         from every <c>git ls-files</c> gate is that a newly rendered card is invisible until someone
///         stages it; CI's unscoped diff is what catches an unstaged one.
///     </para>
///     <para>
///         <b>What counts as an artifact.</b> A tracked markdown file named
///         <see cref="ContextFileComposer.FileName" /> or <c>ARCHITECTURE.md</c> that carries a managed
///         block, outside <c>examples/</c> — those blocks belong to the example solutions' own specs and
///         are gated by the examples job's own zero-diff, the same boundary the dogfood tests draw. The
///         name filter is what keeps a golden fixture whose body <em>is</em> a rendered block out of the
///         count without naming its path here. A card is any of those below the repository root, which is
///         exactly the set the renderer places by layer and by scope.
///     </para>
/// </remarks>
internal static class RenderedArtifactCounts
{
    /// <summary>Which committed set a claim counts.</summary>
    public enum ClaimKind
    {
        /// <summary>Everything the render writes here: the root block, the diagram, and every card.</summary>
        Artifacts,

        /// <summary>The per-directory cards alone.</summary>
        Cards
    }

    /// <summary>The diagram half of the render — the second artifact this repository commits at its root.</summary>
    private const string DiagramFileName = "ARCHITECTURE.md";

    /// <summary>The blocks the example solutions' own specs write, gated by the examples job's zero-diff.</summary>
    private const string ExampleRoot = "examples/";

    /// <summary>
    ///     The one doc whose counts are frozen. A release note states what a past version rendered ("Six
    ///     per-directory cards are now committed where there was one"), and holding that to today's set
    ///     would rewrite history every time a rule places a card. Every other tracked doc is swept, and
    ///     <c>ReleaseHistory_IsSilencedByItsExemption_NotByTheScannerMissingIt</c> pins that this
    ///     exemption, rather than a pattern that stopped matching, is what keeps the file quiet.
    /// </summary>
    private const string ReleaseHistory = "CHANGELOG.md";

    /// <summary>
    ///     A prose claim about how many artifacts the render writes. The number is captured as written —
    ///     a spelled word or a numeral — so the gate compares what the doc says rather than what a parse
    ///     of it rounded to.
    /// </summary>
    private static readonly Regex ArtifactClaim = new(
        $@"\b{GrandfatheredCounts.CountToken} committed artifacts\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     The same claim about the cards alone, in either of the two shapes this repository's prose
    ///     writes it: one sentence counts the files and the other counts the directories holding them.
    /// </summary>
    private static readonly Regex CardClaim = new(
        $@"\b{GrandfatheredCounts.CountToken} (?:per-directory cards|directories under)\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     Every artifact this repository's own render writes and git carries, repository-relative with
    ///     forward slashes.
    /// </summary>
    public static IReadOnlyList<string> CommittedArtifacts()
    {
        return TrackedFiles.Markdown
            .Where(static path => !path.StartsWith(ExampleRoot, StringComparison.Ordinal))
            .Where(static path => IsRenderTarget(Path.GetFileName(path)))
            .Where(CarriesAManagedBlock)
            .ToList();
    }

    /// <summary>
    ///     The per-directory cards among them: every committed context file below the repository root.
    ///     The root block is what sits at the root, so depth is the whole discriminator.
    /// </summary>
    public static IReadOnlyList<string> CommittedCards()
    {
        return CommittedArtifacts()
            .Where(static path => path.Contains('/'))
            .Where(static path => string.Equals(
                Path.GetFileName(path), ContextFileComposer.FileName, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    ///     Every rendered-artifact count claim in <paramref name="docText" />, named by
    ///     <paramref name="doc" />'s repo-relative path.
    /// </summary>
    public static IReadOnlyList<RenderedArtifactCountClaim> Extract(string doc, string docText)
    {
        if (string.Equals(doc, ReleaseHistory, StringComparison.Ordinal)) return [];

        List<RenderedArtifactCountClaim> claims = new();
        string[] lines = docText.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            Collect(claims, doc, index + 1, lines[index], ArtifactClaim, ClaimKind.Artifacts);
            Collect(claims, doc, index + 1, lines[index], CardClaim, ClaimKind.Cards);
        }

        return claims;
    }

    private static void Collect(
        List<RenderedArtifactCountClaim> claims,
        string doc,
        int docLine,
        string line,
        Regex pattern,
        ClaimKind kind)
    {
        foreach (Match match in pattern.Matches(line))
        {
            string written = match.Groups["count"]
                .Value;

            // Ordinary prose puts a determiner where the number goes — "the per-directory cards this
            // example is built around" — and that is a reference rather than a claim. Requiring a spelled
            // count is what tells one from the other, and it is why every doc that merely mentions the
            // cards needs no exemption entry.
            if (!GrandfatheredCounts.IsCountWord(written)) continue;

            claims.Add(new RenderedArtifactCountClaim(doc, docLine, kind, written));
        }
    }

    private static bool IsRenderTarget(string fileName)
    {
        return string.Equals(fileName, ContextFileComposer.FileName, StringComparison.Ordinal)
               || string.Equals(fileName, DiagramFileName, StringComparison.Ordinal);
    }

    private static bool CarriesAManagedBlock(string path)
    {
        return ManagedBlock.ExtractBody(RepoRoot.ReadText(path)) is not null;
    }
}
