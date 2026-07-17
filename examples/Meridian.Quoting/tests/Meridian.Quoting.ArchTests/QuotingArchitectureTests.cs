using Meridian.Quoting.ArchSpec;
using Zphil.LoadBearing.Xunit;

namespace Meridian.Quoting.ArchTests;

/// <summary>
///     The xUnit adapter pattern for the quoting spec: derive from
///     <c>ArchRuleTests&lt;QuotingArchSpec&gt;</c> and every post-desugar rule in
///     <see cref="QuotingArchSpec" /> becomes its own individually named test — the rule ID is the
///     test's display name, so a violated architecture rule reads as a named failing test whose
///     message is the exact CLI human block.
/// </summary>
public sealed class QuotingArchitectureTests : ArchRuleTests<QuotingArchSpec>
{
    protected override string SolutionPath => FindSolutionUp("Meridian.Quoting.slnx");
}
