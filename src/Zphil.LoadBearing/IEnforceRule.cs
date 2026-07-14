namespace Zphil.LoadBearing;

/// <summary>Trailers available on an <c>Enforce</c> rule (GRAMMAR §3.2). <c>Because</c> is required (§8 item 3).</summary>
public interface IEnforceRule
{
    /// <summary>The rationale — required; rendered to agents and echoed in violation messages.</summary>
    IEnforceRule Because(string because);

    /// <summary>The remediation hint — optional; emitted at the point of failure.</summary>
    IEnforceRule Fix(string fix);
}