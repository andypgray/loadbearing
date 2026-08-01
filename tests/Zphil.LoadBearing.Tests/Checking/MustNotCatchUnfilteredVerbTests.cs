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
///     The filter-aware catch verb <c>MustNotCatchUnfiltered</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3):
///     a catch edge trips only where a subject <c>catch</c>es a forbidden type at a clause that spells no
///     <c>when</c> filter. The two pins the sibling <c>MustNotCatch</c> cannot carry are here — an edge whose
///     every site is filtered is <b>green</b>, and a mixed edge's evidence is the <b>unfiltered sites only</b>,
///     so no reader is ever pointed at a site the rule considers good. The rest clones the sibling: matching by
///     exact definition-level FQN (banning <c>typeof(Exception)</c> never flags a narrower
///     <c>catch (IOException)</c>); a hierarchy-adjective operand matching solution-declared exception types but
///     never an external one; the type-pair ratchet with a bystander that stays red; the inert-target warning on
///     an empty pattern operand vs. the silent win on an absent bare <c>typeof</c>; and the pinned human line +
///     JSON kind. The verb reuses <see cref="ViolationKind.Catch" /> — the kind names the fact family, not the
///     verb — so identity stays the (source, caught) type pair riding <see cref="BaselineEntry.ForEdge" />
///     unchanged, and the unfiltered sites are evidence, never identity.
/// </summary>
public sealed class MustNotCatchUnfilteredVerbTests
{
    // Errors.DbError is the domain exception nobody may swallow blind. DataHandler catches it with no filter
    // (the red edge); FilteredHandler catches the very same type behind a `when` filter — a catch, and a catch of
    // a banned type, but the good state this verb exists to reward, so the verb is silent on it.
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

    // One (source, caught) pair caught twice in one type — filtered at line 9, unfiltered at line 11. Extraction
    // records both as sites and only the second as unfiltered, which is what the evidence-subset pin reads.
    private const string MixedScene = """
                                      namespace Errors { public class DbError : System.Exception {} }
                                      namespace App
                                      {
                                          public class MixedHandler
                                          {
                                              public void Run(bool flag)
                                              {
                                                  try { }
                                                  catch (Errors.DbError) when (flag) { }
                                                  try { }
                                                  catch (Errors.DbError) { }
                                              }
                                          }
                                      }
                                      """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel MixedModel = CompilationFactory.Extract(MixedScene);

    [Fact]
    public void MustNotCatchUnfiltered_SubjectCatchesBannedTypeUnfiltered_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/filter-domain-catches")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.Catch);
        violation.Source!.FullName.ShouldBe("App.DataHandler");
        violation.Target!.FullName.ShouldBe("Errors.DbError");
        violation.Sites.ShouldNotBeEmpty();

        // The subject covers FilteredHandler too, and its identical catch of the identical type is absent from the
        // report — the verb's whole point, stated as a complete list.
        result.CatchPairs().ShouldBe(["App.DataHandler -> Errors.DbError"]);

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain("App.DataHandler catches Errors.DbError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotCatchUnfiltered_EveryCatchSiteFiltered_PassesCleanWithNoWarnings()
    {
        // The all-filtered-is-green pin. FilteredHandler catches the banned Errors.DbError — the edge exists, and
        // plain MustNotCatch would red it — but every site of that edge spells a `when` filter, so the unfiltered
        // subset is empty and the rule passes. The forbidden target resolves (Errors.DbError exists), so this is a
        // real pass, not an inert one.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/filter-domain-catches")
                    .Enforce(arch.Namespace("App.*").WithSuffix("FilteredHandler")
                        .MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatchUnfiltered_EdgeMixesFilteredAndUnfilteredSites_EvidenceIsTheUnfilteredSitesOnly()
    {
        // The evidence-subset pin. One (source, caught) edge, two catch sites: the filtered one at line 9 and the
        // unfiltered one at line 11. The edge violates because a site is unfiltered, and the violation carries the
        // unfiltered site ALONE — so every printed file:line is a site the rule actually objects to, which is what
        // lets the verb reuse the Catch kind and its "{source} catches {target}" line without printing a falsehood.
        MixedModel.CatchEdge("App.MixedHandler", "Errors.DbError").Lines().ShouldBe([9, 11]);

        RuleResult result = Checker.Run(MixedModel, arch =>
                arch.Rule("ex/filter-domain-catches")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Sites.Select(site => site.Line).ShouldBe([11]);

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain("Test.cs:11 — App.MixedHandler catches Errors.DbError");
        block.ShouldNotContain("Test.cs:9");
    }

    [Fact]
    public void MustNotCatchUnfiltered_BansExceptionButCatchesNarrowerType_NarrowCatchNotFlagged()
    {
        // Matching is exact definition-level FQN, not hierarchy: banning typeof(Exception) flags the broad
        // unfiltered `catch (System.Exception)` but NEVER the narrower `catch (IOException)`, filtered or not —
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
                arch.Rule("ex/filter-broad-catches")
                    .Enforce(arch.Types.MustNotCatchUnfiltered(typeof(Exception)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.Broad -> System.Exception"]);
    }

    [Fact]
    public void MustNotCatchUnfiltered_DerivedFromOperand_MatchesSolutionExceptionNotExternal()
    {
        // A hierarchy-adjective operand (arch.Types.DerivedFrom) ranges over solution-declared types only —
        // external types carry a shallow hierarchy. Worker catches its own N.AppError unfiltered (solution,
        // derives from Exception → matched → red) and an external System.InvalidOperationException, also
        // unfiltered but never matched by a DerivedFrom operand, so not flagged.
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
                arch.Rule("ex/filter-derived-catches")
                    .Enforce(arch.Namespace("N.*").MustNotCatchUnfiltered(arch.Types.DerivedFrom(typeof(Exception))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["N.Worker -> N.AppError"]);
    }

    [Fact]
    public void MustNotCatchUnfiltered_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate: the verb joins the forbidden-set inert-warn family (§4.1) exactly
        // as its sibling does.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/inert")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(CheckWarningKind.InertTarget);
        warning.Message.ShouldBe("This rule is inert: its target selection matched no types.");
    }

    [Fact]
    public void MustNotCatchUnfiltered_AbsentTypeofTarget_IsSilentWinNoWarning()
    {
        // A bare typeof target absent from the codebase is the WIN condition, not an inert warning: nobody
        // catches System.FormatException at all, so the ban resolves empty but — being a concrete typeof anchor,
        // not a pattern — stays silent (the departure from the pattern-operand inert warning above).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/filter-format-catches")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(typeof(FormatException)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotCatchUnfiltered_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the filter-aware catch verb takes the same subject gate.
        RuleResult result = Checker.Run(
            "namespace App { public class Foo {} }",
            arch => arch.Rule("ex/empty")
                .Enforce(arch.Namespace("Nowhere.*").MustNotCatchUnfiltered(arch.Namespace("App.*")))
                .Because("b")).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe(ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustNotCatchUnfiltered_GrandfatheredEdgePasses_NewCatchStaysRed()
    {
        // Identity is the (source, caught) type pair (GRAMMAR §4.3) — the unfiltered sites are evidence, not
        // identity, so a baseline entry written for either catch verb keys the same edge. Handler is grandfathered
        // for catching AErr; its NEW unfiltered `catch (BErr)` is a distinct identity → red.
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
        BaselineIndex index = Index("ex/filter-catches", BaselineEntry.ForEdge("T:App.Handler", "T:Errors.AErr"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/filter-catches")
                    .Migrate("legacy unfiltered catches", arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("name what a broad catch expects"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.Handler -> Errors.BErr"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void MustNotCatchUnfiltered_BystanderCatch_StaysRedWhenAnotherEdgeBaselined()
    {
        // Two handlers catch the same Errors.Err unfiltered; only OldHandler's edge is grandfathered. NewHandler
        // catching the identical type is a distinct (source, caught) identity — a bystander — so it stays red.
        const string source = """
                              namespace Errors { public class Err : System.Exception {} }
                              namespace App
                              {
                                  public class OldHandler { public void Run() { try { } catch (Errors.Err) { } } }
                                  public class NewHandler { public void Run() { try { } catch (Errors.Err) { } } }
                              }
                              """;
        BaselineIndex index = Index("ex/filter-catches", BaselineEntry.ForEdge("T:App.OldHandler", "T:Errors.Err"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/filter-catches")
                    .Migrate("legacy unfiltered catches", arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("name what a broad catch expects"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.CatchPairs().ShouldBe(["App.NewHandler -> Errors.Err"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void JsonReportRenderer_UnfilteredCatchViolation_EmitsCatchKindAndTargetAndOmitsMemberSlots()
    {
        // Kind reuse reaches the wire: the JSON kind string is "catch", the caught type rides the existing
        // `target` field, and schemaVersion stays 3 — a new verb on a recorded fact family adds no vocabulary a
        // hook would have to learn.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("ex/filter-domain-catches")
                .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
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