using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The throw-ban verb <c>MustNotThrow</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3): the ban polarity
///     beside the strict allow-list <c>MustOnlyThrow</c>, for the case where the forbidden thrown types are
///     enumerable and the permitted ones are not.
/// </summary>
/// <remarks>
///     A throw edge trips only where a subject throws a forbidden
///     type, matched by exact definition-level FQN (banning <c>typeof(Exception)</c> never flags a derived
///     <c>throw new InvalidOperationException()</c> — the narrow throw is the good state); a hierarchy-adjective
///     operand matches solution-declared exception types but never an external one; the type-pair ratchet
///     (one edge grandfathered, a new pair stays red); the inert-target warning on an empty pattern operand
///     vs. the silent win on an absent bare <c>typeof</c> — the departure from <c>MustOnlyThrow</c>, which
///     never warns; and the pinned
///     human line + JSON kind. The verb reuses <see cref="ViolationKind.Throw" /> — the kind names the fact
///     family, not the verb — so identity is the (source, thrown) type pair riding
///     <see cref="BaselineEntry.ForEdge" /> unchanged.
/// </remarks>
public sealed class MustNotThrowVerbTests
{
    // Service throws a permitted Errors.DomainError and a banned Errors.InfraError; CleanService throws only an
    // external OperationCanceledException — a throw, but not a forbidden one, so the verb is silent on it.
    private const string Scene = """
                                 namespace Errors
                                 {
                                     public class DomainError : System.Exception {}
                                     public class InfraError : System.Exception {}
                                 }
                                 namespace App
                                 {
                                     public class Service
                                     {
                                         public void Run(bool b)
                                         {
                                             if (b) throw new Errors.DomainError();
                                             throw new Errors.InfraError();
                                         }
                                     }
                                     public class CleanService
                                     {
                                         public void Run() => throw new System.OperationCanceledException();
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustNotThrow_SubjectThrowsBannedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        // Service throws DomainError (unbanned → silent) and InfraError (banned → red): the ban flags only the
        // listed type, and the unlisted throw passing proves the ban enumerates rather than allow-lists.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-infra-throws")
                    .Enforce(arch.Namespace("App.*").MustNotThrow(arch.Namespace("Errors.*").WithSuffix("InfraError")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdge(ViolationKind.Throw, "App.Service", "Errors.InfraError");

        string block = result.HumanBlock();
        block.ShouldContain("App.Service throws Errors.InfraError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotThrow_SubjectThrowsUnbannedType_PassesCleanWithNoWarnings()
    {
        // CleanService throws an external OperationCanceledException — a throw, but outside the banned Errors.*
        // namespace, so the ban is silent. The forbidden target resolves (the Errors types exist), so this is a
        // real pass, not an inert one.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-domain-throws")
                    .Enforce(arch.Namespace("App.*").WithSuffix("CleanService")
                        .MustNotThrow(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotThrow_BansExceptionButThrowsNarrowerType_NarrowThrowNotFlagged()
    {
        // Matching is exact definition-level FQN, not hierarchy: banning typeof(Exception) flags the bare
        // `throw new System.Exception()` but NEVER the narrower `throw new InvalidOperationException()` — the
        // narrow throw is the good state the rule steers toward, exactly as with the catch verbs. Broad's
        // presence proves the ban is live, not vacuously empty.
        const string source = """
                              namespace App
                              {
                                  public class Broad
                                  {
                                      public void Run() => throw new System.Exception();
                                  }
                                  public class Narrow
                                  {
                                      public void Run() => throw new System.InvalidOperationException();
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/no-bare-throws")
                    .Enforce(arch.Types.MustNotThrow(typeof(Exception)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.ThrowPairs()
            .ShouldBe(["App.Broad -> System.Exception"]);
    }

    [Fact]
    public void MustNotThrow_DerivedFromOperand_MatchesSolutionExceptionNotExternal()
    {
        // A hierarchy-adjective operand (arch.Types.DerivedFrom) ranges over solution-declared types only —
        // external types carry a shallow hierarchy. Worker throws its own N.AppError (solution, derives from
        // Exception → matched → red) and an external System.InvalidOperationException (never matched by a
        // DerivedFrom operand, so not flagged), pinning that the adjective matches solution types but not externals.
        const string source = """
                              namespace N
                              {
                                  public class AppError : System.Exception {}
                                  public class Worker
                                  {
                                      public void Run(bool b)
                                      {
                                          if (b) throw new N.AppError();
                                          throw new System.InvalidOperationException();
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/no-derived-throws")
                    .Enforce(arch.Namespace("N.*").MustNotThrow(arch.Types.DerivedFrom(typeof(Exception))))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.ThrowPairs()
            .ShouldBe(["N.Worker -> N.AppError"]);
    }

    [Fact]
    public void MustNotThrow_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate: the verb joins the forbidden-set inert-warn family (§4.1), which
        // is exactly where it parts company with MustOnlyThrow's never-warns rule.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/inert")
                    .Enforce(arch.Namespace("App.*").MustNotThrow(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void MustNotThrow_AbsentTypeofTarget_IsSilentWinNoWarning()
    {
        // A bare typeof target absent from the codebase is the WIN condition, not an inert warning: nobody throws
        // System.FormatException, so the ban resolves empty but — being a concrete typeof anchor, not a pattern —
        // stays silent. This is the shape a forward tripwire takes: a ban nobody trips renders no noise.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-format-throws")
                    .Enforce(arch.Namespace("App.*").MustNotThrow(typeof(FormatException)))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotThrow_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the throw-ban verb takes the same subject gate.
        RuleResult result = Checker.Run(
                "namespace App { public class Foo {} }",
                arch => arch.Rule("ex/empty")
                    .Enforce(arch.Namespace("Nowhere.*").MustNotThrow(arch.Namespace("App.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustNotThrow_GrandfatheredEdgePasses_NewThrowStaysRed()
    {
        // The throw ratchet keys the (source, thrown) type pair (GRAMMAR §4.3): Worker is grandfathered for
        // throwing Alpha, but its throw of the equally banned Beta is a distinct identity → red. Throw sites are
        // evidence, not identity, so the entry means the same thing it would under MustOnlyThrow.
        const string source = """
                              namespace N { public class Alpha : System.Exception {} public class Beta : System.Exception {} }
                              namespace N
                              {
                                  public class Worker
                                  {
                                      public void Run(int x)
                                      {
                                          if (x == 0) throw new N.Alpha();
                                          throw new N.Beta();
                                      }
                                  }
                              }
                              """;
        BaselineIndex index = Checker.Baselines("ex/no-throw", BaselineEntry.ForEdge("T:N.Worker", "T:N.Alpha"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/no-throw")
                    .Migrate("legacy bare throws", arch.Namespace("N.*").MustNotThrow(arch.Namespace("N.*")))
                    .Because("throw types a caller can dispatch on"))
            .Single();

        result.ShouldHaveFailed();
        result.ThrowPairs()
            .ShouldBe(["N.Worker -> N.Beta"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void JsonReportRenderer_ForbiddenThrowViolation_EmitsThrowKindAndTargetAndOmitsMemberSlots()
    {
        // Kind reuse reaches the wire: the JSON kind string is "throw", the thrown type rides the existing
        // `target` field, and schemaVersion stays 3 — the ban polarity adds no vocabulary a hook would have to
        // learn beside the allow-list's.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("ex/no-infra-throws")
                .Enforce(arch.Namespace("App.*").MustNotThrow(arch.Namespace("Errors.*").WithSuffix("InfraError")))
                .Because("b"));

        report.ShouldRenderEdgeViolation("throw", "App.Service", "Errors.InfraError");
    }
}
