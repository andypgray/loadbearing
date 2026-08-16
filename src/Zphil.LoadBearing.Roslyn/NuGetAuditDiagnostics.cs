using System.Text.RegularExpressions;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Recognises the NuGet audit diagnostic family (NU19xx) among the workspace-load diagnostics, so a
///     message that offers the load as an explanation for something can lead with a diagnostic that is
///     actually about this solution.
/// </summary>
/// <remarks>
///     <para>
///         <b>What this decides: nothing.</b> It used to be the <c>check</c> fail-closed gate's carve-out —
///         the family was filtered out of the gate input while still rendering everywhere. That boundary is
///         gone, because the gate no longer reads diagnostics at all:
///         <see cref="ProjectLoadFailures" /> reads which projects failed to load off the loaded solution's
///         own structure, and <see cref="WorkspaceDiagnostics.FailedProjects" /> is the whole gate input. So
///         an advisory cannot flip an exit code whether this recognises it or not, and neither can any other
///         message.
///     </para>
///     <para>
///         <b>Why it still exists.</b> Two refusals have nothing but text to offer — <c>SpecResolver</c>'s
///         "no spec project found" chooses between blaming the load and blaming the spec, and its quote is
///         bounded. An advisory's publication date and an audit fetch's network reachability are external,
///         time-varying inputs that say nothing about how this codebase is built, so a run whose only
///         diagnostics are advisories must not be sent to go and repair its restore, and three freshly
///         published advisories must not fill a three-line quote and push the one actionable line out of it.
///         That is the whole remaining job: ordering and wording, never a verdict.
///     </para>
///     <para>
///         <b>The code is not in the text, so the text is what this matches.</b> Roslyn's
///         <c>MSBuildDiagnosticLogger</c> records <c>BuildEventArgs.Message</c> and never <c>.Code</c>, so
///         <c>NU1902</c> is structurally absent from every diagnostic that reaches a host. What arrives is
///         NuGet's own words inside Roslyn's project-load frame, and nothing else:
///         <c>Package 'X' 1.0.0 has a known moderate severity vulnerability, https://…/advisories/GHSA-…</c>.
///         A code-only matcher therefore never fired on a real advisory, and while this family was a gate
///         input, every solution carrying one vulnerable transitive package failed <c>check</c> closed with a
///         complete, correct model (issue #19). The code match is kept for the paths that do carry one; the
///         advisory match is what does the work.
///     </para>
///     <para>
///         <b>Localisation is no longer a limit, because it is no longer load-bearing.</b> NuGet ships its
///         wording in many languages, and matching text could never be finished: the measured German
///         <c>NU1900</c> carries no GHSA URL, no <c>NU1900</c> token, and neither English phrase below, so it
///         refused where the identical English run passed. Nothing about that refusal was this matcher's to
///         fix — the defect was that a message reached a decision. It no longer can, in any language. What a
///         miss here costs now is that a German advisory sorts as though it were actionable inside one
///         bounded quote, which changes no exit code and no document.
///     </para>
/// </remarks>
internal static partial class NuGetAuditDiagnostics
{
    /// <summary>
    ///     Whether <paramref name="diagnostic" /> is a NuGet audit advisory — NU1900 (the audit fetch itself
    ///     failed), NU1901–1904 (low/moderate/high/critical severity advisories), NU1905, and any future
    ///     NU19xx code — recognised by the advisory text NuGet emits, or by the code on the paths that keep
    ///     one. Both patterns stay deliberately narrow — phrases NuGet owns, and a URL prefix nothing else
    ///     emits — so that a false positive cannot demote a genuine load diagnostic in the one bounded quote
    ///     that reads this.
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
