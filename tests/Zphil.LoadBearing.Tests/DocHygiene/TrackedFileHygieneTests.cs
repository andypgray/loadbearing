using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The hygiene gates over everything this repository publishes, rather than over a list of named
///     files. Two arms, split by where prose can hide: a C# source is read through the comment mask,
///     because only its comments carry prose a person wrote; every other text file is read whole,
///     because a settings file, a workflow or an ignore rule has no construct that separates prose
///     from data. Both arms run the internal working references and the shapes of a private
///     development environment; the source arm runs a third catalog over a text with the suppression
///     directives blanked out, for prose that narrates the development tooling to a stranger.
/// </summary>
/// <remarks>
///     <para>
///         Several facts guard the enumerator itself. An enumerator that degraded to an empty result
///         would turn every arm green over nothing, and a gate that passes because it read no files is
///         worse than no gate — so the inventory is required to reach each source root, its paths are
///         pinned to the character set that needs no quoting, and every exemption is required to still
///         match a hit.
///     </para>
///     <para>
///         The third catalog runs over sources only, and the limit is worth stating rather than
///         papering over. A settings file has to describe itself — which entry it carries, and why that
///         value rather than another — so sweeping the catalog across the non-source set would red
///         exactly the prose that makes those files readable. Narration growing inside one of them is
///         caught at review rather than here.
///     </para>
/// </remarks>
public sealed class TrackedFileHygieneTests
{
    /// <summary>
    ///     The only text a gate arm forgives, as (path, token) pairs rather than whole files: exempting
    ///     a file would forgive every future token in it, which is the granularity these gates exist to
    ///     beat.
    /// </summary>
    /// <remarks>
    ///     The ignore rule that keeps the working-notes directory out of this repository is the reason
    ///     its name is banned everywhere else, so it cannot also be banned there. The prompt file gives
    ///     an adopting reader advice about where design material lives in <em>their</em> repository, in
    ///     a model-facing voice that is off limits for restyling.
    /// </remarks>
    private static readonly (string Path, string Token)[] NonSourceExemptions =
    [
        (".gitignore", "docs/"),
        ("src/Zphil.LoadBearing.Cli/Mcp/Prompts/derive-spec.md", "docs/")
    ];

    /// <summary>
    ///     The tooling mentions the source arm forgives, on the same (path, token) granularity as the
    ///     pairs above.
    /// </summary>
    /// <remarks>
    ///     All three are one category: an instruction protecting the file it sits in from a reformatting
    ///     or member-moving pass, addressed to whoever edits that file next. The sample spec explains the
    ///     marker that holds the formatter off itself; the assertion shadows explain why their qualified
    ///     static calls have to survive one; the validation fixtures warn that their line numbers are
    ///     quoted verbatim elsewhere. None of them narrates a workflow to a stranger — each is the reason
    ///     its own file still reads the way it does.
    /// </remarks>
    private static readonly (string Path, string Token)[] SourceExemptions =
    [
        ("tests/Zphil.LoadBearing.Tests/CanonicalSampleSpec.cs", "ReSharper"),
        ("tests/Zphil.LoadBearing.Tests/ShouldlyExtensions.cs", "ReSharper"),
        ("tests/Zphil.LoadBearing.Tests/SpecValidationSpecs.cs", "cleanup profile")
    ];

    private static readonly Regex[] EnvironmentPatterns =
        [.. DocHygieneTests.PrivateEnvironmentPatterns, .. LocalPrivatePatterns.Patterns];

    private static readonly Regex PlainPath = new("^[A-Za-z0-9._/-]+$");

    // Read and mask once for every arm that scans them, and do it across cores. Masking is the
    // expensive half of this class by an order of magnitude — it parses every tracked source — so
    // letting each arm repeat it would multiply the suite's slowest gate by the number of catalogs.
    // The trailing sort restores a deterministic order, so a failure lists its findings the same way
    // twice running.
    private static readonly Lazy<(string Path, string Text)[]> LazyMaskedSources =
        new(() => ReadAll(TrackedFiles.CSharp, CommentText.Mask));

    // The suppression directives are the one comment form that names a tool by design, and the tree
    // carries some forty of them. Blanking them out of the already-masked text is what lets the third
    // catalog read prose alone — and deriving a second set here, rather than changing the mask, keeps
    // the two arms above reading exactly the text they read before. The regex pass is cheap next to the
    // parse that produced the text.
    private static readonly Lazy<(string Path, string Text)[]> LazyDirectiveFreeSources =
        new(() => LazyMaskedSources.Value
            .Select(static entry => (entry.Path, Text: CommentText.WithoutSuppressionDirectives(entry.Text)))
            .ToArray());

    private static readonly Lazy<(string Path, string Text)[]> LazyNonSourceTexts =
        new(() => ReadAll(TrackedFiles.NonSourceText, static text => text));

    [Fact]
    public void TrackedSource_CommentsNameNoInternalWorkingReferences()
    {
        // Act
        List<string> findings = ScanSources(DocHygieneTests.InternalReferencePatterns);

        // Assert
        findings.ShouldReportNothing("Comments name internal working references");
    }

    [Fact]
    public void TrackedSource_CommentsNameNoPrivateEnvironment()
    {
        // Act
        List<string> findings = ScanSources(EnvironmentPatterns);

        // Assert
        findings.ShouldReportNothing("Comments name a private development environment");
    }

    [Fact]
    public void TrackedSource_CommentsNarrateNoDevTooling()
    {
        // Act
        List<string> findings = ScanSourcesWithoutDirectives(DocHygieneTests.DevToolingPatterns);

        // Assert
        findings.ShouldReportNothing("Comments narrate the development tooling");
    }

    [Fact]
    public void TrackedText_NamesNoInternalWorkingReferences()
    {
        // Act
        List<string> findings = ScanNonSource(DocHygieneTests.InternalReferencePatterns);

        // Assert
        findings.ShouldReportNothing("Published text names internal working references");
    }

    [Fact]
    public void TrackedText_NamesNoPrivateEnvironment()
    {
        // Act
        List<string> findings = ScanNonSource(EnvironmentPatterns);

        // Assert
        findings.ShouldReportNothing("Published text names a private development environment");
    }

    [Fact]
    public void TrackedInventory_ReachesEverySourceRoot()
    {
        // Assert: not a scope list — git decides scope. This is the proof that the enumerator
        // returned something real, so a green arm above means "scanned and clean" and never
        // "scanned nothing".
        TrackedFiles.All.ShouldNotBeEmpty();

        foreach (string root in new[] { "src/", "tests/", "examples/", "arch/" })
            TrackedFiles.CSharp.ShouldContain(
                path => path.StartsWith(root, StringComparison.Ordinal),
                $"The tracked source set reaches nothing under {root}.");

        TrackedFiles.NonSourceText.ShouldContain(".gitignore");
        TrackedFiles.NonSourceText.ShouldContain("README.md");
    }

    [Fact]
    public void TrackedPaths_NeedNoQuoting()
    {
        // Act
        List<string> quoted = TrackedFiles.All.Where(static path => !PlainPath.IsMatch(path))
            .ToList();

        // Assert: the inventory is read from line-oriented output, so a path git would quote or one
        // holding a newline would come back mangled. Pinning the character set means the day such a
        // path appears the gate says so, rather than silently dropping the file.
        quoted.ShouldReportNothing("Tracked path(s) need quoting, so the inventory cannot be read line by line");
    }

    [Fact]
    public void NonSourceExemptions_AllStillMatchAHit()
    {
        // Arrange
        Regex[] everyPattern = [.. DocHygieneTests.InternalReferencePatterns, .. EnvironmentPatterns];
        List<string> dead = new();

        // Act
        foreach ((string path, string token) in NonSourceExemptions)
        {
            string absolute = RepoRoot.Absolute(path);
            if (!File.Exists(absolute))
            {
                dead.Add($"{path} no longer exists.");
                continue;
            }

            IReadOnlyList<string> hits = DocProse.FindForbidden(File.ReadAllText(absolute), everyPattern);
            if (!hits.Any(hit => IsToken(hit, token))) dead.Add($"{path} no longer carries '{token}'.");
        }

        // Assert: an exemption whose hit has gone forgives nothing and hides the next one.
        dead.ShouldReportNothing("These exemptions no longer match anything and should be removed");
    }

    [Fact]
    public void SourceExemptions_AllStillMatchAHit()
    {
        // Arrange: read from the very text the arm scans rather than from the file on disk, so an
        // exemption whose token survives only in code or in a string literal counts as dead.
        Dictionary<string, string> byPath = LazyDirectiveFreeSources.Value
            .ToDictionary(static entry => entry.Path, static entry => entry.Text, StringComparer.Ordinal);
        List<string> dead = new();

        // Act
        foreach ((string path, string token) in SourceExemptions)
        {
            if (!byPath.TryGetValue(path, out string? text))
            {
                dead.Add($"{path} is no longer a tracked source.");
                continue;
            }

            IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.DevToolingPatterns);
            if (!hits.Any(hit => IsToken(hit, token))) dead.Add($"{path} no longer carries '{token}'.");
        }

        // Assert: same reason as the arm above — a stale exemption forgives nothing and hides the
        // next mention to land in that file.
        dead.ShouldReportNothing("These exemptions no longer match anything and should be removed");
    }

    private static (string Path, string Text)[] ReadAll(
        IEnumerable<string> paths, Func<string, string> prepare)
    {
        return paths
            .AsParallel()
            .Select(path => (Path: path, Text: prepare(RepoRoot.ReadText(path))))
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
    }

    // Comments as the mask leaves them, and nothing forgiven: a token either reference catalog finds
    // in a comment has no benign reading, so there is no exemption to pass.
    private static List<string> ScanSources(Regex[] patterns)
    {
        return Scan(LazyMaskedSources.Value, patterns, []);
    }

    private static List<string> ScanSourcesWithoutDirectives(Regex[] patterns)
    {
        return Scan(LazyDirectiveFreeSources.Value, patterns, SourceExemptions);
    }

    private static List<string> ScanNonSource(Regex[] patterns)
    {
        return Scan(LazyNonSourceTexts.Value, patterns, NonSourceExemptions);
    }

    private static List<string> Scan(
        (string Path, string Text)[] texts, Regex[] patterns, (string Path, string Token)[] exemptions)
    {
        List<string> findings = new();
        foreach ((string path, string text) in texts)
        foreach (string hit in DocProse.FindForbidden(text, patterns))
            if (!IsExempt(exemptions, path, hit))
                findings.Add($"{path}:{hit}");

        return findings;
    }

    private static bool IsExempt((string Path, string Token)[] exemptions, string path, string hit)
    {
        return exemptions.Any(exemption => exemption.Path == path && IsToken(hit, exemption.Token));
    }

    // FindForbidden formats a hit as "{line}: {matchedText}", so the matched token is what follows
    // the separator — which is what makes an exemption forgive one token rather than one file.
    private static bool IsToken(string hit, string token)
    {
        return hit.EndsWith($": {token}", StringComparison.Ordinal);
    }
}
