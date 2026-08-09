using System.Xml.Linq;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps the solution <c>.DotSettings</c> well-formed XML, its silent-cleanup
///     default naming a profile the file actually defines, and that profile keeping the two cleanup
///     tasks off whose <c>False</c> is load-bearing.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why well-formedness needs a gate.</b> ReSharper's own settings reader tolerates
///         malformed XML, so jb keeps working — named profile, severities and all — after the file
///         stops being readable by any conformant XML parser. The consumers that break are the
///         strict ones: the MCP cleanup server resolves the silent-cleanup default with
///         <see cref="XDocument.Load(string)" />, and on failure falls back to
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
/// </remarks>
public sealed class DotSettingsCleanupProfileTests
{
    private const string SilentCleanupProfileKey = "/Default/CodeStyle/CodeCleanup/SilentCleanupProfile/@EntryValue";
    private const string ProfileKeyPrefix = "/Default/CodeStyle/CodeCleanup/Profiles/=";

    private static readonly XName KeyName = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    [Fact]
    public void SolutionDotSettings_IsWellFormedXml()
    {
        Should.NotThrow(
            static () => XDocument.Load(RepoRoot.SolutionDotSettings),
            "ReSharper tolerates a malformed .DotSettings, so nothing else in the toolchain notices "
            + "when this file stops parsing — strict readers, the MCP cleanup server's profile "
            + "resolution among them, silently fall back to Built-in: Full Cleanup, which strips "
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
        string? arrangeArgumentsStyle = profile.Element("CSCodeStyleAttributes")?.Attribute("ArrangeArgumentsStyle")?.Value;
        string? reorderTypeMembers = profile.Element("CSReorderTypeMembers")?.Value;

        // Assert
        arrangeArgumentsStyle.ShouldBe("False",
            "ArrangeArgumentsStyle is the cleanup task that normalises arguments to positional; False "
            + "is the only value that leaves the deliberate named arguments (GRAMMAR §10) as-authored.");
        reorderTypeMembers.ShouldBe("False",
            "CSReorderTypeMembers is the only cleanup task that moves a member wholesale across a "
            + "file, and SpecValidationSpecs.cs pins expected messages to literal line numbers; False "
            + "is what lets cleanup run over any file in the repo without consulting a list.");
    }

    private static string DeclaredProfileName(XDocument settings)
    {
        XElement entry = ShouldHaveEntries(settings).Single(static entry => KeyOf(entry) == SilentCleanupProfileKey);

        return entry.Value;
    }

    private static XElement DeclaredProfile(XDocument settings)
    {
        string declared = DeclaredProfileName(settings);

        return ShouldHaveDefinedProfiles(settings).Single(profile => profile.Attribute("name")?.Value == declared);
    }

    private static IReadOnlyList<string?> DefinedProfileNames(XDocument settings)
    {
        return ShouldHaveDefinedProfiles(settings)
            .Select(static profile => profile.Attribute("name")?.Value)
            .ToArray();
    }

    // Each profile is a whole XML document escaped into its entry's text; parsing it back out reads
    // the content the way ReSharper does, so the pins hold against what actually runs.
    private static IReadOnlyList<XElement> ShouldHaveDefinedProfiles(XDocument settings)
    {
        return ShouldHaveEntries(settings)
            .Where(static entry => KeyOf(entry).StartsWith(ProfileKeyPrefix, StringComparison.Ordinal))
            .Select(static entry => XDocument.Parse(entry.Value).Root.ShouldNotBeNull())
            .ToArray();
    }

    private static IEnumerable<XElement> ShouldHaveEntries(XDocument settings)
    {
        XElement root = settings.Root.ShouldNotBeNull();

        return root.Elements();
    }

    private static string KeyOf(XElement entry)
    {
        return entry.Attribute(KeyName)?.Value ?? "";
    }
}
