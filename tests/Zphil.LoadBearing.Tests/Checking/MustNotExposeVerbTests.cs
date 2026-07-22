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
///     The exposure verb <c>MustNotExpose</c> over the fast path (GRAMMAR §4.9, §4.3, §5.3): an exposure edge
///     trips only where a subject <c>expose</c>s a forbidden type in a public signature position, matched by
///     exact definition-level FQN (banning <c>typeof(Exception)</c> never flags a narrower
///     <c>IOException</c> exposure — no hierarchy-aware matching); a type that merely <em>references</em> the
///     banned type in a method body but never surfaces it on a signature stays green (the reference≠exposure
///     distinction that is the whole point of the fact family); a hierarchy-adjective operand matches
///     solution-declared types; the type-pair ratchet (grandfather one edge, bystanders stay red); the
///     inert-target warning on an empty pattern operand vs. the silent win on an absent bare <c>typeof</c>;
///     and the pinned human line + JSON kind. Violation identity is the (source, exposed) type pair, riding
///     <see cref="BaselineEntry.ForEdge" /> unchanged.
/// </summary>
public sealed class MustNotExposeVerbTests
{
    // Secrets.Secret is the internal type nobody may surface on their public API. Facade exposes it through a
    // public method parameter (the red edge); CleanService only `new`s it inside a method body — a reference
    // and a construction, but never a signature position — so the exposure verb is silent on it.
    private const string Scene = """
                                 namespace Secrets
                                 {
                                     public class Secret {}
                                 }
                                 namespace App
                                 {
                                     public class Facade
                                     {
                                         public void Take(Secrets.Secret s) {}
                                     }
                                     public class CleanService
                                     {
                                         public void Run()
                                         {
                                             var local = new Secrets.Secret();
                                             System.Console.WriteLine(local.ToString());
                                         }
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustNotExpose_SubjectExposesBannedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("api/no-expose-secret")
                    .Enforce(arch.Namespace("App.*").MustNotExpose(arch.Namespace("Secrets.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.Expose);
        violation.Source!.FullName.ShouldBe("App.Facade");
        violation.Target!.FullName.ShouldBe("Secrets.Secret");
        violation.Sites.ShouldNotBeEmpty();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain("App.Facade exposes Secrets.Secret");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotExpose_SubjectOnlyReferencesBannedTypeInBody_PassesCleanWithNoWarnings()
    {
        // CleanService constructs and references Secrets.Secret inside a method body but never names it in a
        // public signature position, so no exposure edge is minted — reference≠exposure, the crux of the verb.
        // The forbidden target resolves (Secrets.Secret exists), so this is a real pass, not an inert one.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("api/no-expose-secret")
                    .Enforce(arch.Namespace("App.*").WithSuffix("CleanService").MustNotExpose(arch.Namespace("Secrets.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotExpose_BansExceptionButExposesNarrowerType_NarrowExposureNotFlagged()
    {
        // Matching is exact definition-level FQN, not hierarchy: MustNotExpose(typeof(Exception)) flags a
        // `System.Exception` parameter but NEVER a narrower `IOException` one. Wide's presence proves the ban is
        // live, not vacuously empty; framework types do mint exposure edges (§4.9), so both endpoints exist.
        const string source = """
                              namespace App
                              {
                                  public class Wide
                                  {
                                      public void Take(System.Exception e) {}
                                  }
                                  public class Narrow
                                  {
                                      public void Take(System.IO.IOException e) {}
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("api/no-expose-all")
                    .Enforce(arch.Types.MustNotExpose(typeof(Exception)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ExposurePairs().ShouldBe(["App.Wide -> System.Exception"]);
    }

    [Fact]
    public void MustNotExpose_DerivedFromOperand_MatchesSolutionTypeNotExternal()
    {
        // A hierarchy-adjective operand (arch.Types.DerivedFrom) ranges over solution-declared types only —
        // external types carry a shallow hierarchy. Gateway exposes its own N.AppError (solution, derives from
        // Exception → matched → red) and an external System.InvalidOperationException (never matched by a
        // DerivedFrom operand, so not flagged), pinning that the adjective matches solution types but not externals.
        const string source = """
                              namespace N
                              {
                                  public class AppError : System.Exception {}
                                  public class Gateway
                                  {
                                      public void Take(N.AppError e) {}
                                      public void Copy(System.InvalidOperationException e) {}
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("api/no-expose-derived")
                    .Enforce(arch.Namespace("N.*").MustNotExpose(arch.Types.DerivedFrom(typeof(Exception))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ExposurePairs().ShouldBe(["N.Gateway -> N.AppError"]);
    }

    [Fact]
    public void MustNotExpose_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate, exactly as MustNotCatch's inert-target semantics (§4.9).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("api/inert")
                    .Enforce(arch.Namespace("App.*").MustNotExpose(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(CheckWarningKind.InertTarget);
        warning.Message.ShouldBe("This rule is inert: its target selection matched no types.");
    }

    [Fact]
    public void MustNotExpose_AbsentTypeofTarget_IsSilentWinNoWarning()
    {
        // A bare typeof target absent from the codebase is the WIN condition, not an inert warning: nobody
        // exposes System.FormatException, so the ban resolves empty but — being a concrete typeof anchor, not a
        // pattern — stays silent (the departure from the pattern-operand inert warning above).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("api/no-expose-format")
                    .Enforce(arch.Namespace("App.*").MustNotExpose(typeof(FormatException)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotExpose_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the exposure verb takes the same subject gate.
        RuleResult result = Checker.Run(
            "namespace App { public class Foo {} }",
            arch => arch.Rule("api/empty")
                .Enforce(arch.Namespace("Nowhere.*").MustNotExpose(arch.Namespace("App.*")))
                .Because("b")).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe(ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustNotExpose_GrandfatheredEdgePasses_NewExposureStaysRed()
    {
        // The exposure ratchet keys the (source, exposed) type pair (GRAMMAR §4.3): Facade is grandfathered for
        // exposing Secrets.A, but its NEW exposure of Secrets.B is a distinct identity → red.
        const string source = """
                              namespace Secrets { public class A {} public class B {} }
                              namespace App
                              {
                                  public class Facade
                                  {
                                      public void Take(Secrets.A a) {}
                                      public void Put(Secrets.B b) {}
                                  }
                              }
                              """;
        BaselineIndex index = Index("api/no-expose", BaselineEntry.ForEdge("T:App.Facade", "T:Secrets.A"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("api/no-expose")
                    .Migrate("legacy leaked surface", arch.Namespace("App.*").MustNotExpose(arch.Namespace("Secrets.*")))
                    .Because("keep the internal types off the public API"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ExposurePairs().ShouldBe(["App.Facade -> Secrets.B"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void MustNotExpose_BystanderExposure_StaysRedWhenAnotherEdgeBaselined()
    {
        // Two facades expose the same Secrets.Data; only OldFacade's edge is grandfathered. NewFacade exposing
        // the identical type is a distinct (source, exposed) identity — a bystander — so it stays red.
        const string source = """
                              namespace Secrets { public class Data {} }
                              namespace App
                              {
                                  public class OldFacade { public void Take(Secrets.Data d) {} }
                                  public class NewFacade { public void Take(Secrets.Data d) {} }
                              }
                              """;
        BaselineIndex index = Index("api/no-expose", BaselineEntry.ForEdge("T:App.OldFacade", "T:Secrets.Data"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("api/no-expose")
                    .Migrate("legacy leaked surface", arch.Namespace("App.*").MustNotExpose(arch.Namespace("Secrets.*")))
                    .Because("keep the internal types off the public API"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ExposurePairs().ShouldBe(["App.NewFacade -> Secrets.Data"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void JsonReportRenderer_ExposeViolation_EmitsExposeKindAndTargetAndOmitsMemberSlots()
    {
        // The JSON kind string is "expose" (auto-camelled from the enum) and the exposed type rides the existing
        // `target` field — no new slot, schemaVersion stays 3, so member/subject slots stay omitted as before.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("api/no-expose-secret")
                .Enforce(arch.Namespace("App.*").MustNotExpose(arch.Namespace("Secrets.*")))
                .Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, []);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(3);
        JsonElement violation = document.RootElement.GetProperty("rules")[0].GetProperty("violations")[0];
        violation.GetProperty("kind").GetString().ShouldBe("expose");
        violation.GetProperty("source").GetString().ShouldBe("App.Facade");
        violation.GetProperty("target").GetString().ShouldBe("Secrets.Secret");
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