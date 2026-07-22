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

    // The MustAcceptParameter substrate (GRAMMAR §4.6, §5.7): every method returns a Task form (so the
    // .Returning(Task, Task<>) subject keeps them all), and the parameter lists span the compliant and
    // non-compliant shapes — a bare token, a token behind other params, a defaulted token (the common
    // compliant signature), an open-generic IProgress<int>, and the two definition-level "honesty" traps a
    // CancellationToken? (System.Nullable<T>) and a params CancellationToken[] (the array type) that do NOT
    // match a System.Threading.CancellationToken anchor.
    private const string Parameters = """
                                      namespace App.Parameters
                                      {
                                          using System;
                                          using System.Threading;
                                          using System.Threading.Tasks;
                                          public class Handlers
                                          {
                                              public Task HandleWithToken(CancellationToken cancellationToken) => Task.CompletedTask;
                                              public Task<int> FetchWithToken(string key, CancellationToken cancellationToken) => Task.FromResult(0);
                                              public Task PollWithoutToken() => Task.CompletedTask;
                                              public Task DefaultedToken(CancellationToken cancellationToken = default) => Task.CompletedTask;
                                              public Task ReportProgress(IProgress<int> progress) => Task.CompletedTask;
                                              public Task NullableTokenOnly(CancellationToken? cancellationToken) => Task.CompletedTask;
                                              public Task ParamsTokensOnly(params CancellationToken[] tokens) => Task.CompletedTask;
                                          }
                                      }
                                      """;

    private static readonly CodebaseModel MembersModel = CompilationFactory.Extract(Members);
    private static readonly CodebaseModel AsyncModel = CompilationFactory.Extract(Async);
    private static readonly CodebaseModel KindsModel = CompilationFactory.Extract(Kinds);
    private static readonly CodebaseModel ParametersModel = CompilationFactory.Extract(Parameters);

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

    [Fact]
    public void WithNameMatchingAdjective_NarrowsMemberSetByGlob_ThenVerbRuns()
    {
        // The .WithNameMatching *adjective* (not the MustHaveNameMatching verb above) narrows the projected
        // methods by an ordinal *-glob before the verb runs — MemberSelectionEvaluator.ApplyAdjective's
        // name-matching arm. "*Async" keeps only RunAsync, so MustHaveSuffix("Async") holds; without the
        // narrowing the other seven methods would fail, so the pass proves the glob actually filtered.
        Pass(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithNameMatching("*Async").MustHaveSuffix("Async"));

        // "*Walk*" selects only Walk, which fails the suffix — the glob arm flags exactly that member.
        FailedMemberIds(MembersModel, arch => arch.Namespace("App.Members.*").Methods.WithNameMatching("*Walk*").MustHaveSuffix("Async"))
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

    // ── MustAcceptParameter (definition-level) ────────────────────────────────────────────────────────

    [Fact]
    public void MustAcceptParameter_TokenBearingMethodPasses_TokenlessMethodRedsAtDeclarationSite()
    {
        // Under .Returning(Task, Task<>).MustAcceptParameter(CancellationToken): a method that declares a
        // CancellationToken parameter passes.
        Pass(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
            .WithPrefix("HandleWithToken").Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken)));

        // …a tokenless one reds, keyed on its own M: DocId and located at its declaration site (Test.cs).
        RuleResult result = Checker.Run(ParametersModel, arch => arch.Rule("member/x").Enforce(
                arch.Namespace("App.Parameters.*").Methods.WithPrefix("PollWithoutToken")
                    .Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken))).Because("b"))
            .Single();
        Violation red = result.Violations.Single(v => v.Kind == ViolationKind.MemberShape);
        red.SubjectMember!.SymbolId.ShouldBe("M:App.Parameters.Handlers.PollWithoutToken");
        red.Sites.ShouldContain(site => site.FilePath.EndsWith("Test.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void MustAcceptParameter_BareMethodsSubject_HoldsAndFlagsMismatch()
    {
        // The bare .Methods.MustAcceptParameter form (no .Returning narrowing): a method that accepts a token
        // behind another parameter passes (any position counts), a tokenless one reds.
        Pass(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
            .WithPrefix("FetchWithToken").MustAcceptParameter(typeof(CancellationToken)));

        FailedMemberIds(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
                .WithPrefix("PollWithoutToken").MustAcceptParameter(typeof(CancellationToken)))
            .ShouldBe(["M:App.Parameters.Handlers.PollWithoutToken"]);
    }

    [Fact]
    public void MustAcceptParameter_OpenGenericAnchor_MatchesAnyConstruction()
    {
        // typeof(IProgress<>) matches a method accepting IProgress<int> at the definition level (any
        // construction on the definition name, GRAMMAR §4.6) — the parameter analog of .Returning(typeof(Task<>)).
        Pass(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
            .WithPrefix("ReportProgress").MustAcceptParameter(typeof(IProgress<>)));

        // …and reds a method accepting no IProgress construction.
        FailedMemberIds(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
                .WithPrefix("PollWithoutToken").MustAcceptParameter(typeof(IProgress<>)))
            .ShouldBe(["M:App.Parameters.Handlers.PollWithoutToken"]);
    }

    [Fact]
    public void MustAcceptParameter_DefaultValuedParameter_Counts()
    {
        // A `CancellationToken cancellationToken = default` parameter counts — the most common compliant
        // signature. Extraction reads the declared symbol, so a default value never drops the parameter.
        Pass(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
            .WithPrefix("DefaultedToken").MustAcceptParameter(typeof(CancellationToken)));
    }

    [Fact]
    public void MustAcceptParameter_DefinitionLevelHonesty_NullableAndParamsArrayRed()
    {
        // Definition-level honesty (GRAMMAR §4.6): a CancellationToken? parameter records System.Nullable<T>'s
        // definition, and a params CancellationToken[] records the array type — NEITHER equals the
        // System.Threading.CancellationToken anchor, so both red. Asserted through the real evaluator over
        // real extracted facts, not by faking a parameter list.
        FailedMemberIds(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
                .WithPrefix("NullableTokenOnly").MustAcceptParameter(typeof(CancellationToken)))
            .ShouldBe(["M:App.Parameters.Handlers.NullableTokenOnly(System.Nullable{System.Threading.CancellationToken})"]);

        FailedMemberIds(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
                .WithPrefix("ParamsTokensOnly").MustAcceptParameter(typeof(CancellationToken)))
            .ShouldBe(["M:App.Parameters.Handlers.ParamsTokensOnly(System.Threading.CancellationToken[])"]);
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

    [Fact]
    public void Where_EscapeHatch_ReachesParametersEndToEnd()
    {
        // Self-guarding: the Where narrows to methods declaring a System.Threading.CancellationToken
        // parameter — if Parameters/TypeFullName were unpopulated the subject would be empty and the rule
        // would FAIL. Passed proves the real extracted parameter facts reach the checker end-to-end.
        Pass(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
            .Where(m => m.Parameters.Any(p => p.TypeFullName == "System.Threading.CancellationToken"), "that accept a token")
            .MustBePublic());
    }

    [Fact]
    public void Must_EscapeHatch_ReachesParameterCountEndToEnd()
    {
        // The Must predicate reads m.Parameters.Count: the one parameterless method reds, proving the
        // parameter list reaches the member predicate end-to-end (the other six declare a parameter and pass).
        FailedMemberIds(ParametersModel, arch => arch.Namespace("App.Parameters.*").Methods
                .Must(m => m.Parameters.Count > 0, "declare at least one parameter"))
            .ShouldBe(["M:App.Parameters.Handlers.PollWithoutToken"]);
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
        // blessing does not cover it — a NEW red.
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

    [Fact]
    public void MemberSubjectRatchet_MustAcceptParameter_GrandfathersMember_ThenNewMemberIsRed()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new InlineSpec(arch => arch.Rule("async/accept-cancellation")
            .Migrate("legacy Task-returning methods do not accept a CancellationToken",
                arch.Namespace("App.Cancel.*").Methods
                    .Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken)))
            .Because("Async methods must honor cancellation.")));

        const string before = """
                              namespace App.Cancel
                              {
                                  using System.Threading;
                                  using System.Threading.Tasks;
                                  public class Api
                                  {
                                      public Task Poll() => Task.CompletedTask;
                                      public Task Fetch(CancellationToken cancellationToken) => Task.CompletedTask;
                                  }
                              }
                              """;
        CodebaseModel beforeModel = CompilationFactory.Extract(before);

        // Empty baseline → Poll() is red (Fetch passes); capture its member-subject identity (a M: DocId).
        RuleResult red = ArchChecker.Check(model, beforeModel, BaselineIndex.Empty).Single();
        red.Status.ShouldBe(RuleStatus.Failed);
        Violation observed = red.Violations.Single(v => v.Kind == ViolationKind.MemberShape);
        BaselineEntry identity = observed.BaselineIdentity()!;
        identity.Subject.ShouldBe("M:App.Cancel.Api.Poll");

        // Grandfather Poll through an in-memory BaselineIndex carrying an attribution — the shape-verb
        // identity substrate, zero baseline-format changes (GRAMMAR §4.6).
        var index = new BaselineIndex(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal)
        {
            ["async/accept-cancellation"] = new([identity.WithBecause("INC-1")])
        });
        RuleResult grandfathered = ArchChecker.Check(model, beforeModel, index).Single();
        grandfathered.Status.ShouldBe(RuleStatus.Passed);
        grandfathered.Grandfathered.Count.ShouldBe(1);
        grandfathered.GrandfatheredEntries.Single().Because.ShouldBe("INC-1"); // --because attribution round-trips

        // Add a NEW tokenless Task method: its identity is its own DocId, so the grandfathered blessing does
        // not cover it — a NEW red beside the still-grandfathered bystander (Poll).
        const string after = """
                             namespace App.Cancel
                             {
                                 using System.Threading;
                                 using System.Threading.Tasks;
                                 public class Api
                                 {
                                     public Task Poll() => Task.CompletedTask;
                                     public Task Fetch(CancellationToken cancellationToken) => Task.CompletedTask;
                                     public Task Purge() => Task.CompletedTask;
                                 }
                             }
                             """;
        RuleResult regressed = ArchChecker.Check(model, CompilationFactory.Extract(after), index).Single();
        regressed.Status.ShouldBe(RuleStatus.Failed);
        regressed.Grandfathered.Count.ShouldBe(1);
        FailedMemberIds(regressed).ShouldBe(["M:App.Cancel.Api.Purge"]);
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

    [Fact]
    public void MustAcceptParameter_ClosedGenericAnchor_IsRefusedAsRuleErrorAtCheckTime()
    {
        // Spec build refuses a closed-generic MustAcceptParameter (GRAMMAR §8 item 20); the checker carries
        // the same refusal as a backstop — the sibling of the .Returning backstop above, resolving the anchor
        // through the SAME definition-FQN path. A hand-built model bypasses spec-build validation to reach it:
        // the closed construction surfaces as a RuleError, not a crash.
        var arch = new Arch();
        Constraint constraint = arch.Namespace("App.Parameters.*").Methods.MustAcceptParameter(typeof(IProgress<int>));
        var model = new ArchitectureModel(
            [new ArchRule("async/x", Posture.Enforce, "b", null, "sentence", constraint, null, null)], []);

        RuleResult result = ArchChecker.Check(model, ParametersModel).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.RuleError);
        violation.Detail.ShouldBe(
            "`IProgress<Int32>` is a closed generic construction; member parameter matching is definition-level. " +
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