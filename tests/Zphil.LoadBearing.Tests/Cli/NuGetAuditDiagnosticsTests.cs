using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     Pins the NU19xx audit-family boundary that <see cref="NuGetAuditDiagnostics.IsAudit" /> draws over
///     raw diagnostic message strings: the whole NU1900–NU19xx family matches inside realistic advisory
///     text, while a neighbouring restore code (NU1701), a truncated token, an embedded token without a
///     leading word boundary, a five-digit run, an ordinary load failure, and the empty string all miss.
///     No workspace is involved, so this is a fast, non-<c>Serial</c> unit pin.
/// </summary>
public sealed class NuGetAuditDiagnosticsTests
{
    [Theory]
    [InlineData(
        "NU1903: Package 'System.Security.Cryptography.Xml' 4.7.0 has a known high severity vulnerability, "
        + "https://github.com/advisories/GHSA-7h4f-3q2m-9xrv")]
    [InlineData("NU1900: Error communicating with the package source while running a security audit.")]
    [InlineData("NU1901: Package 'Contoso.Widgets' 1.2.0 has a known low severity vulnerability")]
    [InlineData("Restore surfaced advisory code NU1999")]
    public void IsAudit_AuditCodeInMessage_ReturnsTrue(string diagnostic)
    {
        NuGetAuditDiagnostics.IsAudit(diagnostic).ShouldBeTrue();
    }

    [Theory]
    [InlineData(
        "NU1701: Package 'Legacy.Lib' 2.0.0 was restored using '.NETFramework,Version=v4.8' instead of the "
        + "project target framework 'net10.0'.")]
    [InlineData("NU19")]
    [InlineData("XNU1903 leaves no word boundary before the code, so it is not an advisory")]
    [InlineData("code NU19034 carries five digits and is not a NU19xx advisory")]
    [InlineData("Project 'MyApp.Broken' failed to load: simulated workspace-load failure.")]
    [InlineData("")]
    public void IsAudit_NonAuditText_ReturnsFalse(string diagnostic)
    {
        NuGetAuditDiagnostics.IsAudit(diagnostic).ShouldBeFalse();
    }
}