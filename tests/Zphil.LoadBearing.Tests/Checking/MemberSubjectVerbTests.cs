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
///     The member modal verbs over the fast path (GRAMMAR §4.6, §5.7): per-verb pass/fail for all ten
///     verbs (naming, accessibility, static/abstract/virtual, and the <c>Must</c> escape hatch), the
///     projection kind filter, <c>Returning</c> at the definition level (exact / open-generic / void /
///     multi-anchor), the deterministic <c>(DeclaringType.FullName, SymbolId)</c> ordering, the member
///     escape hatches reaching real extracted facts end-to-end (acceptance box 2), the empty
///     member-subject failure, the ratchet round-trip, the closed-generic check-time backstop, and the
///     human/JSON member-shape rendering. Pinned strings are the spec — moving one is a deliberate act.
/// </summary>
public sealed class MemberSubjectVerbTests
{
    private const string Members = """
                                   namespace App.Members
                                   {
                                       public class Widget
                                       {
                                           public void RunAsync() {}
                                           public void Walk() {}
                                           internal void Hidden() {}
                                           private void Secret() {}
                                           public static void Boot() {}
                                       }
                                       public abstract class Machine
                                       {
                                           public abstract void Cycle();
                                           public virtual void Spin() {}
                                           public void Idle() {}
                                       }
                                   }
                                   """;

    private const string Async = """
                                 namespace App.Async
                                 {
                                     using System.Threading.Tasks;
                                     public class HomeController
                                     {
                                         public Task Save() => Task.CompletedTask;
                                         public Task<int> Load() => Task.FromResult(0);
                                         public Task SaveAsync() => Task.CompletedTask;
                                         public void Sync() {}
                                     }
                                 }
                                 """;

    private const string Kinds = """
                                 namespace App.Kinds
                                 {
                                     public class Box
                                     {
                                         public int Field;
                                         public int Prop { get; set; }
                                         public event System.Action Evt;
                                         public void Do() {}
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel MembersModel = CompilationFactory.Extract(Members);
    private static readonly CodebaseModel AsyncModel = CompilationFactory.Extract(Async);
    private static readonly CodebaseModel KindsModel = CompilationFactory.Extract(Kinds);

    // ── naming verbs ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MustHaveSuffix_HoldsAndFlagsMismatch()
    {
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithSuffix("Async").MustHaveSuffix("Async"));

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Walk").MustHaveSuffix("Async"))
            .ShouldBe(["M:App.Members.Widget.Walk"]);
    }

    [Fact]
    public void MustHavePrefix_HoldsAndFlagsMismatch()
    {
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Run").MustHavePrefix("Run"));

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Walk").MustHavePrefix("Run"))
            .ShouldBe(["M:App.Members.Widget.Walk"]);
    }

    [Fact]
    public void MustHaveNameMatching_HoldsAndFlagsMismatch()
    {
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithSuffix("Async").MustHaveNameMatching("*Async"));

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Walk").MustHaveNameMatching("*Async"))
            .ShouldBe(["M:App.Members.Widget.Walk"]);
    }

    // ── accessibility verbs ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MustBePublic_HoldsAndFlagsInternal()
    {
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Run").MustBePublic());

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Hidden").MustBePublic())
            .ShouldBe(["M:App.Members.Widget.Hidden"]);
    }

    [Fact]
    public void MustBeInternal_HoldsAndFlagsPublic()
    {
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Hidden").MustBeInternal());

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Run").MustBeInternal())
            .ShouldBe(["M:App.Members.Widget.RunAsync"]);
    }

    [Fact]
    public void MustBePrivate_HoldsAndFlagsPublic()
    {
        // MustBePrivate is member-only vocabulary (no type-side twin) — a private member is inventoried.
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Secret").MustBePrivate());

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Run").MustBePrivate())
            .ShouldBe(["M:App.Members.Widget.RunAsync"]);
    }

    // ── static / abstract / virtual verbs ─────────────────────────────────────────────────────────────

    [Fact]
    public void MustBeStatic_HoldsAndFlagsInstance()
    {
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Boot").MustBeStatic());

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Walk").MustBeStatic())
            .ShouldBe(["M:App.Members.Widget.Walk"]);
    }

    [Fact]
    public void MustBeAbstract_HoldsAndFlagsConcrete()
    {
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Cycle").MustBeAbstract());

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Idle").MustBeAbstract())
            .ShouldBe(["M:App.Members.Machine.Idle"]);
    }

    [Fact]
    public void MustBeVirtual_HoldsAndFlagsNonVirtual()
    {
        // C# declaration semantics: only a `virtual` member is virtual — an abstract or concrete one is not
        // (member-only vocabulary, no type-side twin).
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Spin").MustBeVirtual());

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Idle").MustBeVirtual())
            .ShouldBe(["M:App.Members.Machine.Idle"]);

        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithPrefix("Cycle").MustBeVirtual())
            .ShouldBe(["M:App.Members.Machine.Cycle"]);
    }

    // ── projections (kind filter) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Projections_SelectOnlyTheirKind()
    {
        // Each kind sugar narrows the member set to exactly its kind — a MustBeStatic over an instance
        // member of that kind flags only it, proving the other kinds were filtered out.
        FailedMemberIds(KindsModel, arch => arch.Namespace("App.Kinds.*").Properties.MustBeStatic())
            .ShouldBe(["P:App.Kinds.Box.Prop"]);
        FailedMemberIds(KindsModel, arch => arch.Namespace("App.Kinds.*").Fields.MustBeStatic())
            .ShouldBe(["F:App.Kinds.Box.Field"]);
        FailedMemberIds(KindsModel, arch => arch.Namespace("App.Kinds.*").Events.MustBeStatic())
            .ShouldBe(["E:App.Kinds.Box.Evt"]);
    }

    [Fact]
    public void Members_ProjectionIsAllKinds()
    {
        // .Members is the unrestricted projection — every kind is in the subject set.
        FailedMemberIds(KindsModel, arch => arch.Namespace("App.Kinds.*").Members.MustBeStatic())
            .ShouldBe(["E:App.Kinds.Box.Evt", "F:App.Kinds.Box.Field", "M:App.Kinds.Box.Do", "P:App.Kinds.Box.Prop"]);
    }

    // ── Returning (definition-level) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Returning_NonGenericAnchor_MatchesThatReturnTypeExactly()
    {
        // typeof(Task) matches the two Task-returning methods (not Task<int>, not void); Save fails the suffix.
        FailedMemberIds(AsyncModel, arch => arch.Namespace("App.Async.*").Methods.Returning(typeof(Task)).MustHaveSuffix("Async"))
            .ShouldBe(["M:App.Async.HomeController.Save"]);
    }

    [Fact]
    public void Returning_OpenGenericAnchor_MatchesAnyConstruction()
    {
        // typeof(Task<>) matches Task<int> at the definition level (GRAMMAR §4.6) — the open-generic
        // construction match, mirroring Implementing's auto-detect.
        FailedMemberIds(AsyncModel, arch => arch.Namespace("App.Async.*").Methods.Returning(typeof(Task<>)).MustHaveSuffix("Async"))
            .ShouldBe(["M:App.Async.HomeController.Load"]);
    }

    [Fact]
    public void Returning_VoidAnchor_MatchesVoidReturn()
    {
        // typeof(void) → the System.Void return normalization the extractor records.
        FailedMemberIds(AsyncModel, arch => arch.Namespace("App.Async.*").Methods.Returning(typeof(void)).MustHaveSuffix("Async"))
            .ShouldBe(["M:App.Async.HomeController.Sync"]);
    }

    [Fact]
    public void Returning_MultipleAnchors_UnionTheReturnTypes()
    {
        // typeof(Task) or typeof(Task<>) selects Save, SaveAsync, and Load; SaveAsync passes, the other two fail.
        FailedMemberIds(AsyncModel, arch => arch.Namespace("App.Async.*").Methods
                .Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
            .ShouldBe(["M:App.Async.HomeController.Load", "M:App.Async.HomeController.Save"]);
    }

    // ── deterministic ordering ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MemberViolations_OrderByDeclaringTypeThenSymbolId()
    {
        // Ordering is (DeclaringType.FullName, SymbolId), NOT global SymbolId: Alpha.Prop (a P: id) sorts
        // before Zebra.Run (an M: id) because Alpha's type name sorts first — a global SymbolId sort would
        // put the M: entry first.
        const string source = """
                              namespace App.Order
                              {
                                  public class Zebra { public void Run() {} }
                                  public class Alpha { public int Prop { get; set; } }
                              }
                              """;

        FailedMemberIds(CompilationFactory.Extract(source), arch => arch.Namespace("App.Order.*").Members.MustBeStatic())
            .ShouldBe(["P:App.Order.Alpha.Prop", "M:App.Order.Zebra.Run"]);
    }

    // ── escape hatches reaching real facts (acceptance box 2) ─────────────────────────────────────────

    [Fact]
    public void Where_EscapeHatch_ReachesIsAsyncEndToEnd()
    {
        // Self-guarding: the Where narrows to async methods, so if IsAsync were unpopulated the subject
        // would be empty and the rule would FAIL — Passed proves IsAsync reaches the checker end-to-end.
        const string source = """
                              namespace App.Hatch
                              {
                                  using System.Threading.Tasks;
                                  public class Worker
                                  {
                                      public async Task RunAsync() { await Task.Yield(); }
                                      public void Plain() {}
                                  }
                              }
                              """;

        Pass(CompilationFactory.Extract(source), arch => arch.Namespace("App.Hatch.*").Methods
            .Where(m => m.IsAsync, "that are async").MustHaveSuffix("Async"));
    }

    [Fact]
    public void Must_EscapeHatch_ReachesDeclaringTypeEndToEnd()
    {
        // The Must predicate reads the member's DeclaringType (the reused ITypeInfo contract): a method on
        // the wrong type is flagged, proving the member predicate reaches its declaring type's facts.
        const string source = """
                              namespace App.Hatch
                              {
                                  public class KeepThis { public void A() {} }
                                  public class DropThis { public void B() {} }
                              }
                              """;

        FailedMemberIds(CompilationFactory.Extract(source), arch => arch.Namespace("App.Hatch.*").Methods
                .Must(m => m.DeclaringType.Name == "KeepThis", "be declared on KeepThis"))
            .ShouldBe(["M:App.Hatch.DropThis.B"]);
    }

    // ── empty member subject ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyMemberSubject_TypesMatchButNoMembersSurviveKindFilter_FailsWithMemberMessage()
    {
        // A type with no fields fails a .Fields rule with the member-flavored empty message — the ordinary
        // way to hit it (GRAMMAR §4.6). The member dispatch bypasses the type-subject gate entirely.
        const string source = "namespace App.NoFields { public class C { public void M() {} } }";

        RuleResult result = Checker.Run(CompilationFactory.Extract(source), arch =>
                arch.Rule("member/x").Enforce(arch.Namespace("App.NoFields.*").Fields.MustBePublic()).Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe(ConstraintEvaluator.EmptyMemberSubjectMessage);
        violation.Detail.ShouldBe("The subject selection matched no solution-declared members.");
    }

    [Fact]
    public void EmptyMemberSubject_TypeSelectionMatchesNothing_AlsoFailsWithMemberMessage()
    {
        // A member rule whose underlying type selection matches no types still speaks in member terms —
        // member dispatch runs before the type-subject empty gate.
        RuleResult result = Checker.Run(AsyncModel, arch =>
                arch.Rule("member/x").Enforce(arch.Namespace("Nowhere.*").Methods.MustBePublic()).Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Violations.Single().Detail.ShouldBe(ConstraintEvaluator.EmptyMemberSubjectMessage);
    }

    // ── ratchet round-trip ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MemberSubjectRatchet_GrandfathersMember_ThenNewMemberIsRed()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new InlineSpec(arch => arch.Rule("naming/async-suffix")
            .Migrate("legacy Task-returning methods lack the Async suffix",
                arch.Namespace("App.Async.*").Methods.Returning(typeof(Task)).MustHaveSuffix("Async"))
            .Because("Async discovery is suffix-based.")));

        // Empty baseline → Save() is red (SaveAsync passes); capture its member-subject identity (a M: DocId).
        RuleResult red = ArchChecker.Check(model, AsyncModel, BaselineIndex.Empty).Single();
        red.Status.ShouldBe(RuleStatus.Failed);
        Violation observed = red.Violations.Single(v => v.Kind == ViolationKind.MemberShape);
        BaselineEntry identity = observed.BaselineIdentity()!;
        identity.Subject.ShouldBe("M:App.Async.HomeController.Save");

        // Grandfather Save through an in-memory BaselineIndex — the shape-verb identity substrate, zero
        // baseline format changes (GRAMMAR §4.6).
        var index = new BaselineIndex(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal)
        {
            ["naming/async-suffix"] = new([identity.WithBecause("INC-1")])
        });
        RuleResult grandfathered = ArchChecker.Check(model, AsyncModel, index).Single();
        grandfathered.Status.ShouldBe(RuleStatus.Passed);
        grandfathered.Grandfathered.Count.ShouldBe(1);

        // Add a NEW unsuffixed Task method: identity is the member's own DocId, so the grandfathered
        // blessing does not cover it — a NEW red (DESIGN.md §5).
        const string after = """
                             namespace App.Async
                             {
                                 using System.Threading.Tasks;
                                 public class HomeController
                                 {
                                     public Task Save() => Task.CompletedTask;
                                     public Task<int> Load() => Task.FromResult(0);
                                     public Task SaveAsync() => Task.CompletedTask;
                                     public Task Delete() => Task.CompletedTask;
                                     public void Sync() {}
                                 }
                             }
                             """;
        RuleResult regressed = ArchChecker.Check(model, CompilationFactory.Extract(after), index).Single();
        regressed.Status.ShouldBe(RuleStatus.Failed);
        regressed.Grandfathered.Count.ShouldBe(1);
        FailedMemberIds(regressed).ShouldBe(["M:App.Async.HomeController.Delete"]);
    }

    // ── closed-generic check-time backstop ────────────────────────────────────────────────────────────

    [Fact]
    public void Returning_ClosedGenericAnchor_IsRefusedAsRuleErrorAtCheckTime()
    {
        // Spec build refuses a closed-generic .Returning (GRAMMAR §8 item 14); the checker carries the same
        // refusal as a backstop. A hand-built model bypasses spec-build validation to reach it: the closed
        // construction surfaces as a RuleError, not a crash.
        var arch = new Arch();
        Constraint constraint = arch.Namespace("App.Async.*").Methods.Returning(typeof(Task<int>)).MustHaveSuffix("Async");
        var model = new ArchitectureModel(
            [new ArchRule("naming/x", Posture.Enforce, "b", null, "sentence", constraint, null, null)], []);

        RuleResult result = ArchChecker.Check(model, AsyncModel).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.RuleError);
        violation.Detail.ShouldBe(
            "`Task<Int32>` is a closed generic construction; member return-type matching is definition-level. " +
            "Anchor on the open definition instead.");
    }

    // ── human + JSON rendering ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HumanReport_MemberShapeLine_IsDeclaringTypeDotMemberWithParensForMethodAtDeclarationSite()
    {
        RuleResult result = Checker.Run(AsyncModel, arch =>
                arch.Rule("naming/async-suffix")
                    .Enforce(arch.Namespace("App.Async.*").Methods.Returning(typeof(Task)).MustHaveSuffix("Async"))
                    .Because("Async discovery is suffix-based."))
            .Single();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain("App.Async.HomeController.Save()");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void JsonReport_MemberShapeViolation_EmitsSubjectMemberAndOmitsSubjectTargetAndTargetMember()
    {
        CheckReport report = Checker.Run(AsyncModel, arch =>
            arch.Rule("naming/async-suffix")
                .Enforce(arch.Namespace("App.Async.*").Methods.Returning(typeof(Task)).MustHaveSuffix("Async"))
                .Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, []);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        JsonElement violation = document.RootElement.GetProperty("rules")[0].GetProperty("violations")[0];
        violation.GetProperty("kind").GetString().ShouldBe("memberShape");
        violation.GetProperty("subjectMember").GetString().ShouldBe("M:App.Async.HomeController.Save");
        violation.TryGetProperty("subject", out _).ShouldBeFalse();
        violation.TryGetProperty("target", out _).ShouldBeFalse();
        violation.TryGetProperty("targetMember", out _).ShouldBeFalse();
        violation.GetProperty("sites").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void JsonReport_MemberSubjectFreeReport_OmitsSubjectMemberEntirely()
    {
        // A report with no member-shape violation renders byte-identically to before this slot existed: the
        // additive subjectMember field is null-omitted, so the schema stays version 3.
        CheckReport report = Checker.Run("namespace App { public class foo {} }", arch =>
            arch.Rule("naming/x").Enforce(arch.Types.MustHavePrefix("Bar")).Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, []);

        writer.ToString().ShouldNotContain("subjectMember");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static void Pass(CodebaseModel codebase, Func<Arch, Constraint> constraint)
    {
        Checker.Run(codebase, arch => arch.Rule("member/x").Enforce(constraint(arch)).Because("b"))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    private static IReadOnlyList<string> FailedMemberIds(CodebaseModel codebase, Func<Arch, Constraint> constraint)
    {
        RuleResult result = Checker.Run(codebase, arch => arch.Rule("member/x").Enforce(constraint(arch)).Because("b")).Single();
        return FailedMemberIds(result);
    }

    private static IReadOnlyList<string> FailedMemberIds(RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.MemberShape)
            .Select(v => v.SubjectMember!.SymbolId)
            .ToList();
    }
}