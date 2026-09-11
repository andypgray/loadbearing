namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The calls available after <c>Enforce</c>: <see cref="Because" />, which is required, and the
///     optional <see cref="Fix" /> and <see cref="Citation" />, in any order and each at most once.
///     Every prose value is a single non-blank line; anything else is reported when the spec is loaded.
/// </summary>
public interface IEnforceRule
{
    /// <summary>
    ///     States why the rule exists, in one line of prose. Required: a rule without it is reported when
    ///     the spec is loaded. Rendered into the generated agent context and echoed with every violation.
    /// </summary>
    IEnforceRule Because(string because);

    /// <summary>
    ///     States what to do instead, in one line of prose. Optional; rendered into the generated agent
    ///     context and printed with every violation.
    /// </summary>
    IEnforceRule Fix(string fix);

    /// <summary>
    ///     Names the page the rule's reasoning rests on, as an absolute <c>http</c> or <c>https</c> URL.
    ///     Optional. Rendered as "See &lt;url&gt;." after the reason in the generated agent context,
    ///     printed as a <c>citation:</c> line by <c>explain</c> and beneath a failed rule by <c>check</c>,
    ///     and published as the rule's help URI in SARIF output. A value that is not an absolute http(s)
    ///     URL is reported when the spec is loaded.
    /// </summary>
    IEnforceRule Citation(string uri);
}
