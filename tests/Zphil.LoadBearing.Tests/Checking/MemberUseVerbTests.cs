using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;
using Zphil.LoadBearing.Tests.Checking.MemberTargets;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The member-access verb <c>MustNotUse</c> over the fast path (GRAMMAR §4.5, §4.3): matching by
///     (declaring type, name), per-overload violation identity, the dispatch boundary, the never-warns
///     win condition, the closed-generic member-anchor refusal, and the ratchet round-trip. Member-target
///     types are the reflectable <see cref="Clock" />/<see cref="IGauge" />/<see cref="Gauge" /> mirrored
///     in <see cref="Scene" /> (the CheckerTargets discipline).
/// </summary>
public sealed class MemberUseVerbTests
{
    private const string T = "Zphil.LoadBearing.Tests.Checking.MemberTargets.";

    // Mirrors MemberTargets.cs (the typeof anchors) plus subjects that use those members. App.Dashboard
    // reads Clock.Ticks and calls both Advance overloads; App.Meter calls Read through an interface-typed
    // and a concrete-typed receiver (the dispatch-boundary pair).
    private const string Scene = """
                                 namespace Zphil.LoadBearing.Tests.Checking.MemberTargets
                                 {
                                     public sealed class Clock
                                     {
                                         public int Ticks { get; set; }
                                         public void Advance() {}
                                         public void Advance(int by) {}
                                     }
                                     public interface IGauge { void Read(); }
                                     public sealed class Gauge : IGauge { public void Read() {} }
                                 }
                                 namespace App
                                 {
                                     using Zphil.LoadBearing.Tests.Checking.MemberTargets;
                                     public class Dashboard
                                     {
                                         public int Show(Clock c)
                                         {
                                             c.Advance();
                                             c.Advance(3);
                                             return c.Ticks;
                                         }
                                     }
                                     public class Meter
                                     {
                                         public void ViaInterface(IGauge g) { g.Read(); }
                                         public void ViaConcrete(Gauge c) { c.Read(); }
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustNotUse_SubjectUsesBannedMember_FailsWithSourceMemberSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("member/no-clock-read")
                    .Enforce(arch.Namespace("App.*").MustNotUse(arch.Member(typeof(Clock), nameof(Clock.Ticks))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.MemberUse);
        violation.Source!.FullName.ShouldBe("App.Dashboard");
        violation.Member!.SymbolId.ShouldBe($"P:{T}Clock.Ticks");
        violation.Member.ContainingType.FullName.ShouldBe($"{T}Clock");
        violation.Sites.ShouldNotBeEmpty();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain($"App.Dashboard uses {T}Clock.Ticks");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotUse_HumanLine_AppendsParensForMethodNotForProperty()
    {
        string methodBlock = Block(arch => arch.Rule("member/no-advance")
            .Enforce(arch.Namespace("App.*").MustNotUse(arch.Member(typeof(Clock), nameof(Clock.Advance))))
            .Because("b"));
        methodBlock.ShouldContain($"uses {T}Clock.Advance()");

        string propertyBlock = Block(arch => arch.Rule("member/no-ticks")
            .Enforce(arch.Namespace("App.*").MustNotUse(arch.Member(typeof(Clock), nameof(Clock.Ticks))))
            .Because("b"));
        propertyBlock.ShouldContain($"uses {T}Clock.Ticks");
        propertyBlock.ShouldNotContain("Ticks()");
    }

    [Fact]
    public void MustNotUse_BannedMemberAbsentFromCodebase_PassesWithZeroWarnings()
    {
        // A banned member the codebase never uses is the win condition: MustNotUse never warns (no pattern
        // form exists), exactly like a bare typeof target (GRAMMAR §4.5).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("member/no-now")
                    .Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotUse_OverloadedMethodBannedByName_ProducesOneViolationPerOverload()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("member/no-advance")
                    .Enforce(arch.Namespace("App.*").MustNotUse(arch.Member(typeof(Clock), nameof(Clock.Advance))))
                    .Because("b"))
            .Single();

        // One ban on (Clock, Advance) covers both overloads; each resolved overload is a distinct identity
        // (GRAMMAR §4.3), so a grandfathered Advance() and Advance(int) ratchet independently.
        result.Violations.Select(v => v.BaselineIdentity()!.Target).OrderBy(t => t, StringComparer.Ordinal)
            .ShouldBe([$"M:{T}Clock.Advance", $"M:{T}Clock.Advance(System.Int32)"]);
    }

    [Fact]
    public void MustNotUse_DispatchBoundary_ConcreteBanAndInterfaceBanEachCatchOnlyTheirReceiver()
    {
        // A ban on the concrete member catches only the concrete-typed call (member bans are
        // source-visibility bans, not runtime-dispatch bans — GRAMMAR §4.5).
        RuleResult concrete = Checker.Run(SceneModel, arch =>
                arch.Rule("member/no-concrete")
                    .Enforce(arch.Namespace("App.*").MustNotUse(arch.Member(typeof(Gauge), nameof(Gauge.Read))))
                    .Because("b"))
            .Single();
        MemberIds(concrete).ShouldBe([$"M:{T}Gauge.Read"]);

        // A ban on the interface member catches only the interface-typed call.
        RuleResult iface = Checker.Run(SceneModel, arch =>
                arch.Rule("member/no-iface")
                    .Enforce(arch.Namespace("App.*").MustNotUse(arch.Member(typeof(IGauge), nameof(IGauge.Read))))
                    .Because("b"))
            .Single();
        MemberIds(iface).ShouldBe([$"M:{T}IGauge.Read"]);
    }

    [Fact]
    public void MustNotUse_EmptySubject_FailsWithSharedEmptySubjectMessage()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("member/x")
                    .Enforce(arch.Namespace("Nowhere.*").MustNotUse(arch.Member(typeof(Clock), nameof(Clock.Ticks))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe(ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustNotUse_ClosedGenericMemberAnchor_FailsWithRuleErrorRefusal()
    {
        // WP1's spec-build typo guard normalizes typeof(Task<int>) to the definition and passes, so the
        // closed construction legitimately reaches the checker, which refuses it (member edges are
        // definition-level, GRAMMAR §4.5).
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("member/closed")
                    .Enforce(arch.Types.MustNotUse(arch.Member(typeof(Task<int>), "Result")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.RuleError);
        violation.Detail.ShouldBe(
            "`Task<Int32>` is a closed generic construction; member-use edges are definition-level. " +
            "Anchor the member on the open definition instead.");
    }

    [Fact]
    public void MustNotUse_MultipleMembers_ViolatesAcrossEveryBannedMember()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("member/no-clock")
                    .Enforce(arch.Namespace("App.*").MustNotUse(
                        arch.Member(typeof(Clock), nameof(Clock.Ticks)),
                        arch.Member(typeof(Clock), nameof(Clock.Advance))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        // The Ticks read plus both Advance overloads — the ban spans both named members.
        MemberIds(result).OrderBy(id => id, StringComparer.Ordinal).ShouldBe(
        [
            $"M:{T}Clock.Advance",
            $"M:{T}Clock.Advance(System.Int32)",
            $"P:{T}Clock.Ticks"
        ]);
    }

    [Fact]
    public void MustNotUse_UsingStaticBareName_IsCaughtAtTheCheckerPath()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using static System.Math;
                                                         namespace App;
                                                         public class Calc { public double Go() => Sqrt(2); }
                                                         """);

        RuleResult result = Checker.Run(model, arch =>
                arch.Rule("member/no-sqrt")
                    .Enforce(arch.Types.MustNotUse(arch.Member(typeof(Math), nameof(Math.Sqrt))))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Source!.FullName.ShouldBe("App.Calc");
        violation.Member!.SymbolId.ShouldBe("M:System.Math.Sqrt(System.Double)");
    }

    [Fact]
    public void MustNotUse_MemberRatchet_GrandfathersThenNewOverloadIsRed_AndTamperRefused()
    {
        string dir = Path.Combine(Path.GetTempPath(), "loadbearing-member-ratchet", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ArchitectureModel model = ArchModelBuilder.Build(new InlineSpec(arch => arch.Rule("mig/no-wait")
                .Migrate("legacy blocking waits", arch.Types.MustNotUse(arch.Member(typeof(Task), nameof(Task.Wait))))
                .Baseline("member.json")
                .Because("b")));

            // Empty baseline → the blocking Wait() is red; capture its exact member identity.
            CodebaseModel before = CompilationFactory.Extract(WaitSource("t.Wait();"));
            RuleResult red = ArchChecker.Check(model, before, BaselineIndex.Empty).Single();
            red.Status.ShouldBe(RuleStatus.Failed);
            Violation observed = red.Violations.Single(v => v.Kind == ViolationKind.MemberUse);
            observed.Member!.SymbolId.ShouldBe("M:System.Threading.Tasks.Task.Wait");

            // Grandfather it through the real store — a member entry (source T: / target M:) round-trips
            // with zero format/parser changes.
            BaselineEntry identity = observed.BaselineIdentity()!;
            var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal)
            {
                ["mig/no-wait"] = [identity.WithBecause("INC-1")]
            };
            string path = Path.Combine(dir, "member.json");
            BaselineStore.Write(path, new BaselineDocument(sections));

            BaselineIndex loaded = BaselineStore.LoadForModel(model, dir);
            RuleResult grandfathered = ArchChecker.Check(model, before, loaded).Single();
            grandfathered.Status.ShouldBe(RuleStatus.Passed);
            grandfathered.Grandfathered.Count.ShouldBe(1);

            // Change the used overload Wait() → Wait(timeout): identity is the specific member id, so the
            // grandfathered blessing does not cover it — NEW red.
            CodebaseModel after = CompilationFactory.Extract(WaitSource("t.Wait(System.TimeSpan.Zero);"));
            RuleResult regressed = ArchChecker.Check(model, after, loaded).Single();
            regressed.Status.ShouldBe(RuleStatus.Failed);
            regressed.Violations.Single(v => v.Kind == ViolationKind.MemberUse).Member!.SymbolId
                .ShouldBe("M:System.Threading.Tasks.Task.Wait(System.TimeSpan)");

            // Hand-edit the member entry's target line without rebuilding the digest → tamper refusal,
            // proving the digest is computed over member ids too (prefix-agnostic, zero changes).
            File.WriteAllText(path, File.ReadAllText(path).Replace(
                "M:System.Threading.Tasks.Task.Wait", "M:System.Threading.Tasks.Task.Wait(System.TimeSpan)"));
            Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
                .Message.ShouldContain("failed its integrity check");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void JsonReportRenderer_MemberUseViolation_EmitsTargetMemberAndOmitsTargetAndSubject()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace App;
                                                         public class Home { public DateTime Go() => DateTime.Now; }
                                                         """);
        CheckReport report = Checker.Run(model, arch =>
            arch.Rule("member/no-now")
                .Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))))
                .Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, []);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        JsonElement violation = document.RootElement.GetProperty("rules")[0].GetProperty("violations")[0];
        violation.GetProperty("kind").GetString().ShouldBe("memberUse");
        violation.GetProperty("source").GetString().ShouldBe("App.Home");
        violation.GetProperty("targetMember").GetString().ShouldBe("P:System.DateTime.Now");
        violation.TryGetProperty("target", out _).ShouldBeFalse();
        violation.TryGetProperty("subject", out _).ShouldBeFalse();
        violation.GetProperty("sites").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void MustNotUse_ExpressionMintedAnchor_MatchesSameAsTypeofAnchor()
    {
        // An expression-minted anchor arch.Member<Clock>(c => c.Ticks) reifies to the same (declaring type,
        // name), so it yields the identical member SymbolId — and the identical violation — as the typeof form.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("member/no-ticks")
                    .Enforce(arch.Namespace("App.*").MustNotUse(arch.Member<Clock>(c => c.Ticks)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.MemberUse);
        violation.Member!.SymbolId.ShouldBe($"P:{T}Clock.Ticks");
    }

    [Fact]
    public void MustNotUse_ExpressionInterfaceCastAnchor_RespectsDispatchBoundary()
    {
        // arch.Member<Gauge>(g => ((IGauge)g).Read()) anchors the interface member: the receiver-side Convert
        // is peeled to recognise the parameter, but the resolved method is IGauge.Read, so the ban catches
        // only the interface-typed call — the same dispatch boundary as the typeof(IGauge) ban (GRAMMAR §4.5).
        RuleResult iface = Checker.Run(SceneModel, arch =>
        {
            // The (IGauge) cast is load-bearing: it moves the statically-resolved method the expression tree
            // records from the concrete Gauge.Read to IGauge.Read, so it must survive cleanup's cast strip.
            // ReSharper disable once RedundantCast
            Member ifaceRead = arch.Member<Gauge>(g => ((IGauge)g).Read());
            arch.Rule("member/no-iface")
                .Enforce(arch.Namespace("App.*").MustNotUse(ifaceRead))
                .Because("b");
        }).Single();

        MemberIds(iface).ShouldBe([$"M:{T}IGauge.Read"]);
    }

    [Fact]
    public void MustNotUse_VerbMintedStaticAnchor_MatchesSameAsTypeofAnchor()
    {
        // A verb-minted static anchor `() => DateTime.Now` desugars through arch.Member(() => …) to the same
        // (declaring type, name) leaf as the typeof form, so it yields the identical member SymbolId — and the
        // identical violation. The shared Scene uses no static member, so a local model that reads DateTime.Now
        // stands in (the local-model pattern of the JsonReportRenderer / UsingStatic siblings above).
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace App;
                                                         public class Home { public DateTime Go() => DateTime.Now; }
                                                         """);

        RuleResult result = Checker.Run(model, arch =>
                arch.Rule("member/no-now")
                    .Enforce(arch.Namespace("App.*").MustNotUse(() => DateTime.Now))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.MemberUse);
        violation.Member!.SymbolId.ShouldBe("P:System.DateTime.Now");
    }

    private static string Block(Action<Arch> define)
    {
        RuleResult result = Checker.Run(SceneModel, define).Single();
        return HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
    }

    private static IReadOnlyList<string> MemberIds(RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.MemberUse)
            .Select(v => v.Member!.SymbolId)
            .ToList();
    }

    private static string WaitSource(string call)
    {
        return "using System.Threading.Tasks;\n"
               + "namespace N;\n"
               + "public class Waiter { public void Go(Task t) { " + call + " } }";
    }
}