using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The negative case for <see cref="CheckJsonAssertions" />: an assertion helper that never throws
///     passes silently at every site that calls it, so the one fact worth pinning is that it reds.
/// </summary>
/// <remarks>
///     The document below is hand-written and deliberately not a second copy of the wire shape — the JSON
///     golden owns that, and a copy here would be one more thing to move when the schema does. It carries
///     only what makes these reds reachable: one rule whose violation is sole, and one whose two violations
///     share an endpoint.
/// </remarks>
public sealed class CheckJsonAssertionsTests
{
    private const string Report =
        """
        {
          "rules": [
            {
              "id": "ex/one-catch",
              "status": "failed",
              "violations": [
                {
                  "kind": "catch",
                  "source": "App.Handler",
                  "target": "System.Exception",
                  "sites": [ { "file": "Handler.cs", "line": 7 } ]
                }
              ]
            },
            {
              "id": "ex/two-catches",
              "status": "failed",
              "violations": [
                {
                  "kind": "catch",
                  "source": "App.Handler",
                  "target": "System.Exception",
                  "sites": [ { "file": "Handler.cs", "line": 7 } ]
                },
                {
                  "kind": "catch",
                  "source": "App.Handler",
                  "target": "System.IO.IOException",
                  "sites": [ { "file": "Handler.cs", "line": 12 } ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void ShouldHaveSingleViolationOnEdge_KindMismatch_Reds()
    {
        // A catch violation asserted as a reference: the batched field conditions must surface it rather than
        // swallow it, which is what makes the helper safe to use at every bystander site.
        Should.Throw<ShouldAssertException>(() => Report.ShouldHaveSingleViolationOnEdge(
            "ex/one-catch", kind: "reference", source: "App.Handler", target: "System.Exception"));
    }

    [Fact]
    public void ShouldHaveViolationAtSites_IdentityMatchingTwoViolations_Reds()
    {
        // Both catches share a source, so the identity selects two. Selecting with Single() would raise an
        // InvalidOperationException naming nothing, which is an error rather than a red.
        var sharedSource = ("source", "App.Handler");

        Should.Throw<ShouldAssertException>(() => Report.ShouldHaveViolationAtSites("ex/two-catches", sharedSource, 1));
    }

    [Fact]
    public void AnyAssertion_RuleNotInTheReport_RedsNamingTheRulesItDoesCarry()
    {
        // "The rule is absent" is the regression these assertions exist to catch, so the message has to say
        // both what was asked for and what there was to ask for.
        var failure = Should.Throw<ShouldAssertException>(() => Report.ShouldHavePassed("ex/absent"));

        failure.Message.ShouldContain("ex/absent");
        failure.Message.ShouldContain("ex/one-catch");
        failure.Message.ShouldContain("ex/two-catches");
    }

    [Fact]
    public void AnyAssertion_TextThatIsNotJson_RedsCarryingTheText()
    {
        // An MCP tool's text reaches these helpers with nothing else parsing it, so a refusal or a truncated
        // payload has to arrive as a failure carrying what was written, not as a raw JsonException.
        const string refusal = "error: the spec assembly was not found";

        var failure = Should.Throw<ShouldAssertException>(() => refusal.ShouldHavePassed("ex/one-catch"));

        failure.Message.ShouldContain(refusal);
    }
}
