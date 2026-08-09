using System.Text.Json;
using System.Xml.Linq;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps every place this repository states its own version naming one version.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why here, when release.yml already checks three of the four.</b> It checks them against
///         the tag, in CI, at tag time — which is to say after the rewrite, after the release gate, and
///         with the release already in flight. A bump that reached the props file and one manifest
///         field is caught there by a job that has to be re-run against a new tag, and a tag is the one
///         thing in this flow that publishes. Catching the same disagreement in the suite costs
///         seconds and names the site.
///     </para>
///     <para>
///         <b>The fourth site is worse than unchecked.</b> The SARIF golden's driver version is held
///         only by <c>CheckCommandE2ETests</c>, which compares whole documents — so a missed bump
///         arrives as a JSON diff roughly nine minutes into a Release suite, at the end of a publish
///         run, reading like a rendering regression rather than like a number nobody changed.
///     </para>
///     <para>
///         <b>The props file is parsed as XML rather than matched.</b> Its own comment carries the
///         literal <c>"&lt;Version&gt;"</c>, which is why release.yml's regex needs its closing-tag
///         lookahead to stay correct. An element query cannot see inside a comment, so there is
///         nothing here to get wrong and no lookahead to preserve.
///     </para>
/// </remarks>
public sealed class ManifestVersionTests
{
    private const string PropsPath = "Directory.Build.props";
    private const string ManifestPath = ".mcp/server.json";
    private const string SarifGoldenPath = "tests/Zphil.LoadBearing.Tests/Cli/Golden/violated-check.sarif";

    private const string ManifestTopSite = $"{ManifestPath} .version";
    private const string ManifestPackageSite = $"{ManifestPath} .packages[0].version";
    private const string SarifDriverSite = $"{SarifGoldenPath} runs[0].tool.driver.version";

    [Fact]
    public void EveryVersionSite_NamesTheSameVersion()
    {
        // Arrange
        var sites = ReadSites();

        // Act
        string[] distinct = sites
            .Select(static site => site.Version)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Assert
        distinct.ShouldHaveSingleItem(
            "A version bump has to reach all four of these, and one that reaches only some of them "
            + "fails a long way from the edit: the two manifest fields are refused against the tag "
            + "inside release.yml, with the release already running, and the SARIF golden reds as a "
            + $"document diff at the end of a Release suite. What the sites say:\n{Describe(sites)}");
    }

    private static IReadOnlyList<VersionSite> ReadSites()
    {
        (string top, string package) = ManifestVersions();

        return
        [
            new VersionSite($"{PropsPath} <Version>", ShouldHaveSinglePropsVersion()),
            new VersionSite(ManifestTopSite, top),
            new VersionSite(ManifestPackageSite, package),
            new VersionSite(SarifDriverSite, SarifDriverVersion())
        ];
    }

    private static string ShouldHaveSinglePropsVersion()
    {
        XDocument props = XDocument.Load(RepoRoot.Absolute(PropsPath));

        var declared = props.Descendants("Version").ToArray();
        XElement version = declared.ShouldHaveSingleItem(
            $"{PropsPath} declares the lockstep <Version> exactly once, for all four packages at "
            + $"once; this run found {declared.Length}.");

        return version.Value;
    }

    private static (string Top, string Package) ManifestVersions()
    {
        string json = File.ReadAllText(RepoRoot.Absolute(ManifestPath));

        using JsonDocument manifest = JsonDocument.Parse(json);
        JsonElement root = manifest.RootElement;
        JsonElement firstPackage = root.GetProperty("packages")[0];

        string top = ShouldHaveText(root, "version", ManifestTopSite);
        string package = ShouldHaveText(firstPackage, "version", ManifestPackageSite);

        return (top, package);
    }

    // The golden's shape is CheckCommandE2ETests' pin, so navigating it plainly is safe; only the
    // leaf — the field a bump has to reach and nothing cross-checks against the tag — is read here.
    private static string SarifDriverVersion()
    {
        string json = File.ReadAllText(RepoRoot.Absolute(SarifGoldenPath));

        using JsonDocument golden = JsonDocument.Parse(json);
        JsonElement firstRun = golden.RootElement.GetProperty("runs")[0];
        JsonElement driver = firstRun.GetProperty("tool").GetProperty("driver");

        return ShouldHaveText(driver, "version", SarifDriverSite);
    }

    private static string ShouldHaveText(JsonElement parent, string name, string site)
    {
        bool found = parent.TryGetProperty(name, out JsonElement value);
        found.ShouldBeTrue($"{site} is absent, so nothing there states a version at all.");

        return value.GetString().ShouldNotBeNull($"{site} carries no version string.");
    }

    private static string Describe(IEnumerable<VersionSite> sites)
    {
        var lines = sites.Select(static site => $"  {site.Site} -> {site.Version}");

        return string.Join("\n", lines);
    }

    private sealed record VersionSite(string Site, string Version);
}
