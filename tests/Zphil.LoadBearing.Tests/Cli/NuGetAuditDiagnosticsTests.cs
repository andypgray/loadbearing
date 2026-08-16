using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     Pins the audit-family boundary that <see cref="NuGetAuditDiagnostics.IsAudit" /> draws over raw
///     diagnostic message strings. The matching arm leads with the shapes that actually reach a host —
///     captured from MSBuildWorkspace runs over solutions carrying real advisories, where Roslyn has
///     already dropped the NU19xx code — because the code-only matcher this replaced never fired on one
///     (issue #19). A neighbouring restore code, a truncated token, an ordinary load failure, and text that
///     merely mentions a vulnerability all still miss. No workspace is involved, so this is a fast,
///     non-<c>Serial</c> unit pin.
/// </summary>
/// <remarks>
///     <b>What a wrong answer here now costs.</b> This classifier used to be a gate input, so a miss refused
///     a healthy solution and a false hit let a broken one through. It decides no verdict any more — the
///     fail-closed gate reads which projects failed to load, off the loaded solution's structure — so what
///     turns on these cases is only which of two spellings a refusal that was going to happen anyway uses,
///     and the order a bounded quote puts its evidence in. The boundary stays narrow and stays pinned
///     because a message that offers the load as an explanation should not be triggered by an advisory
///     published this morning; it is no longer load-bearing for any exit code.
/// </remarks>
public sealed class NuGetAuditDiagnosticsTests
{
    /// <summary>The frame Roslyn wraps every project-load diagnostic in, code already discarded.</summary>
    private const string Wrapper =
        "Msbuild failed when processing the file 'C:\\src\\App\\App.csproj' with message: ";

    [Theory]
    // Captured verbatim: a NU1902 advisory as it arrives after Roslyn drops the code.
    [InlineData(
        Wrapper + "Package 'System.Security.Cryptography.Xml' 4.7.0 has a known moderate severity "
                + "vulnerability, https://github.com/advisories/GHSA-vh55-786g-wjwj")]
    // The same advisory escalated by TreatWarningsAsErrors, which only adds a prefix.
    [InlineData(
        Wrapper + "Warning As Error: Package 'System.Drawing.Common' 4.7.0 has a known critical severity "
                + "vulnerability, https://github.com/advisories/GHSA-rxg9-xrhp-64gj")]
    // The shape reported in issue #19.
    [InlineData(
        Wrapper + "Package 'KubernetesClient' 15.0.1 has a known moderate severity vulnerability, "
                + "https://github.com/advisories/GHSA-w7r3-mgwf-4mqq")]
    // NU1900, the audit fetch itself failing — an offline or unreachable-source run.
    [InlineData(
        Wrapper + "Error occurred while getting package vulnerability data: Unable to load the service index "
                + "for source https://nuget.example.com/v3/index.json.")]
    [InlineData("NU1900: Error communicating with the package source while running a security audit.")]
    // And the code-bearing shapes, for any path that still preserves one.
    [InlineData(
        "NU1903: Package 'System.Security.Cryptography.Xml' 4.7.0 has a known high severity vulnerability, "
        + "https://github.com/advisories/GHSA-7h4f-3q2m-9xrv")]
    [InlineData("NU1901: Package 'Contoso.Widgets' 1.2.0 has a known low severity vulnerability")]
    [InlineData("Restore surfaced advisory code NU1999")]
    public void IsAudit_AuditAdvisory_ReturnsTrue(string diagnostic)
    {
        NuGetAuditDiagnostics.IsAudit(diagnostic)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(
        "NU1701: Package 'Legacy.Lib' 2.0.0 was restored using '.NETFramework,Version=v4.8' instead of the "
        + "project target framework 'net10.0'.")]
    [InlineData("NU19")]
    [InlineData("XNU1903 leaves no word boundary before the code, so it is not an advisory")]
    [InlineData("code NU19034 carries five digits and is not a NU19xx advisory")]
    [InlineData("Project 'MyApp.Broken' failed to load: simulated workspace-load failure.")]
    // A genuine load failure that happens to name a vulnerability: the phrasing NuGet owns is absent, so
    // this still gates. Widening the match to the bare word would have un-gated it.
    [InlineData(Wrapper + "The type 'VulnerabilityScanner' could not be found in the referenced assembly.")]
    [InlineData("")]
    public void IsAudit_NonAuditText_ReturnsFalse(string diagnostic)
    {
        NuGetAuditDiagnostics.IsAudit(diagnostic)
            .ShouldBeFalse();
    }
}
