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
///     from data. Each arm runs both catalogs — the internal working references, and the shapes of a
///     private development environment.
/// </summary>
/// <remarks>
///     Three facts guard the enumerator itself. An enumerator that degraded to an empty result would
///     turn every arm green over nothing, and a gate that passes because it read no files is worse
///     than no gate — so the inventory is required to reach each source root, its paths are pinned to
///     the character set that needs no quoting, and every exemption is required to still match a hit.
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

    private static readonly Regex[] EnvironmentPatterns =
        [..DocHygieneTests.PrivateEnvironmentPatterns, ..LocalPrivatePatterns.Patterns];

    private static readonly Regex PlainPath = new("^[A-Za-z0-9._/-]+$");

    // Read and mask once for every arm that scans them, and do it across cores. Masking is the
    // expensive half of this class by an order of magnitude — it parses every tracked source — so
    // letting each arm repeat it would multiply the suite's slowest gate by the number of catalogs.
    // The trailing sort restores a deterministic order, so a failure lists its findings the same way
    // twice running.
    private static readonly Lazy<(string Path, string Text)[]> LazyMaskedSources =
        new(() => ReadAll(TrackedFiles.CSharp, CommentText.Mask));

    private static readonly Lazy<(string Path, string Text)[]> LazyNonSourceTexts =
        new(() => ReadAll(TrackedFiles.NonSourceText, static text => text));

    [Fact]
    public void TrackedSource_CommentsNameNoInternalWorkingReferences()
    {
        // Act
        List<string> findings = ScanSources(DocHygieneTests.InternalReferencePatterns);

        // Assert
        findings.ShouldBeEmpty(
            $"Comments name internal working references:\n{string.Join("\n", findings)}");
    }

    [Fact]
    public void TrackedSource_CommentsNameNoPrivateEnvironment()
    {
        // Act
        List<string> findings = ScanSources(EnvironmentPatterns);

        // Assert
        findings.ShouldBeEmpty(
            $"Comments name a private development environment:\n{string.Join("\n", findings)}");
    }

    [Fact]
    public void TrackedText_NamesNoInternalWorkingReferences()
    {
        // Act
        List<string> findings = ScanNonSource(DocHygieneTests.InternalReferencePatterns);

        // Assert
        findings.ShouldBeEmpty(
            $"Published text names internal working references:\n{string.Join("\n", findings)}");
    }

    [Fact]
    public void TrackedText_NamesNoPrivateEnvironment()
    {
        // Act
        List<string> findings = ScanNonSource(EnvironmentPatterns);

        // Assert
        findings.ShouldBeEmpty(
            $"Published text names a private development environment:\n{string.Join("\n", findings)}");
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
        quoted.ShouldBeEmpty(
            $"Tracked path(s) need quoting, so the inventory cannot be read line by line:\n{string.Join("\n", quoted)}");
    }

    [Fact]
    public void NonSourceExemptions_AllStillMatchAHit()
    {
        // Arrange
        Regex[] everyPattern = [..DocHygieneTests.InternalReferencePatterns, ..EnvironmentPatterns];
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
        dead.ShouldBeEmpty($"These exemptions no longer match anything and should be removed:\n{string.Join("\n", dead)}");
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

    private static List<string> ScanSources(Regex[] patterns)
    {
        List<string> findings = new();
        foreach ((string path, string masked) in LazyMaskedSources.Value)
        foreach (string hit in DocProse.FindForbidden(masked, patterns))
            findings.Add($"{path}:{hit}");

        return findings;
    }

    private static List<string> ScanNonSource(Regex[] patterns)
    {
        List<string> findings = new();
        foreach ((string path, string text) in LazyNonSourceTexts.Value)
        foreach (string hit in DocProse.FindForbidden(text, patterns))
            if (!IsExempt(path, hit))
                findings.Add($"{path}:{hit}");

        return findings;
    }

    private static bool IsExempt(string path, string hit)
    {
        return NonSourceExemptions.Any(exemption => exemption.Path == path && IsToken(hit, exemption.Token));
    }

    // FindForbidden formats a hit as "{line}: {matchedText}", so the matched token is what follows
    // the separator — which is what makes an exemption forgive one token rather than one file.
    private static bool IsToken(string hit, string token)
    {
        return hit.EndsWith($": {token}", StringComparison.Ordinal);
    }
}
