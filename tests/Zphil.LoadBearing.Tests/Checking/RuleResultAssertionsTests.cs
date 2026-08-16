using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The negative case for <see cref="RuleResultAssertions" />: an assertion helper that never throws
///     passes silently at every site that calls it, so the one fact worth pinning is that it reds.
/// </summary>
public sealed class RuleResultAssertionsTests
{
    [Fact]
    public void ShouldHaveFailedWithEdge_KindMismatch_Reds()
    {
        // A Catch violation asserted as a Reference: the batched property conditions must surface it rather
        // than swallow it, which is what makes the helper safe to use at a hundred sites.
        RuleResult result = Checker.Run(
                """
                namespace Errors { public class DbError : System.Exception {} }
                namespace App
                {
                    public class Handler
                    {
                        public void Run() { try { } catch (Errors.DbError) { } }
                    }
                }
                """,
                arch => arch.Rule("ex/no-catch")
                    .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        Should.Throw<ShouldAssertException>(() => result.ShouldHaveFailedWithEdge(ViolationKind.Reference, "App.Handler", "Errors.DbError"));
    }
}
