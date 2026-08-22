using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The negative case for <see cref="RuleResultAssertions" />: an assertion helper that never throws
///     passes silently at every site that calls it, so the one fact worth pinning is that it reds.
/// </summary>
/// <remarks>
///     A documented tolerance needs the same protection as a documented refusal, so the unordered comparison
///     is pinned here too, positively: a later tightening to ordered would otherwise red in whichever swept
///     row happened to spell its set in reading order rather than here, where the decision is recorded.
/// </remarks>
public sealed class RuleResultAssertionsTests
{
    private const string OneCatch = """
                                    namespace Errors { public class DbError : System.Exception {} }
                                    namespace App
                                    {
                                        public class Handler
                                        {
                                            public void Run() { try { } catch (Errors.DbError) { } }
                                        }
                                    }
                                    """;

    private const string TwoCatches = """
                                      namespace Errors { public class DbError : System.Exception {} }
                                      namespace App
                                      {
                                          public class Alpha
                                          {
                                              public void Run() { try { } catch (Errors.DbError) { } }
                                          }
                                          public class Beta
                                          {
                                              public void Run() { try { } catch (Errors.DbError) { } }
                                          }
                                      }
                                      """;

    [Fact]
    public void ShouldHaveFailedWithSingleEdge_KindMismatch_Reds()
    {
        // A Catch violation asserted as a Reference: the batched property conditions must surface it rather
        // than swallow it, which is what makes the helper safe to use at a hundred sites.
        RuleResult result = Caught(OneCatch);

        Action assertion = () => result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Handler", "Errors.DbError");

        Should.Throw<ShouldAssertException>(assertion);
    }

    [Fact]
    public void ShouldHaveFailedWithEdges_KindMismatch_Reds()
    {
        // The same mismatch one grain up: filtering to the wrong kind yields an empty projection, and the
        // set comparison has to red on it rather than reading the edge off some other kind's violation.
        RuleResult result = Caught(OneCatch);

        Action assertion = () => result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Handler -> Errors.DbError"]);

        Should.Throw<ShouldAssertException>(assertion);
    }

    [Fact]
    public void ShouldHaveFailedWithEdgesIncluding_OneEdgePresentOneAbsent_Reds()
    {
        // The batched-fragment case: a present edge must not carry an absent one past the assertion, which is
        // the failure mode ShouldSatisfyAllConditions exists to prevent and nothing else here covers.
        RuleResult result = Caught(TwoCatches);

        Action assertion = () => result.ShouldHaveFailedWithEdgesIncluding(
            ViolationKind.Catch, "App.Alpha -> Errors.DbError", "App.Zeta -> Errors.DbError");

        Should.Throw<ShouldAssertException>(assertion);
    }

    [Fact]
    public void ShouldHaveFailedWithEdges_ExpectationInReverseOfReportOrder_Passes()
    {
        // The tolerance, stated positively. Report order is ordinal by source then target, so Alpha precedes
        // Beta; naming them the other way round is still the same set, and this verb says so.
        RuleResult result = Caught(TwoCatches);

        result.ShouldHaveFailedWithEdges(
            ViolationKind.Catch, ["App.Beta -> Errors.DbError", "App.Alpha -> Errors.DbError"]);
    }

    [Theory]
    [InlineData("edges")]
    [InlineData("subjects")]
    [InlineData("including")]
    public void EveryCollectionVerb_EmptyExpectation_Reds(string verb)
    {
        // An empty expectation is a silent total pass — a rule red on some other kind satisfies both the
        // status claim and empty-equals-empty — so every verb refuses it rather than reporting green.
        RuleResult result = Caught(OneCatch);

        Action assertion = verb switch
        {
            "edges" => () => result.ShouldHaveFailedWithEdges(ViolationKind.Catch, []),
            "subjects" => () => result.ShouldHaveFailedWithSubjects([]),
            _ => () => result.ShouldHaveFailedWithEdgesIncluding(ViolationKind.Catch)
        };

        Should.Throw<ShouldAssertException>(assertion);
    }

    [Fact]
    public void AnyAssertion_WhenItReds_CarriesTheWholeResultIntoTheMessage()
    {
        // The reason this vocabulary exists, pinned once: the reader of a red gets the rule, its status and
        // every violation, not two bare collections. Nothing else asserts that Describe reaches the message.
        RuleResult result = Caught(OneCatch);

        Action assertion = () => result.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.Handler -> Errors.Missing"]);

        var red = Should.Throw<ShouldAssertException>(assertion);

        red.Message.ShouldContain("violations (");
        red.Message.ShouldContain("Catch App.Handler -> Errors.DbError");
    }

    private static RuleResult Caught(string source)
    {
        return Checker.Run(source, arch => arch.Rule("ex/no-catch")
                .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                .Because("b"))
            .Single();
    }
}
