using Xunit;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Xunit;

namespace Zphil.LoadBearing.Tests.Dogfood;

/// <summary>
///     The adapter dogfood (acceptance box 3): LoadBearing's own self-spec, run through the xUnit
///     adapter. Its one rule — <c>layering/core-no-roslyn</c> — surfaces as an individually named test
///     (the rule ID is the display name) and is green. The <see cref="ArchRuleTests{TSpec}" /> default
///     <c>ExcludeProjectName</c> drops the spec's own project (<c>Zphil.LoadBearing.ArchSpec</c>) from the
///     checked universe, exactly as the CLI self-check does. Its <c>SolutionPath</c> resolves through the
///     shipped <c>FindSolutionUp</c> helper, so the dogfood runs the exact line a consumer writes.
/// </summary>
[Collection("Serial")]
public sealed class AdapterSelfSpecTests : ArchRuleTests<LoadBearingArchSpec>
{
    protected override string SolutionPath => FindSolutionUp("Zphil.LoadBearing.slnx");
}