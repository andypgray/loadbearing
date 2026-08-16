using Shouldly;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The Shouldly surface over <see cref="SpecValidationException" />: one assertion for the question
///     the catalog asks of every failing spec — was this code reported, against this rule (GRAMMAR §8).
/// </summary>
/// <remarks>
///     <para>
///         The predicate form it replaces reds by printing the lambda, not the errors, so a catalog
///         failure said which check was expected and never which checks ran. This lists every reported
///         (code, rule) pair in the order the validator found them, which is the first question a red
///         about a one-pass validator raises.
///     </para>
///     <para>
///         Returns the matched error so the site can go on to pin its message, which stays at the call
///         site: those strings are the spec, and burying them in an overload would hide them.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class SpecValidationAssertions
{
    /// <summary>
    ///     Asserts <paramref name="exception" /> reported <paramref name="code" /> — against
    ///     <paramref name="ruleId" /> when one is given — and returns the first error that matched.
    /// </summary>
    internal static SpecValidationError ShouldHaveError(
        this SpecValidationException exception, SpecValidationErrorCode code, string? ruleId = null)
    {
        SpecValidationError? match = exception.Errors
            .FirstOrDefault(error => error.Code == code && (ruleId is null || error.RuleId == ruleId));

        if (match is null)
        {
            string wanted = ruleId is null ? $"{code}" : $"{code} on '{ruleId}'";
            throw new ShouldAssertException(
                $"No {wanted} was reported.{Environment.NewLine}{Describe(exception)}");
        }

        return match;
    }

    /// <summary>Every reported error as <c>Code on 'ruleId'</c>, in validator order.</summary>
    private static string Describe(SpecValidationException exception)
    {
        IEnumerable<string> lines = exception.Errors
            .Select(error => $"    {error.Code} on '{error.RuleId ?? "(no rule)"}'");

        return $"  reported {exception.Errors.Count} error(s):{Environment.NewLine}"
               + string.Join(Environment.NewLine, lines);
    }
}
