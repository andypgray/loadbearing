using System.Text.RegularExpressions;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     Recognises the NuGet audit diagnostic family (NU19xx) among the workspace-load diagnostics, so a
///     message that offers the load as an explanation for something can lead with a diagnostic that is
///     actually about this solution.
/// </summary>
/// <remarks>
///     <para>
///         <b>What this decides: nothing.</b> The fail-closed gate reads which projects failed to load and
///         which projects' packages are missing off structure — <see cref="ProjectLoadFailures" /> and
///         <see cref="RestoreFailures" /> — never diagnostics, so an advisory cannot flip an exit code
///         whether this recognises it or not, and neither can any other message. The one load-bearing
///         consumer is <see cref="IsAuditCode" />, on the assets-file path where NuGet records the code as
///         a field of its own.
///     </para>
///     <para>
///         <b>What remains here is ordering and wording, never a verdict.</b> An advisory's publication
///         date and an audit fetch's network reachability are external, time-varying inputs that say
///         nothing about how this codebase is built, so a run whose only diagnostics are advisories must
///         not be sent to go and repair its restore, and freshly published advisories must not push the
///         one actionable line out of a bounded quote.
///     </para>
///     <para>
///         <b>The code is not in the text, so the text is what this matches.</b> Roslyn's project-load
///         reporting records the message and never the code, so <c>NU1902</c> is structurally absent from
///         every diagnostic that reaches a host; what arrives is NuGet's own words inside Roslyn's frame.
///         The code match is kept for the paths that do carry one; the advisory match is what does the
///         work. A miss — a localised advisory, say — costs sorting inside one bounded quote, and changes
///         no exit code and no document.
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
    ///     Whether <paramref name="code" /> is a NuGet audit code — the same NU19xx family
    ///     <see cref="IsAudit" /> recognises, matched on the code alone.
    /// </summary>
    /// <remarks>
    ///     For the one caller that has a real code to match rather than a sentence:
    ///     <see cref="RestoreFailures" /> reads <c>project.assets.json</c>, where NuGet records <c>code</c> and
    ///     <c>level</c> as separate invariant fields. That makes the carve-out this class could never perform
    ///     on the diagnostic stream — where <c>.Code</c> is discarded before a host sees it — exact and
    ///     language-independent there, and it is load-bearing there in a way it is not here: it keeps an
    ///     advisory promoted to an error by <c>TreatWarningsAsErrors</c> from refusing a solution whose
    ///     resolution succeeded and whose model is complete.
    /// </remarks>
    internal static bool IsAuditCode(string code)
    {
        return AuditCode().IsMatch(code);
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
