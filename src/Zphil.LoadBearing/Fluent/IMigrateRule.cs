namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The calls available after <c>Migrate</c>: <see cref="Because" />, which is required, and the
///     optional <see cref="Baseline" />, <see cref="WhileYoureThere" />, <see cref="Fix" /> and
///     <see cref="Citation" />, in any order and each at most once. Every prose value is a single
///     non-blank line; anything else is reported when the spec is loaded.
/// </summary>
public interface IMigrateRule
{
    /// <summary>
    ///     States why the migration exists, in one line of prose. Required: a rule without it is reported
    ///     when the spec is loaded. Rendered into the generated agent context and echoed with every
    ///     violation.
    /// </summary>
    IMigrateRule Because(string because);

    /// <summary>
    ///     States what to do instead, in one line of prose. Optional; rendered into the generated agent
    ///     context and printed with every violation.
    /// </summary>
    IMigrateRule Fix(string fix);

    /// <summary>
    ///     Names the page the rule's reasoning rests on, as an absolute <c>http</c> or <c>https</c> URL.
    ///     Optional. Rendered as "See &lt;url&gt;." between the reason and the migration policy in the
    ///     generated agent context, printed as a <c>citation:</c> line by <c>explain</c> and beneath a
    ///     failed rule by <c>check</c>, and published as the rule's help URI in SARIF output. A value that
    ///     is not an absolute http(s) URL is reported when the spec is loaded.
    /// </summary>
    IMigrateRule Citation(string uri);

    /// <summary>
    ///     Sets the file that records the rule's grandfathered violations, captured with the CLI's
    ///     <c>baseline</c> verb. Optional: when omitted the path is <c>arch/baselines/{rule-id}.json</c>,
    ///     each <c>/</c> in the rule ID becoming a directory. A relative path resolves against the
    ///     solution directory. Until a baseline is captured, every violation fails the check.
    /// </summary>
    IMigrateRule Baseline(string path);

    /// <summary>
    ///     Sets what an editor already changing a file with grandfathered violations is asked to do about
    ///     them. Optional; defaults to <see cref="MigrationPolicy.MigrateIfSmall" />. Rendered into the
    ///     generated agent context as guidance, and nothing checks it.
    /// </summary>
    IMigrateRule WhileYoureThere(MigrationPolicy policy);
}
