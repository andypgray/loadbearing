using System.Xml.Linq;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps the solution <c>.DotSettings</c> well-formed XML, its silent-cleanup
///     default naming a profile the file actually defines, that profile keeping the two cleanup
///     tasks off whose <c>False</c> is load-bearing, and line wrapping declared off with no
///     chain-wrapping key left standing beside it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why well-formedness needs a gate.</b> ReSharper's own settings reader tolerates
///         malformed XML, so jb keeps working — named profile, severities and all — after the file
///         stops being readable by any conformant XML parser. The consumers that break are the
///         strict ones: a runner that resolves the silent-cleanup default with
///         <see cref="XDocument.Load(string)" /> falls back on failure to
///         <c>Built-in: Full Cleanup</c>, which rewrites the deliberate named arguments GRAMMAR §10
///         makes documentation. That split shipped once — a comment edit introduced the adjacent
///         hyphens XML forbids inside comments — and nothing went red for five days, because the
///         fallback surfaces in the consumer's log rather than in any gate.
///     </para>
///     <para>
///         <b>Why the profile's content is pinned too.</b> The declared default and the profile it
///         names sit at opposite ends of the file and drift independently; a rename that reaches
///         only one end sends every profile-less cleanup to Full Cleanup just as silently. And of
///         the profile's tasks, two are off for cause rather than taste: ArrangeArgumentsStyle, the
///         task that would normalise named arguments to positional, and CSReorderTypeMembers, the
///         only task that moves members wholesale across line-pinned files. The blob is not pinned
///         whole — every other task is a nicety, adjustable without ceremony.
///     </para>
///     <para>
///         <b>Why wrapping is pinned from both sides.</b> The harness's fluent chains are chopped one
///         call per line, and the formatter's job is to preserve that shape, never to compute it.
///         Two different edits break that, in opposite directions. Deleting the <c>WRAP_LINES</c>
///         entry looks like removing a restriction and is really the opposite — the ReSharper default
///         is wrapping on, which fully chops any chain already carrying a break. And leaving a
///         <c>WRAP_CHAINED_METHOD_CALLS</c> key behind after the one-time chopping pass arms every
///         later cleanup to chop the files that pass deliberately left alone. Both show up as an
///         enormous diff on some unrelated file's next cleanup, long after the edit that caused it.
///     </para>
/// </remarks>
public sealed class DotSettingsCleanupProfileTests
{
    private const string SilentCleanupProfileKey = "/Default/CodeStyle/CodeCleanup/SilentCleanupProfile/@EntryValue";
    private const string ProfileKeyPrefix = "/Default/CodeStyle/CodeCleanup/Profiles/=";
    private const string WrapLinesKey = "/Default/CodeStyle/CodeFormatting/CSharpFormat/WRAP_LINES/@EntryValue";
    private const string ChainWrapKeyFragment = "WRAP_CHAINED_METHOD_CALLS";

    private static readonly XName KeyName = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    [Fact]
    public void SolutionDotSettings_IsWellFormedXml()
    {
        Should.NotThrow(
            static () => XDocument.Load(RepoRoot.SolutionDotSettings),
            "ReSharper tolerates a malformed .DotSettings, so nothing else in the toolchain notices "
            + "when this file stops parsing — strict readers, silent-cleanup profile resolution "
            + "among them, silently fall back to Built-in: Full Cleanup, which strips "
            + "deliberate named arguments. The classic way to break it: two adjacent hyphens inside "
            + "an XML comment.");
    }

    [Fact]
    public void SilentCleanupProfile_NamesAProfileTheFileDefines()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);
        string declared = DeclaredProfileName(settings);

        // Act
        var defined = DefinedProfileNames(settings);

        // Assert
        defined.ShouldContain(declared,
            "The silent-cleanup default and the profile it names sit at opposite ends of the file; "
            + "a rename that reaches only one end sends every profile-less cleanup to Built-in: "
            + "Full Cleanup, silently.");
    }

    [Fact]
    public void DeclaredCleanupProfile_KeepsArgumentStyleAndMemberOrderAsAuthored()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);
        XElement profile = DeclaredProfile(settings);

        // Act
        string? arrangeArgumentsStyle = profile.Element("CSCodeStyleAttributes")
            ?.Attribute("ArrangeArgumentsStyle")
            ?.Value;
        string? reorderTypeMembers = profile.Element("CSReorderTypeMembers")
            ?.Value;

        // Assert
        arrangeArgumentsStyle.ShouldBe("False",
            "ArrangeArgumentsStyle is the cleanup task that normalises arguments to positional; False "
            + "is the only value that leaves the deliberate named arguments (GRAMMAR §10) as-authored.");
        reorderTypeMembers.ShouldBe("False",
            "CSReorderTypeMembers is the only cleanup task that moves a member wholesale across a "
            + "file, and SpecValidationSpecs.cs pins expected messages to literal line numbers; False "
            + "is what lets cleanup run over any file in the repo without consulting a list.");
    }

    [Fact]
    public void LineWrapping_IsDeclaredOffRatherThanLeftUnset()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        XElement? wrapLines = ShouldHaveEntries(settings)
            .SingleOrDefault(static entry => KeyOf(entry) == WrapLinesKey);

        // Assert
        XElement declared = wrapLines.ShouldNotBeNull(
            "Wrapping off is not ReSharper's default, so deleting this entry re-enables margin "
            + "wrapping rather than leaving things as they are — and margin wrapping fully chops any "
            + "chain that already carries one break, which is most of the line-pinned ones.");
        declared.Value.ShouldBe("False",
            "The chopped chains throughout the harness are preserved, never produced, by the "
            + "formatter; any value but False puts cleanup back in the business of rewriting them.");
    }

    [Fact]
    public void ChainMethodWrapping_HasNoEntryLeftBehind()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        string[] chainWrapKeys = ShouldHaveEntries(settings)
            .Select(KeyOf)
            .Where(static key => key.Contains(ChainWrapKeyFragment, StringComparison.Ordinal))
            .ToArray();

        // Assert
        chainWrapKeys.ShouldBeEmpty(
            "CHOP_ALWAYS belongs to the one-time pass that chopped the harness and was removed with "
            + "it. Left standing, it chops every two-call chain on the next cleanup of any file — "
            + "SpecValidationSpecs.cs included, whose forty expected messages quote literal line "
            + "numbers.");
    }

    private static string DeclaredProfileName(XDocument settings)
    {
        XElement entry = ShouldHaveEntries(settings)
            .Single(static entry => KeyOf(entry) == SilentCleanupProfileKey);

        return entry.Value;
    }

    private static XElement DeclaredProfile(XDocument settings)
    {
        string declared = DeclaredProfileName(settings);

        return ShouldHaveDefinedProfiles(settings)
            .Single(profile => profile.Attribute("name")
                ?.Value == declared);
    }

    private static IReadOnlyList<string?> DefinedProfileNames(XDocument settings)
    {
        return ShouldHaveDefinedProfiles(settings)
            .Select(static profile => profile.Attribute("name")
                ?.Value)
            .ToArray();
    }

    // Each profile is a whole XML document escaped into its entry's text; parsing it back out reads
    // the content the way ReSharper does, so the pins hold against what actually runs.
    private static IReadOnlyList<XElement> ShouldHaveDefinedProfiles(XDocument settings)
    {
        return ShouldHaveEntries(settings)
            .Where(static entry => KeyOf(entry)
                .StartsWith(ProfileKeyPrefix, StringComparison.Ordinal))
            .Select(static entry => XDocument.Parse(entry.Value)
                .Root.ShouldNotBeNull())
            .ToArray();
    }

    private static IEnumerable<XElement> ShouldHaveEntries(XDocument settings)
    {
        XElement root = settings.Root.ShouldNotBeNull();

        return root.Elements();
    }

    private static string KeyOf(XElement entry)
    {
        return entry.Attribute(KeyName)
            ?.Value ?? "";
    }
}
