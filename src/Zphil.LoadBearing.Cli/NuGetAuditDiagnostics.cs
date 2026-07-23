using System.Text.RegularExpressions;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Recognises the NuGet audit diagnostic family (NU19xx) so the <c>check</c> fail-closed gate can carve
///     it out. NuGetAudit re-raises NVD/GHSA advisories as restore warnings, which MSBuildWorkspace then
///     surfaces as workspace diagnostics — but an advisory's publication and the audit fetch's network
///     reachability are external, time-varying inputs, not a statement that the model failed to build.
///     Letting them reach a deterministic gate means a freshly published advisory (or an offline run) flips
///     the exit code with no source change, and vulnerability response already has owned lanes (Dependabot,
///     NuGetAudit itself, <c>dotnet list package --vulnerable</c>). So the family is filtered out of the
///     gate input only: the messages still render everywhere (stderr warnings, the JSON
///     <c>workspaceDiagnostics</c> array, SARIF notifications) — render-but-don't-gate.
/// </summary>
internal static partial class NuGetAuditDiagnostics
{
    /// <summary>
    ///     Whether <paramref name="diagnostic" /> is a NuGet audit advisory — NU1900 (the audit fetch itself
    ///     failed), NU1901–1904 (low/moderate/high/critical severity advisories), NU1905, and any future
    ///     NU19xx code. Diagnostics arrive as raw message strings with no structured id, so the match is on
    ///     the code token in the text: word-bounded on both sides (so <c>XNU1903</c>, a bare <c>NU19</c>, and
    ///     <c>NU19034</c> all miss), <c>[0-9]</c> rather than <c>\d</c> for ASCII-ordinal intent, and
    ///     case-sensitive. The false-positive direction here would un-gate a genuine load failure, so the
    ///     pattern stays deliberately narrow and conservative.
    /// </summary>
    internal static bool IsAudit(string diagnostic)
    {
        return AuditCode().IsMatch(diagnostic);
    }

    [GeneratedRegex(@"\bNU19[0-9]{2}\b")]
    private static partial Regex AuditCode();
}