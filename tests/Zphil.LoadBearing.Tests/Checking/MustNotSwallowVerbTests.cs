using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The rethrow-aware catch verb <c>MustNotSwallow</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3): a catch
///     edge trips only where a subject <c>catch</c>es a forbidden type at a clause that spells no <c>when</c>
///     filter <em>and</em> does not end in a <c>throw</c>.
/// </summary>
/// <remarks>
///     The pin neither sibling can carry is the
///     differentiator — one scene, four handlers over one banned type, checked under both verbs: the unfiltered
///     swallow is red under both, the two rethrowing handlers are red under <c>MustNotCatchUnfiltered</c> and
///     <b>green</b> here, and the filtered one is green under both. Beside it the syntactic honesty boundary,
///     both ways: a terminal <c>throw</c> that is not on every path still reads as throwing, and a throw on some
///     path that is not the last statement still reads as swallowing. The rest clones the sibling: matching by
///     exact definition-level FQN; a hierarchy-adjective operand; the type-pair ratchet with a bystander that
///     stays red; the inert-target warning on an empty pattern operand vs. the silent win on an absent bare
///     <c>typeof</c>; and the pinned human line + JSON kind. The verb reuses <see cref="ViolationKind.Catch" />
///     — the kind names the fact family, not the verb — so identity stays the (source, caught) type pair riding
///     <see cref="BaselineEntry.ForEdge" /> unchanged, and the swallowing sites are evidence, never identity.
/// </remarks>
public sealed class MustNotSwallowVerbTests
{
    // Errors.DbError is the domain exception nobody may hold and continue on. Four handlers catch exactly that
    // type: Swallower holds it (red under both catch verbs); Rethrower cleans up and rethrows and Translator
    // wraps and throws (both red under MustNotCatchUnfiltered, both GREEN here — they suppress nothing);
    // FilteredHandler names its expectation in a `when` filter (green under both).
    private const string Scene = """
                                 namespace Errors
                                 {
                                     public class DbError : System.Exception {}
                                 }
                                 namespace App
                                 {
                                     public class Swallower
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError) { }
                                         }
                                     }
                                     public class Rethrower
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError) { Cleanup(); throw; }
                                         }
                                         private void Cleanup() {}
                                     }
                                     public class Translator
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError e) { throw new System.InvalidOperationException("x", e); }
                                         }
                                     }
                                     public class FilteredHandler
                                     {
                                         public void Run(bool flag)
                                         {
                                             try { }
                                             catch (Errors.DbError) when (flag) { }
                                         }
                                     }
                                 }
                                 """;

    // One (source, caught) pair caught twice in one type — rethrowing at line 9, swallowing at line 11. Extraction
    // records both as unfiltered sites and only the second as swallowing, which is what the evidence-subset pin
    // reads.
    private const string MixedScene = """
                                      namespace Errors { public class DbError : System.Exception {} }
                                      namespace App
                                      {
                                          public class MixedHandler
                                          {
                                              public void Run()
                                              {
                                                  try { }
                                                  catch (Errors.DbError) { throw; }
                                                  try { }
                                                  catch (Errors.DbError) { }
                                              }
                                          }
                                      }
                                      """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel MixedModel = CompilationFactory.Extract(MixedScene);

    [Fact]
    public void MustNotSwallow_SubjectSwallowsBannedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-swallowed-domain-errors")
                    .Enforce(arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdge(ViolationKind.Catch, "App.Swallower", "Errors.DbError");

        // The subject covers all four handlers, and only the one that holds the failure is in the report — the
        // verb's whole point, stated as a complete list.
        result.CatchPairs().ShouldBe(["App.Swallower -> Errors.DbError"]);

        string block = result.HumanBlock();
        block.ShouldContain("App.Swallower catches Errors.DbError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotSwallow_TheDifferentiator_RethrowingHandlersAreGreenHereAndRedUnderMustNotCatchUnfiltered()
    {
        // The verb's differentiator, both ways round on one scene. Under MustNotCatchUnfiltered all three
        // unfiltered handlers red, including the two that suppress nothing — the exact cost that held nine
        // rethrow sites hostage. Under MustNotSwallow only the handler that holds the failure and continues
        // reds, and the filtered handler is green under both.
        RuleResult unfiltered = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/unfiltered")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        unfiltered.CatchPairs().ShouldBe([
            "App.Rethrower -> Errors.DbError",
            "App.Swallower -> Errors.DbError",
            "App.Translator -> Errors.DbError"
        ]);

        RuleResult swallow = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/swallow")
                    .Enforce(arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        swallow.CatchPairs().ShouldBe(["App.Swallower -> Errors.DbError"]);
    }

    [Fact]
    public void MustNotSwallow_EveryCatchSiteRethrows_PassesCleanWithNoWarnings()
    {
        // The all-rethrowing-is-green pin. Rethrower catches the banned Errors.DbError with no filter — the edge
        // exists, and MustNotCatchUnfiltered would red it — but its clause ends in a throw, so the swallowing
        // subset is empty and the rule passes. The forbidden target resolves (Errors.DbError exists), so this is
        // a real pass, not an inert one.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-swallowed-domain-errors")
                    .Enforce(arch.Namespace("App.*").WithSuffix("Rethrower")
                        .MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotSwallow_EveryCatchSiteFiltered_PassesCleanWithNoWarnings()
    {
        // A filtered swallow stays lawful: the filter is where the handler named its expectations, so the house
        // form the sibling verb rewards keeps passing under the refinement.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-swallowed-domain-errors")
                    .Enforce(arch.Namespace("App.*").WithSuffix("FilteredHandler")
                        .MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotSwallow_EdgeMixesRethrowingAndSwallowingSites_EvidenceIsTheSwallowingSitesOnly()
    {
        // The evidence-subset pin. One (source, caught) edge, two unfiltered catch sites: the rethrowing one at
        // line 9 and the swallowing one at line 11. The edge violates because a site swallows, and the violation
        // carries the swallowing site ALONE — so every printed file:line is a site the rule actually objects to,
        // which is what lets the verb reuse the Catch kind and its "{source} catches {target}" line without
        // printing a falsehood.
        MixedModel.CatchEdge("App.MixedHandler", "Errors.DbError").UnfilteredLines().ShouldBe([9, 11]);

        RuleResult result = Checker.Run(MixedModel, arch =>
                arch.Rule("ex/no-swallowed-domain-errors")
                    .Enforce(arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Sites.Select(site => site.Line).ShouldBe([11]);

        string block = result.HumanBlock();
        block.ShouldContain("Test.cs:11 — App.MixedHandler catches Errors.DbError");
        block.ShouldNotContain("Test.cs:9");
    }

    [Fact]
    public void MustNotSwallow_ReturnBeforeATerminalThrow_IsGreenAndAThrowThatIsNotLastIsRed()
    {
        // The documented honesty boundary, pinned from both sides. EarlyReturn can leave its handler having
        // suppressed the failure, and is GREEN, because the fact is the block's last STATEMENT and never an
        // all-paths flow analysis. NotLast throws on one path and is RED, for the same syntactic reason. Stating
        // the boundary in a test is what keeps it a documented limit rather than a discovered surprise.
        const string source = """
                              namespace Errors { public class DbError : System.Exception {} }
                              namespace App
                              {
                                  public class EarlyReturn
                                  {
                                      public void Run(bool flag)
                                      {
                                          try { } catch (Errors.DbError) { if (flag) return; throw; }
                                      }
                                  }
                                  public class NotLast
                                  {
                                      public void Run(bool flag)
                                      {
                                          try { } catch (Errors.DbError) { if (flag) throw; Cleanup(); }
                                      }
                                      private void Cleanup() {}
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/no-swallowed-domain-errors")
                    .Enforce(arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.NotLast -> Errors.DbError"]);
    }

    [Fact]
    public void MustNotSwallow_BansExceptionButSwallowsNarrowerType_NarrowCatchNotFlagged()
    {
        // Matching is exact definition-level FQN, not hierarchy: banning typeof(Exception) flags the broad
        // swallowing `catch (System.Exception)` but NEVER the narrower `catch (IOException)`, swallowing or not —
        // the narrow catch is a good state in its own right. Broad's presence proves the ban is live.
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
                arch.Rule("ex/no-swallowed-broad-catches")
                    .Enforce(arch.Types.MustNotSwallow(typeof(Exception)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.Broad -> System.Exception"]);
    }

    [Fact]
    public void MustNotSwallow_DerivedFromOperand_MatchesSolutionExceptionNotExternal()
    {
        // A hierarchy-adjective operand (arch.Types.DerivedFrom) ranges over solution-declared types only —
        // external types carry a shallow hierarchy. Worker swallows its own N.AppError (solution, derives from
        // Exception → matched → red) and an external System.InvalidOperationException, also swallowed but never
        // matched by a DerivedFrom operand, so not flagged.
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
                arch.Rule("ex/no-swallowed-derived-errors")
                    .Enforce(arch.Namespace("N.*").MustNotSwallow(arch.Types.DerivedFrom(typeof(Exception))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["N.Worker -> N.AppError"]);
    }

    [Fact]
    public void MustNotSwallow_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate: the verb joins the forbidden-set inert-warn family (§4.1) exactly
        // as its siblings do.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/inert")
                    .Enforce(arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void MustNotSwallow_AbsentTypeofTarget_IsSilentWinNoWarning()
    {
        // A bare typeof target absent from the codebase is the WIN condition, not an inert warning: nobody
        // catches System.FormatException at all, so the ban resolves empty but — being a concrete typeof anchor,
        // not a pattern — stays silent (the departure from the pattern-operand inert warning above).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/no-swallowed-format-errors")
                    .Enforce(arch.Namespace("App.*").MustNotSwallow(typeof(FormatException)))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotSwallow_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the rethrow-aware catch verb takes the same subject gate.
        RuleResult result = Checker.Run(
            "namespace App { public class Foo {} }",
            arch => arch.Rule("ex/empty")
                .Enforce(arch.Namespace("Nowhere.*").MustNotSwallow(arch.Namespace("App.*")))
                .Because("b")).Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustNotSwallow_GrandfatheredEdgePasses_NewSwallowStaysRed()
    {
        // Identity is the (source, caught) type pair (GRAMMAR §4.3) — the swallowing sites are evidence, not
        // identity, so a baseline entry written for any of the three catch verbs keys the same edge. Handler is
        // grandfathered for catching AErr; its NEW swallowed BErr is a distinct identity → red.
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
        BaselineIndex index = Index("ex/no-swallowed-errors", BaselineEntry.ForEdge("T:App.Handler", "T:Errors.AErr"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/no-swallowed-errors")
                    .Migrate("legacy swallowed catches", arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("a handler that holds a failure and continues hides it"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.Handler -> Errors.BErr"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void MustNotSwallow_BystanderSwallow_StaysRedWhenAnotherEdgeBaselined()
    {
        // Two handlers swallow the same Errors.Err; only OldHandler's edge is grandfathered. NewHandler swallowing
        // the identical type is a distinct (source, caught) identity — a bystander — so it stays red.
        const string source = """
                              namespace Errors { public class Err : System.Exception {} }
                              namespace App
                              {
                                  public class OldHandler { public void Run() { try { } catch (Errors.Err) { } } }
                                  public class NewHandler { public void Run() { try { } catch (Errors.Err) { } } }
                              }
                              """;
        BaselineIndex index = Index("ex/no-swallowed-errors", BaselineEntry.ForEdge("T:App.OldHandler", "T:Errors.Err"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/no-swallowed-errors")
                    .Migrate("legacy swallowed catches", arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Errors.*")))
                    .Because("a handler that holds a failure and continues hides it"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.NewHandler -> Errors.Err"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void JsonReportRenderer_SwallowViolation_EmitsCatchKindAndTargetAndOmitsMemberSlots()
    {
        // Kind reuse reaches the wire: the JSON kind string is "catch", the caught type rides the existing
        // `target` field, and schemaVersion stays 3 — a third verb on a recorded fact family adds no vocabulary a
        // hook would have to learn.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("ex/no-swallowed-domain-errors")
                .Enforce(arch.Namespace("App.*").MustNotSwallow(arch.Namespace("Errors.*")))
                .Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, [], false, []);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(3);
        JsonElement violation = document.RootElement.GetProperty("rules")[0].GetProperty("violations")[0];
        violation.GetProperty("kind").GetString().ShouldBe("catch");
        violation.GetProperty("source").GetString().ShouldBe("App.Swallower");
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
