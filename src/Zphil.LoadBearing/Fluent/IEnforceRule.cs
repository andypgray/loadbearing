namespace Zphil.LoadBearing.Fluent;

/// <summary>Trailers available on an <c>Enforce</c> rule (GRAMMAR §3.2). <c>Because</c> is required (§8 item 3).</summary>
public interface IEnforceRule
{
    /// <summary>The rationale — required; rendered to agents and echoed in violation messages.</summary>
    IEnforceRule Because(string because);

    /// <summary>The remediation hint — optional; emitted at the point of failure.</summary>
    IEnforceRule Fix(string fix);

    /// <summary>
    ///     The canonical page the rationale rests on — optional; an absolute <c>http</c>/<c>https</c> URL.
    ///     Renders as "See &lt;url&gt;." after the reason in the agent block, as <c>explain</c>'s and
    ///     <c>check</c>'s <c>citation:</c> line, and as SARIF <c>helpUri</c> plus <c>help.markdown</c>.
    /// </summary>
    IEnforceRule Citation(string uri);
}
