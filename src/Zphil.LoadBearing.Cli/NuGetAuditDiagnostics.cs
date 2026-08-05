using System.Text.RegularExpressions;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Recognises the NuGet audit diagnostic family (NU19xx) so the <c>check</c> fail-closed gate can carve
///     it out. NuGetAudit re-raises NVD/GHSA advisories as restore warnings, which are replayed on every
///     later build from the assets file and which MSBuildWorkspace then surfaces as workspace diagnostics —
///     but an advisory's publication and the audit fetch's network reachability are external, time-varying
///     inputs, not a statement that the model failed to build. Letting them reach a deterministic gate means
///     a freshly published advisory (or an offline run) flips the exit code with no source change, and
///     vulnerability response already has owned lanes (Dependabot, NuGetAudit itself,
///     <c>dotnet list package --vulnerable</c>). So the family is filtered out of the gate input only: the
///     messages still render everywhere (stderr warnings, the JSON <c>workspaceDiagnostics</c> array, SARIF
///     notifications) — render-but-don't-gate.
/// </summary>
/// <remarks>
///     <para>
///         <b>The code is not in the text, so the text is what this matches.</b> Roslyn's
///         <c>MSBuildDiagnosticLogger</c> records <c>BuildEventArgs.Message</c> and never <c>.Code</c>, so
///         <c>NU1902</c> is structurally absent from every diagnostic that reaches a host. What arrives is
///         NuGet's own words inside Roslyn's project-load frame, and nothing else:
///         <c>Package 'X' 1.0.0 has a known moderate severity vulnerability, https://…/advisories/GHSA-…</c>.
///         A code-only matcher therefore never fired on a real advisory, and every solution carrying one
///         vulnerable transitive package failed <c>check</c> closed with a complete, correct model
///         (issue #19). The code match is kept for the paths that do carry one; the advisory match is what
///         does the work.
///     </para>
///     <para>
///         <b>Severity, not the gate, is what an advisory arrives as.</b> These are MSBuild <em>warnings</em>
///         — Roslyn reports project-load log items as
///         <see cref="Microsoft.CodeAnalysis.WorkspaceDiagnosticKind.Failure" />
///         regardless — so carving them out does not un-gate a project that genuinely failed to load. A
///         real load failure riding alongside an advisory still fails closed, which
///         <c>WorkspaceDiagnosticsGateE2ETests</c> pins.
///     </para>
///     <para>
///         <b>Known limit: localisation.</b> NuGet's advisory text is localised, so a non-English toolchain
///         can still red the gate. The GHSA URL alternative covers the common case regardless of language;
///         a full fix needs a code, which Roslyn does not give us.
///     </para>
/// </remarks>
internal static partial class NuGetAuditDiagnostics
{
    /// <summary>
    ///     Whether <paramref name="diagnostic" /> is a NuGet audit advisory — NU1900 (the audit fetch itself
    ///     failed), NU1901–1904 (low/moderate/high/critical severity advisories), NU1905, and any future
    ///     NU19xx code — recognised by the advisory text NuGet emits, or by the code on the paths that keep
    ///     one. The false-positive direction here would un-gate a genuine load failure, so both patterns stay
    ///     deliberately narrow: phrases NuGet owns, and a URL prefix nothing else emits.
    /// </summary>
    internal static bool IsAudit(string diagnostic)
    {
        return AuditText().IsMatch(diagnostic) || AuditCode().IsMatch(diagnostic);
    }

    /// <summary>
    ///     The advisory itself: the GHSA link every NU1901–1904 message carries, the severity phrasing, and
    ///     the two NU1900 audit-fetch failures. Case-sensitive, and anchored on wording NuGet owns rather
    ///     than on any package or severity name a project could coincidentally produce.
    /// </summary>
    [GeneratedRegex(
        @"https://github\.com/advisories/"
        + @"|\bhas a known (low|moderate|high|critical) severity vulnerability\b"
        + @"|\bError occurred while getting package vulnerability data\b"
        + @"|\bwhile running a security audit\b")]
    private static partial Regex AuditText();

    /// <summary>
    ///     The code token, for any path that preserves one (a binlog replay, a future Roslyn that carries
    ///     <c>.Code</c>). Word-bounded on both sides — so <c>XNU1903</c>, a bare <c>NU19</c>, and
    ///     <c>NU19034</c> all miss — <c>[0-9]</c> rather than <c>\d</c> for ASCII-ordinal intent, and
    ///     case-sensitive.
    /// </summary>
    [GeneratedRegex(@"\bNU19[0-9]{2}\b")]
    private static partial Regex AuditCode();
}
