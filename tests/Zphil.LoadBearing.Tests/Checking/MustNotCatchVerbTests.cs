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
///     The catch verb <c>MustNotCatch</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3): a catch edge trips
///     only where a subject <c>catch</c>es a forbidden type, matched by exact definition-level FQN (banning
///     <c>typeof(Exception)</c> never flags a narrower <c>catch (IOException)</c> — the narrow catch is the
///     good state); a hierarchy-adjective operand matches solution-declared exception types but never an
///     external one; the type-pair ratchet (grandfather one edge, bystanders stay red); the inert-target
///     warning on an empty pattern operand vs. the silent win on an absent bare <c>typeof</c>; and the pinned
///     human line + JSON kind. Violation identity is the (source, caught) type pair, riding
///     <see cref="BaselineEntry.ForEdge" /> unchanged.
/// </summary>
public sealed class MustNotCatchVerbTests
{
    // Errors.DbError is the domain exception nobody may swallow. DataHandler catches it (the red edge);
    // CleanHandler catches only an external OperationCanceledException outside the banned namespace — a catch,
    // but not a forbidden one, so the verb is silent on it.
    private const string Scene = """
                                 namespace Errors
                                 {
                                     public class DbError : System.Exception {}
                                 }
                                 namespace App
                                 {
                                     public class DataHandler
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError) { }
                                         }
                                     }
                                     public class CleanHandler
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (System.OperationCanceledException) { }
                                         }
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustNotCatch_SubjectCatchesBannedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-catch-domain")
                    .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.Catch);
        violation.Source!.FullName.ShouldBe("App.DataHandler");
        violation.Target!.FullName.ShouldBe("Errors.DbError");
        violation.Sites.ShouldNotBeEmpty();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain("App.DataHandler catches Errors.DbError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotCatch_SubjectCatchesUnbannedType_PassesCleanWithNoWarnings()
    {
        // CleanHandler catches an external OperationCanceledException — a catch, but outside the banned
        // Errors.* namespace, so the ban is silent. The forbidden target resolves (Errors.DbError exists), so
        // this is a real pass, not an inert one.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-catch-domain")
                    .Enforce(arch.Namespace("App.*").WithSuffix("CleanHandler").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatch_BansExceptionButCatchesNarrowerType_NarrowCatchNotFlagged()
    {
        // Matching is exact definition-level FQN, not hierarchy: MustNotCatch(typeof(Exception)) flags the broad
        // `catch (System.Exception)` but NEVER the narrower `catch (IOException)` — the narrow catch is the good
        // state the verb rewards. Broad's presence proves the ban is live, not vacuously empty.
        const string source = """
                              namespace App
                              {
                                  public class Broad
                                  {
                                      public void Run() { try { } catch (System.Exception) { } }
                                  }
                                  public class Narrow
                                  {
                                      public void Run() { try { } catch (System.IO.IOException) { } }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/no-catch-all")
                    .Enforce(arch.Types.MustNotCatch(typeof(Exception)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.Broad -> System.Exception"]);
    }

    [Fact]
    public void MustNotCatch_DerivedFromOperand_MatchesSolutionExceptionNotExternal()
    {
        // A hierarchy-adjective operand (arch.Types.DerivedFrom) ranges over solution-declared types only —
        // external types carry a shallow hierarchy. Worker catches its own N.AppError (solution, derives from
        // Exception → matched → red) and an external System.InvalidOperationException (never matched by a
        // DerivedFrom operand, so not flagged), pinning that the adjective matches solution types but not externals.
        const string source = """
                              namespace N
                              {
                                  public class AppError : System.Exception {}
                                  public class Worker
                                  {
                                      public void Run()
                                      {
                                          try { } catch (N.AppError) { }
                                          try { } catch (System.InvalidOperationException) { }
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/no-catch-derived")
                    .Enforce(arch.Namespace("N.*").MustNotCatch(arch.Types.DerivedFrom(typeof(Exception))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["N.Worker -> N.AppError"]);
    }

    [Fact]
    public void MustNotCatch_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate, exactly as MustNotConstruct's inert-target semantics (§4.8).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/inert")
                    .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(CheckWarningKind.InertTarget);
        warning.Message.ShouldBe("This rule is inert: its target selection matched no types.");
    }

    [Fact]
    public void MustNotCatch_AbsentTypeofTarget_IsSilentWinNoWarning()
    {
        // A bare typeof target absent from the codebase is the WIN condition, not an inert warning: nobody
        // catches System.FormatException, so the ban resolves empty but — being a concrete typeof anchor, not a
        // pattern — stays silent (the departure from the pattern-operand inert warning above).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-catch-format")
                    .Enforce(arch.Namespace("App.*").MustNotCatch(typeof(FormatException)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatch_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the catch verb takes the same subject gate.
        RuleResult result = Checker.Run(
            "namespace App { public class Foo {} }",
            arch => arch.Rule("ex/empty")
                .Enforce(arch.Namespace("Nowhere.*").MustNotCatch(arch.Namespace("App.*")))
                .Because("b")).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe(ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustNotCatch_GrandfatheredEdgePasses_NewCatchStaysRed()
    {
        // The catch ratchet keys the (source, caught) type pair (GRAMMAR §4.3): Handler is grandfathered for
        // catching AErr, but its NEW `catch (BErr)` is a distinct identity → red.
        const string source = """
                              namespace Errors { public class AErr : System.Exception {} public class BErr : System.Exception {} }
                              namespace App
                              {
                                  public class Handler
                                  {
                                      public void Run()
                                      {
                                          try { } catch (Errors.AErr) { }
                                          try { } catch (Errors.BErr) { }
                                      }
                                  }
                              }
                              """;
        BaselineIndex index = Index("ex/no-catch", BaselineEntry.ForEdge("T:App.Handler", "T:Errors.AErr"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/no-catch")
                    .Migrate("legacy broad catches", arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("catch specific exceptions"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.Handler -> Errors.BErr"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void MustNotCatch_BystanderCatch_StaysRedWhenAnotherEdgeBaselined()
    {
        // Two handlers catch the same Errors.Err; only OldHandler's edge is grandfathered. NewHandler catching
        // the identical type is a distinct (source, caught) identity — a bystander — so it stays red.
        const string source = """
                              namespace Errors { public class Err : System.Exception {} }
                              namespace App
                              {
                                  public class OldHandler { public void Run() { try { } catch (Errors.Err) { } } }
                                  public class NewHandler { public void Run() { try { } catch (Errors.Err) { } } }
                              }
                              """;
        BaselineIndex index = Index("ex/no-catch", BaselineEntry.ForEdge("T:App.OldHandler", "T:Errors.Err"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/no-catch")
                    .Migrate("legacy broad catches", arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("catch specific exceptions"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.NewHandler -> Errors.Err"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void JsonReportRenderer_CatchViolation_EmitsCatchKindAndTargetAndOmitsMemberSlots()
    {
        // The JSON kind string is "catch" and the caught type rides the existing `target` field — no new slot,
        // schemaVersion stays 3, so member/subject slots stay omitted (null) as before.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("ex/no-catch-domain")
                .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                .Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, []);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(3);
        JsonElement violation = document.RootElement.GetProperty("rules")[0].GetProperty("violations")[0];
        violation.GetProperty("kind").GetString().ShouldBe("catch");
        violation.GetProperty("source").GetString().ShouldBe("App.DataHandler");
        violation.GetProperty("target").GetString().ShouldBe("Errors.DbError");
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