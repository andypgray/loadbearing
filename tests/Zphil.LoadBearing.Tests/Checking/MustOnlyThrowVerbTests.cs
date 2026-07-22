using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The throw verb <c>MustOnlyThrow</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3): a STRICT allow-list
///     — every throw edge whose thrown type is not in the allowed set is a violation,
///     <b>
///         external thrown types
///         included
///     </b>
///     (the point of departure from <c>MustOnlyReference</c>, which exempts external targets). A
///     <c>Type</c>-sugar operand resolves an external allowed type by FQN (so an allowed external throw passes);
///     an allowed type absent from the model resolves empty and harmlessly allows nothing; the verb
///     <b>never warns</b> (an empty allow-set is loud on its own); the (source, thrown) type-pair ratchet with a
///     bystander; and the pinned human line + JSON kind. Violation identity is the (source, thrown) type pair,
///     riding <see cref="BaselineEntry.ForEdge" /> unchanged.
/// </summary>
public sealed class MustOnlyThrowVerbTests
{
    // Service throws a permitted DomainError and a non-permitted InfraError; only DomainError is in the allow-set.
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
                                 }
                                 """;

    // A single solution throw, reused by the absent-allowed-type and never-warns pins.
    private const string OneThrow = """
                                    namespace Errors { public class MyError : System.Exception {} }
                                    namespace App
                                    {
                                        public class Service { public void Run() => throw new Errors.MyError(); }
                                    }
                                    """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel OneThrowModel = CompilationFactory.Extract(OneThrow);

    [Fact]
    public void MustOnlyThrow_SubjectThrowsDisallowedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        // Service throws DomainError (allowed → silent) and InfraError (not allowed → red): the strict allow-list
        // flags only the unlisted throw, and the allowed one passing proves the allow-set is live.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/only-domain")
                    .Enforce(arch.Namespace("App.*").MustOnlyThrow(arch.Namespace("Errors.*").WithSuffix("DomainError")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.Throw);
        violation.Source!.FullName.ShouldBe("App.Service");
        violation.Target!.FullName.ShouldBe("Errors.InfraError");
        violation.Sites.ShouldNotBeEmpty();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain("App.Service throws Errors.InfraError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustOnlyThrow_DisallowedExternalThrow_IsRed()
    {
        // The strictness pin: Service throws an EXTERNAL System.InvalidOperationException that is not in the
        // allow-set. MustOnlyReference would exempt an external target; MustOnlyThrow drops that clause, so the
        // external throw is red. The allow-set holds a solution DomainError, so the rule is live, not vacuous.
        const string source = """
                              namespace App
                              {
                                  public class DomainError : System.Exception {}
                                  public class Service
                                  {
                                      public void Run() => throw new System.InvalidOperationException();
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/only-domain")
                    .Enforce(arch.Namespace("App.*").WithSuffix("Service").MustOnlyThrow(arch.Types.WithSuffix("Error")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ThrowPairs().ShouldBe(["App.Service -> System.InvalidOperationException"]);
    }

    [Fact]
    public void MustOnlyThrow_TypeSugarAllowsExternalThrow_Passes()
    {
        // A Type-sugar allow-set member resolves an external type by FQN (the target universe includes
        // externals): MustOnlyThrow(typeof(TimeoutException)) allows Service's external TimeoutException throw,
        // so the rule passes with no violation.
        const string source = """
                              namespace App
                              {
                                  public class Service
                                  {
                                      public void Run() => throw new System.TimeoutException();
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/only-timeout")
                    .Enforce(arch.Namespace("App.*").MustOnlyThrow(typeof(TimeoutException)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustOnlyThrow_AllowedTypeAbsentFromModel_AllowsNothingAndIsRed()
    {
        // An allowed type nobody references (System.DivideByZeroException) resolves to an empty allow-set —
        // harmlessly, with no crash — so it allows nothing: Service's Errors.MyError throw is red.
        RuleResult result = Checker.Run(OneThrowModel, arch =>
                arch.Rule("ex/only-absent")
                    .Enforce(arch.Namespace("App.*").MustOnlyThrow(typeof(DivideByZeroException)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ThrowPairs().ShouldBe(["App.Service -> Errors.MyError"]);
    }

    [Fact]
    public void MustOnlyThrow_EmptyAllowSet_NeverWarns()
    {
        // The never-warns pin (the MustOnly* precedent): the allow-set (a namespace glob) resolves empty, which
        // for MustNotCatch WOULD raise an inert-target warning. MustOnlyThrow has no inert arm — an empty
        // allow-set is loud on its own (every throw is red), so the result carries violations and ZERO warnings.
        RuleResult result = Checker.Run(OneThrowModel, arch =>
                arch.Rule("ex/only-nowhere")
                    .Enforce(arch.Namespace("App.*").MustOnlyThrow(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Violations.ShouldNotBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustOnlyThrow_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the throw verb takes the same subject gate.
        RuleResult result = Checker.Run(
            "namespace App { public class Foo {} }",
            arch => arch.Rule("ex/empty")
                .Enforce(arch.Namespace("Nowhere.*").MustOnlyThrow(arch.Namespace("App.*")))
                .Because("b")).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe(ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustOnlyThrow_GrandfatheredThrowPasses_NewThrowStaysRed()
    {
        // The throw ratchet keys the (source, thrown) type pair (GRAMMAR §4.3): Worker is grandfathered for
        // throwing Alpha, but its still-unlisted throw of Beta is a distinct identity → red. The allow-set holds
        // an Allowed.Good so the rule is live rather than an empty-allow-set catch-all.
        const string source = """
                              namespace N { public class Alpha : System.Exception {} public class Beta : System.Exception {} }
                              namespace Allowed { public class Good : System.Exception {} }
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
        BaselineIndex index = Index("ex/only-throw", BaselineEntry.ForEdge("T:N.Worker", "T:N.Alpha"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/only-throw")
                    .Migrate("legacy throws", arch.Namespace("N.*").MustOnlyThrow(arch.Namespace("Allowed.*")))
                    .Because("throw only the sanctioned exceptions"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ThrowPairs().ShouldBe(["N.Worker -> N.Beta"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void MustOnlyThrow_BystanderThrow_StaysRedWhenAnotherEdgeBaselined()
    {
        // Two workers throw the same disallowed Boom; only OldWorker's edge is grandfathered. NewWorker throwing
        // the identical type is a distinct (source, thrown) identity — a bystander — so it stays red.
        const string source = """
                              namespace N { public class Boom : System.Exception {} }
                              namespace N
                              {
                                  public class OldWorker { public void Run() => throw new N.Boom(); }
                                  public class NewWorker { public void Run() => throw new N.Boom(); }
                              }
                              """;
        BaselineIndex index = Index("ex/only-throw", BaselineEntry.ForEdge("T:N.OldWorker", "T:N.Boom"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/only-throw")
                    .Migrate("legacy throws", arch.Namespace("N.*").MustOnlyThrow(arch.Namespace("Allowed.*")))
                    .Because("throw only the sanctioned exceptions"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ThrowPairs().ShouldBe(["N.NewWorker -> N.Boom"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void JsonReportRenderer_ThrowViolation_EmitsThrowKindAndTargetAndOmitsMemberSlots()
    {
        // The JSON kind string is "throw" and the thrown type rides the existing `target` field — no new slot,
        // schemaVersion stays 3, so member/subject slots stay omitted (null) as before.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("ex/only-domain")
                .Enforce(arch.Namespace("App.*").MustOnlyThrow(arch.Namespace("Errors.*").WithSuffix("DomainError")))
                .Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, []);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(3);
        JsonElement violation = document.RootElement.GetProperty("rules")[0].GetProperty("violations")[0];
        violation.GetProperty("kind").GetString().ShouldBe("throw");
        violation.GetProperty("source").GetString().ShouldBe("App.Service");
        violation.GetProperty("target").GetString().ShouldBe("Errors.InfraError");
        violation.TryGetProperty("targetMember", out _).ShouldBeFalse();
        violation.TryGetProperty("subject", out _).ShouldBeFalse();
        violation.TryGetProperty("subjectMember", out _).ShouldBeFalse();
        violation.GetProperty("sites").GetArrayLength().ShouldBeGreaterThan(0);
    }

    private static BaselineIndex Index(string ruleId, params BaselineEntry[] entries)
    {
        return new BaselineIndex(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal)
        {
            [ruleId] = new(entries)
        });
    }
}