using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;
using Zphil.LoadBearing.Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate over the two packages a consumer's own project compiles against: their exported surface
///     must be the surface their committed pin records, and a change to it has to move the pin in the
///     same commit.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a pin exists at all when the csprojs already run ApiCompat.</b> ApiCompat speaks at
///         pack time, against the bytes on nuget.org — the right authority and the wrong moment. It runs
///         in one CI job and in the release gate, so a surface change reaches the author as a red build
///         minutes later and a continent away from the edit. This reds in the suite, in seconds, and
///         names the line. The two also answer different questions: ApiCompat asks whether a consumer of
///         the published package breaks, the pin asks whether the surface has moved since a human last
///         looked at it.
///     </para>
///     <para>
///         <b>The pin never rewrites itself.</b> On drift it writes the regenerated text beside the test
///         assembly and names both paths, and a person copies it across. No environment variable turns
///         that into an overwrite, because the whole value of the file is that every line in it was read
///         by someone: a NuGet version can never be replaced once pushed, only unlisted, so an
///         accidentally-public member becomes permanent at the next release.
///     </para>
///     <para>
///         <b>What holds the other two packages.</b> <c>Zphil.LoadBearing.Roslyn</c> says in its own
///         package description that it is not for direct reference, and <c>Zphil.LoadBearing.Cli</c>
///         ships a command rather than an assembly. Both are registered exemptions below and both are
///         held to the text that justifies them, so an exemption cannot outlive its reason. That there
///         are exactly four packages to account for is not asserted here — the self-spec rule
///         <c>packaging/only-the-four-ship</c> already holds it, and duplicating it would put the same
///         fact in two places that could disagree.
///     </para>
/// </remarks>
public sealed class PublicSurfaceSyncTests
{
    private const string PropsPath = "Directory.Build.props";

    private const string CoreCsproj = "src/Zphil.LoadBearing/Zphil.LoadBearing.csproj";
    private const string AdapterCsproj = "src/Zphil.LoadBearing.Xunit/Zphil.LoadBearing.Xunit.csproj";
    private const string RoslynCsproj = "src/Zphil.LoadBearing.Roslyn/Zphil.LoadBearing.Roslyn.csproj";
    private const string CliCsproj = "src/Zphil.LoadBearing.Cli/Zphil.LoadBearing.Cli.csproj";

    /// <summary>
    ///     The two assemblies a consumer writes code against: the contract package every spec project
    ///     references, and the adapter a test project derives from.
    /// </summary>
    private static readonly (string Csproj, Assembly Assembly)[] PinnedPackages =
    [
        (CoreCsproj, typeof(Arch).Assembly),
        (AdapterCsproj, typeof(ArchRuleTests<>).Assembly)
    ];

    /// <summary>
    ///     The four shipped assemblies by assembly name rather than package id, because the CLI's two
    ///     differ: it packs as <c>Zphil.LoadBearing.Cli</c> and its assembly is <c>loadbearing</c>, which
    ///     is the name the tool command and the internals-visible entry both use.
    /// </summary>
    private static readonly string[] ShippedAssemblyNames =
    [
        "Zphil.LoadBearing",
        "Zphil.LoadBearing.Roslyn",
        "Zphil.LoadBearing.Xunit",
        "loadbearing"
    ];

    /// <summary>
    ///     The two packages deliberately left unpinned, each with the text in its own csproj that makes
    ///     the exemption true. Reading the evidence back is what stops an exemption outliving its reason:
    ///     the day the Roslyn package is meant for direct reference, or the CLI stops being a tool, the
    ///     sentence that says otherwise will have gone and this arm fails rather than quietly covering a
    ///     package nothing watches.
    /// </summary>
    private static readonly Exemption[] Exemptions =
    [
        new(RoslynCsproj,
            "Description",
            "Not intended for direct reference",
            "machinery: it ships as a dependency of the tool and the adapter, and its own description "
            + "tells a reader to reference Zphil.LoadBearing instead"),
        new(CliCsproj,
            "PackAsTool",
            "true",
            "a global tool: what it ships is the `loadbearing` command, not an assembly anyone compiles "
            + "against")
    ];

    private static readonly Regex NumericVersion = new(@"^\d+\.\d+\.\d+");

    [Fact]
    public void EveryPinnedPackage_ShipsExactlyTheSurfaceItsPinRecords()
    {
        // Arrange
        List<string> drift = new();

        // Act: render each shipped surface fresh and compare it to the committed pin.
        foreach ((string _, Assembly assembly) in PinnedPackages)
        {
            string pinPath = PublicSurface.PinPath(assembly);
            string pinned = RepoRoot.ReadText(pinPath);
            string rendered = PublicSurface.Render(assembly);
            IReadOnlyList<string> moved = PublicSurface.Drift(pinned, rendered);
            if (moved.Count == 0) continue;

            drift.AddRange(moved.Select(finding => $"{pinPath}  {finding}"));
            drift.Add($"{pinPath}  once every line above is intended: "
                      + $"cp '{WriteRegenerated(assembly, rendered)}' '{RepoRoot.Absolute(pinPath)}'");
        }

        // Assert
        drift.ShouldReportNothing(
            "The shipped public surface has moved away from its pin. Every line below is something a "
            + "consumer sees, in a package version that can never be replaced once pushed — read them, "
            + "make internal anything that was never meant to be public, and only then copy the "
            + "regenerated file over the pin");
    }

    [Fact]
    public void TheReflectionSweep_SeesTheWholeSurface()
    {
        // Arrange: the failure this arm exists for is a pin regenerated from a sweep that saw nothing.
        // An empty pin compared against an empty render passes forever and says nothing. The anchors are
        // read through typeof so a rename follows them here and is left for the arm above to report.
        (Assembly Assembly, Type Anchor)[] anchors =
        [
            (typeof(Arch).Assembly, typeof(Arch)),
            (typeof(ArchRuleTests<>).Assembly, typeof(ArchRuleTests<>))
        ];
        List<string> blind = new();

        // Act
        foreach ((Assembly assembly, Type anchor) in anchors)
        {
            string rendered = PublicSurface.Render(assembly);
            var anchorLine = $"{anchor.FullName}\n";
            if (!rendered.Contains(anchorLine, StringComparison.Ordinal))
                blind.Add($"{assembly.GetName().Name}: the sweep did not see {anchor.FullName}, so "
                          + "whatever it did see is not this assembly's exported surface.");

            IReadOnlyList<Type> enums = PublicSurface.ExportedEnums(assembly);
            IEnumerable<Type> empty = enums.Where(static enumType => PublicSurface.Constants(enumType)
                .Count == 0);
            blind.AddRange(empty.Select(enumType =>
                $"{assembly.GetName().Name}: exported enum {enumType.FullName} contributed no constants, "
                + "so the renumbering this pin exists to catch would go unseen."));
        }

        // Assert
        blind.ShouldReportNothing("The reflection sweep behind the pins is not seeing what it claims to");
    }

    [Fact]
    public void EveryPackableProject_IsEitherPinnedOrExemptWithALiveReason()
    {
        // Arrange
        List<string> findings = new();

        // Act: a pinned package must have the pack-time check switched on and a committed pin beside it,
        // and an exempt one must still carry the text its exemption rests on.
        foreach ((string csproj, Assembly assembly) in PinnedPackages)
        {
            IReadOnlyDictionary<string, string> properties = Properties(csproj);
            findings.AddRange(MissingValidation(csproj, properties));

            string pinPath = PublicSurface.PinPath(assembly);
            if (!File.Exists(RepoRoot.Absolute(pinPath)))
                findings.Add($"{csproj} is registered as pinned but {pinPath} does not exist.");
        }

        foreach (Exemption exemption in Exemptions)
        {
            IReadOnlyDictionary<string, string> properties = Properties(exemption.Csproj);
            properties.TryGetValue(exemption.Element, out string? declared);

            if (declared is null || !declared.Contains(exemption.Evidence, StringComparison.Ordinal))
                findings.Add($"{exemption.Csproj} is exempt from the surface pin because it is "
                             + $"{exemption.Reason}, but its <{exemption.Element}> no longer says "
                             + $"'{exemption.Evidence}'. Either the exemption is dead and the package "
                             + "needs a pin, or the reason needs rewriting.");
        }

        // Assert
        findings.ShouldReportNothing(
            "These shipping packages are neither pinned nor exempt for a reason their csproj still states");
    }

    [Fact]
    public void EveryValidatedProject_NamesABaselineNoNewerThanTheVersion()
    {
        // Arrange: the baseline names the last PUBLISHED version, so it lags <Version> by design and is
        // deliberately not a fifth site in the four-way version equality ManifestVersionTests holds. What
        // it can never be is ahead: a baseline nobody has pushed cannot be downloaded, and the pack-time
        // check then fails at release time with a restore error rather than an API one.
        Version version = NumericPart(PropertyOf(PropsPath, "Version"));
        List<string> findings = new();

        // Act
        foreach ((string csproj, Assembly _) in PinnedPackages)
        {
            string declared = PropertyOf(csproj, "PackageValidationBaselineVersion");
            Version baseline = NumericPart(declared);
            if (baseline <= version) continue;

            findings.Add($"{csproj} validates against baseline {declared}, which is ahead of the "
                         + $"{version} this repository builds. The baseline may only name a version "
                         + "already on nuget.org.");
        }

        // Assert
        findings.ShouldReportNothing("These packages name a package-validation baseline that cannot exist yet");
    }

    [Fact]
    public void ShippedAssemblies_CarryThePinnedBindingIdentity()
    {
        // Arrange: AssemblyVersion is what a compiled spec's reference to the contract carries, and the
        // default binder refuses a host older than that reference. Letting it track the package version
        // meant every release minted a new identity and broke specs built against a newer contract than
        // the tool running them, which is why it is pinned — and why nothing but this asserts the value.
        List<string> findings = new();

        // Act
        foreach (string name in ShippedAssemblyNames)
        {
            Assembly assembly = Assembly.Load(new AssemblyName(name));
            Version? identity = assembly.GetName()
                .Version;
            if (identity == PublicSurface.PinnedBindingIdentity) continue;

            findings.Add($"{name} has AssemblyVersion {identity}, not "
                         + $"{PublicSurface.PinnedBindingIdentity}. Every spec assembly already compiled "
                         + "against this contract names the old identity and will stop binding.");
        }

        // Assert
        findings.ShouldReportNothing("These shipped assemblies no longer carry the pinned binding identity");
    }

    [Fact]
    public void EveryPinFile_IsCommittedAsLfWithOneTrailingNewline()
    {
        // Arrange: the pin is compared as text against a render that joins on "\n", so a checkout that
        // rewrote the file to CRLF would red every line of it at once and read like a surface change.
        // .gitattributes forces LF, and this is what notices the day it stops.
        List<string> findings = new();

        // Act
        foreach ((string _, Assembly assembly) in PinnedPackages)
        {
            string pinPath = PublicSurface.PinPath(assembly);
            string text = RepoRoot.ReadText(pinPath);

            if (text.Contains('\r')) findings.Add($"{pinPath} carries a carriage return; the pins are LF-only.");

            if (!text.EndsWith("\n", StringComparison.Ordinal))
                findings.Add($"{pinPath} does not end on a newline.");
            else if (text.EndsWith("\n\n", StringComparison.Ordinal))
                findings.Add($"{pinPath} ends on a blank line; the render writes exactly one newline.");
        }

        // Assert
        findings.ShouldReportNothing("These surface pins are not committed in the shape the render writes");
    }

    // The regenerated pin, written beside the test assembly rather than over the committed file. Named
    // ".actual" next to the pin's own file name so the two sort together in a directory listing.
    private static string WriteRegenerated(Assembly assembly, string rendered)
    {
        string fileName = Path.GetFileName(PublicSurface.PinPath(assembly));
        string path = Path.Combine(AppContext.BaseDirectory, $"{fileName}.actual");
        File.WriteAllText(path, rendered);

        return path;
    }

    private static IReadOnlyList<string> MissingValidation(
        string csproj,
        IReadOnlyDictionary<string, string> properties)
    {
        (string Property, string Expected)[] required =
        [
            ("EnablePackageValidation", "true"),
            ("EnableStrictModeForBaselineValidation", "true")
        ];

        List<string> missing = required
            .Where(entry => !properties.TryGetValue(entry.Property, out string? declared)
                            || !string.Equals(declared, entry.Expected, StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{csproj} does not set <{entry.Property}>{entry.Expected}, so nothing "
                             + "compares the package it builds against the one already published.")
            .ToList();

        if (!properties.ContainsKey("PackageValidationBaselineVersion"))
            missing.Add($"{csproj} switches package validation on but names no baseline version, which "
                        + "silently compares it against nothing.");

        return missing;
    }

    private static IReadOnlyDictionary<string, string> Properties(string csproj)
    {
        XDocument document = XDocument.Parse(RepoRoot.ReadText(csproj));
        IEnumerable<XElement> groups = document.Root!.Elements("PropertyGroup");

        return groups.SelectMany(static group => group.Elements())
            .GroupBy(static property => property.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last()
                    .Value.Trim(),
                StringComparer.Ordinal);
    }

    private static string PropertyOf(string path, string property)
    {
        IReadOnlyDictionary<string, string> properties = Properties(path);
        properties.TryGetValue(property, out string? value);

        return value
               ?? throw new InvalidOperationException($"{path} declares no <{property}>.");
    }

    // Versions here may carry a prerelease suffix that System.Version cannot parse, and the comparison
    // only ever needs the numeric core: a baseline names a released version, so the suffix is noise.
    private static Version NumericPart(string declared)
    {
        Match match = NumericVersion.Match(declared);
        if (!match.Success) throw new InvalidOperationException($"'{declared}' has no major.minor.patch part.");

        return Version.Parse(match.Value);
    }

    /// <summary>
    ///     One package deliberately left unpinned: the <paramref name="Csproj" /> it is declared in, the
    ///     <paramref name="Element" /> whose text carries the <paramref name="Evidence" /> that makes the
    ///     exemption true, and the <paramref name="Reason" /> a reader of a failure needs.
    /// </summary>
    private sealed record Exemption(string Csproj, string Element, string Evidence, string Reason);
}
