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
///     The injection verb <c>MustNotInject</c> over the fast path (GRAMMAR §4.7, §4.3, §5.3): a captive
///     injection trips only where a singleton-registered subject takes a scoped/transient-registered service
///     as a constructor parameter (at the parameter's <c>file:line</c>); a mere reference without a
///     constructor parameter stays silent; membership discriminates by lifetime (a singleton injecting a
///     singleton is clean); the sanctioned-root exemption via <c>.Except</c>; the (source, injected) type-pair
///     ratchet with a bystander; the <b>never-warns</b> pin (an empty <c>Registered</c> operand is the win
///     condition, not an inert warning — the point of departure from <c>MustNotConstruct</c>); and the pinned
///     human line + JSON kind. Violation identity is the (source, injected) type pair, constructor-overload-
///     and parameter-name-indifferent, riding <see cref="BaselineEntry.ForEdge" /> unchanged.
/// </summary>
public sealed class MustNotInjectVerbTests
{
    // CaptiveSingleton takes the scoped IScopedDep as a constructor parameter (the captive edge);
    // ReferencingSingleton only mentions IScopedDep in a *method* signature — a reference edge, never a
    // constructor injection — so MustNotInject is silent on it where MustNotReference would fire. Both are
    // registered singletons; IScopedDep/ScopedDep are registered scoped.
    private const string Scene = """
                                 using Microsoft.Extensions.DependencyInjection;
                                 namespace App
                                 {
                                     public interface IScopedDep {}
                                     public class ScopedDep : IScopedDep {}
                                     public class CaptiveSingleton
                                     {
                                         public CaptiveSingleton(IScopedDep dep) { }
                                     }
                                     public class ReferencingSingleton
                                     {
                                         public IScopedDep Passthrough(IScopedDep dep) => dep;
                                     }
                                     public static class Wiring
                                     {
                                         public static void Configure(IServiceCollection services)
                                         {
                                             services.AddSingleton<CaptiveSingleton>();
                                             services.AddSingleton<ReferencingSingleton>();
                                             services.AddScoped<IScopedDep, ScopedDep>();
                                         }
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.ExtractWithDi(("Scene.cs", Scene));

    [Fact]
    public void MustNotInject_SingletonInjectsScoped_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.Injection);
        violation.Source!.FullName.ShouldBe("App.CaptiveSingleton");
        violation.Target!.FullName.ShouldBe("App.IScopedDep");
        violation.Sites.ShouldNotBeEmpty();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
        block.ShouldContain("App.CaptiveSingleton injects App.IScopedDep");
        block.ShouldContain("Scene.cs:"); // the injected parameter's file:line
    }

    [Fact]
    public void MustNotInject_SingletonReferencesScopedWithoutInjecting_PassesCleanWithNoWarnings()
    {
        // ReferencingSingleton mentions IScopedDep only in a method signature (a reference edge) — it takes no
        // constructor parameter of it, so MustNotInject is silent exactly where MustNotReference would fire.
        // The scoped operand resolves (IScopedDep/ScopedDep exist), so this is a real pass, not an inert one.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).WithPrefix("Referencing")
                        .MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotInject_SingletonInjectsSingleton_StaysSilentByLifetimeMembership()
    {
        // Svc injects IOther, but IOther is registered *singleton*, not scoped — so the ban on injecting
        // scoped services never fires. The scoped operand is non-empty (IUnrelated is scoped), so this proves
        // membership discriminates by lifetime, not merely that nothing scoped exists.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public interface IOther {}
                                  public class Other : IOther {}
                                  public interface IUnrelated {}
                                  public class Unrelated : IUnrelated {}
                                  public class Svc { public Svc(IOther dep) { } }
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services)
                                      {
                                          services.AddSingleton<Svc>();
                                          services.AddSingleton<IOther, Other>();
                                          services.AddScoped<IUnrelated, Unrelated>();
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotInject_ExceptSanctionedRoot_ExemptsRootButOtherSiteStaysRed()
    {
        // The DI shape: the sanctioned composition root may inject the scoped concrete type; everyone else may
        // not. .Except carves the root out of the subject set, so its captive injection is exempt while the
        // ordinary singleton in the same estate stays red.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public interface IScopedDep {}
                                  public class ScopedDep : IScopedDep {}
                                  public class OrdinarySingleton { public OrdinarySingleton(IScopedDep dep) { } }
                              }
                              namespace App.Composition
                              {
                                  public class SanctionedRoot { public SanctionedRoot(App.IScopedDep dep) { } }
                              }
                              namespace App
                              {
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services)
                                      {
                                          services.AddSingleton<OrdinarySingleton>();
                                          services.AddSingleton<App.Composition.SanctionedRoot>();
                                          services.AddScoped<IScopedDep, ScopedDep>();
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).Except(arch.Namespace("App.Composition.*"))
                        .MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.InjectionPairs().ShouldBe(["App.OrdinarySingleton -> App.IScopedDep"]);
    }

    [Fact]
    public void MustNotInject_GrandfatheredPairPasses_NewInjectedTargetStaysRed()
    {
        // The injection ratchet keys the (source, injected) type pair (GRAMMAR §4.3): Svc is grandfathered for
        // injecting IScopedA, but its NEW injection of IScopedB is a distinct identity → red.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public interface IScopedA {}
                                  public class ScopedA : IScopedA {}
                                  public interface IScopedB {}
                                  public class ScopedB : IScopedB {}
                                  public class Svc { public Svc(IScopedA a, IScopedB b) { } }
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services)
                                      {
                                          services.AddSingleton<Svc>();
                                          services.AddScoped<IScopedA, ScopedA>();
                                          services.AddScoped<IScopedB, ScopedB>();
                                      }
                                  }
                              }
                              """;
        BaselineIndex index = Index("di/no-captive", BaselineEntry.ForEdge("T:App.Svc", "T:App.IScopedA"));

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), index, arch =>
                arch.Rule("di/no-captive")
                    .Migrate("legacy captive dependencies", arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("resolve scoped work through IServiceScopeFactory"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.InjectionPairs().ShouldBe(["App.Svc -> App.IScopedB"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void MustNotInject_BystanderInjection_StaysRedWhenAnotherEdgeBaselined()
    {
        // Two singletons inject the same scoped IScopedDep; only OldSvc's edge is grandfathered. NewSvc
        // injecting the identical type is a distinct (source, injected) identity — a bystander — so it stays red.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public interface IScopedDep {}
                                  public class ScopedDep : IScopedDep {}
                                  public class OldSvc { public OldSvc(IScopedDep dep) { } }
                                  public class NewSvc { public NewSvc(IScopedDep dep) { } }
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services)
                                      {
                                          services.AddSingleton<OldSvc>();
                                          services.AddSingleton<NewSvc>();
                                          services.AddScoped<IScopedDep, ScopedDep>();
                                      }
                                  }
                              }
                              """;
        BaselineIndex index = Index("di/no-captive", BaselineEntry.ForEdge("T:App.OldSvc", "T:App.IScopedDep"));

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), index, arch =>
                arch.Rule("di/no-captive")
                    .Migrate("legacy captive dependencies", arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("resolve scoped work through IServiceScopeFactory"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.InjectionPairs().ShouldBe(["App.NewSvc -> App.IScopedDep"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void MustNotInject_EmptyRegisteredOperand_NeverWarns()
    {
        // The never-warns pin (GRAMMAR §4.7, §5.3): the scoped operand resolves empty (no scoped registrations
        // exist), which for MustNotInject is the *win condition* — no such registrations, so nothing to inject.
        // MustNotConstruct WOULD flag an empty pattern operand as an inert-target warning; ForbiddenInjection
        // drops that arm, so the result is a clean pass with ZERO warnings.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public interface IFoo {}
                                  public class Foo : IFoo {}
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services) => services.AddSingleton<IFoo, Foo>();
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void JsonReportRenderer_InjectionViolation_EmitsInjectionKindAndTargetAndOmitsMemberSlots()
    {
        // The JSON kind string is "injection" and the injected type rides the existing `target` field — no new
        // slot, schemaVersion stays 3, so member/subject slots stay omitted (null) exactly as before.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("di/no-captive")
                .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                .Because("b"));

        var writer = new StringWriter();
        JsonReportRenderer.Render(writer, report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, []);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(3);
        JsonElement violation = document.RootElement.GetProperty("rules")[0].GetProperty("violations")[0];
        violation.GetProperty("kind").GetString().ShouldBe("injection");
        violation.GetProperty("source").GetString().ShouldBe("App.CaptiveSingleton");
        violation.GetProperty("target").GetString().ShouldBe("App.IScopedDep");
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