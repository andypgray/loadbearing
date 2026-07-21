using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Membership semantics of the <c>arch.Registered</c> noun (GRAMMAR §4.7, §5.1), observed through the
///     <c>MustNotInject</c> verb over the fast path: <c>Registered(lifetime)</c> is the union of the service
///     and implementation FQNs of the recognized registrations at that lifetime; <c>Registered()</c> unions
///     every lifetime; the position-correct universe means an external registered type matches in <b>target</b>
///     position but never enters a <b>subject</b> (§4.1); and an empty <c>Registered</c> subject fails with the
///     shared pinned message. Membership is resolved model-side against the registration facts, never
///     denormalized onto a <see cref="TypeNode" />.
/// </summary>
public sealed class RegisteredNounMembershipTests
{
    [Fact]
    public void Registered_Lifetime_MembershipIsServiceUnionImplementation()
    {
        // AddScoped<IBar, Bar> makes BOTH IBar (service) and Bar (implementation) members of Registered(Scoped).
        // Consumer injects both, so both trip — proving the union, not just the service side.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public interface IBar {}
                                  public class Bar : IBar {}
                                  public class Consumer { public Consumer(IBar svc, Bar impl) { } }
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services)
                                      {
                                          services.AddSingleton<Consumer>();
                                          services.AddScoped<IBar, Bar>();
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.InjectionPairs().ShouldBe(
            ["App.Consumer -> App.IBar", "App.Consumer -> App.Bar"], true);
    }

    [Fact]
    public void Registered_NoArg_MembershipIsUnionOfAllLifetimes()
    {
        // arch.Registered() (no lifetime) is the union across singleton, scoped, and transient registrations.
        // Consumer injects one service of each lifetime; the no-arg operand matches all three.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public interface ISingletonDep {}
                                  public class SingletonDep : ISingletonDep {}
                                  public interface IScopedDep {}
                                  public class ScopedDep : IScopedDep {}
                                  public interface ITransientDep {}
                                  public class TransientDep : ITransientDep {}
                                  public class Consumer { public Consumer(ISingletonDep a, IScopedDep b, ITransientDep c) { } }
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services)
                                      {
                                          services.AddSingleton<Consumer>();
                                          services.AddSingleton<ISingletonDep, SingletonDep>();
                                          services.AddScoped<IScopedDep, ScopedDep>();
                                          services.AddTransient<ITransientDep, TransientDep>();
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered()))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.InjectionPairs().ShouldBe(
            ["App.Consumer -> App.ISingletonDep", "App.Consumer -> App.IScopedDep", "App.Consumer -> App.ITransientDep"],
            true);
    }

    [Fact]
    public void Registered_ExternalRegisteredType_MatchesInTargetPosition()
    {
        // AddScoped<System.IDisposable, Handler> registers an EXTERNAL service (System.IDisposable). In target
        // position the Registered(Scoped) operand matches externals (§4.1), so Consumer injecting the external
        // interface is a captive violation.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public class Handler : System.IDisposable { public void Dispose() { } }
                                  public class Consumer { public Consumer(System.IDisposable dep) { } }
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services)
                                      {
                                          services.AddSingleton<Consumer>();
                                          services.AddScoped<System.IDisposable, Handler>();
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.InjectionPairs().ShouldBe(["App.Consumer -> System.IDisposable"]);
    }

    [Fact]
    public void Registered_ExternalRegisteredType_NeverEntersASubject()
    {
        // The only scoped registration is a self-registration of the external System.IDisposable. In SUBJECT
        // position the noun intersects solution-declared types (§4.1), so the external is excluded — the subject
        // resolves empty and the rule fails with the shared pinned message (how an author discovers the
        // visibility boundary), never silently passing.
        const string source = """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace App
                              {
                                  public static class Wiring
                                  {
                                      public static void Configure(IServiceCollection services) => services.AddScoped(typeof(System.IDisposable));
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(CompilationFactory.ExtractWithDi(("Scene.cs", source)), arch =>
                arch.Rule("di/no-captive")
                    .Enforce(arch.Registered(Lifetime.Scoped).MustNotInject(arch.Types))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe(ConstraintEvaluator.EmptySubjectMessage);
    }
}