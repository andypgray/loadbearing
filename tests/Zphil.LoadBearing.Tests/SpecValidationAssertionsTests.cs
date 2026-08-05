using Shouldly;
using Xunit;
using Zphil.LoadBearing.Validation;
using Code = Zphil.LoadBearing.Validation.SpecValidationErrorCode;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The negative case for <see cref="SpecValidationAssertions" />: an assertion helper that never
///     throws passes silently at every site that calls it, so the one fact worth pinning is that it reds.
/// </summary>
public sealed class SpecValidationAssertionsTests
{
    [Fact]
    public void ShouldHaveError_CodeNeverReported_Reds()
    {
        var ex = Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(new DanglingRuleSpec()));

        Should.Throw<ShouldAssertException>(() => ex.ShouldHaveError(Code.DuplicateId));
    }

    [Fact]
    public void ShouldHaveError_CodeReportedAgainstAnotherRule_Reds()
    {
        // Matching on (code, rule) rather than code alone is the stricter half: the code IS reported here,
        // just not for the rule named.
        var ex = Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(new DanglingRuleSpec()));

        Should.Throw<ShouldAssertException>(() => ex.ShouldHaveError(Code.DanglingAnchor, "area/nothing"));
    }
}
