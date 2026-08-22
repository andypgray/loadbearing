using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The completeness verb <c>MustBeRegistered</c> over the fast path (GRAMMAR §5.3, §4.7, §4.3): a
///     subject is red unless it is a member of <c>arch.Registered()</c> — the union of the service and
///     implementation names of every recognized registration, at any lifetime — so the verb and the noun
///     cannot drift.
/// </summary>
/// <remarks>
///     The verb reads the §4.7 registration facts extraction already records, so it adds no extracted fact,
///     no cache-schema change and nothing for the extraction tests to cover. It inverts the polarity of that
///     fact's honesty boundary, which is why the no-registrations row below is pinned as behaviour: a
///     registration the extraction cannot see makes a correctly registered type a false <em>red</em> rather
///     than a missed violation. Violations are <see cref="ViolationKind.Shape" />, identity the subject
///     symbol ID riding <see cref="BaselineEntry.ForSubject" />.
/// </remarks>
public sealed class MustBeRegisteredVerbTests
{
    // One AddScoped<IBar, Bar>() registration, an unregistered type beside it, and the composition root that
    // wires them — the smallest scene carrying both a green and a red.
    private const string Scene = """
                                 using Microsoft.Extensions.DependencyInjection;
                                 namespace App
                                 {
                                     public interface IBar {}
                                     public class Bar : IBar {}
                                     public class Orphan {}
                                     public static class Wiring
                                     {
                                         public static void Configure(IServiceCollection services)
                                         {
                                             services.AddScoped<IBar, Bar>();
                                         }
                                     }
                                 }
                                 """;

    // One service per lifetime, so a single rule answers whether the verb reads a lifetime at all.
    private const string Lifetimes = """
                                     using Microsoft.Extensions.DependencyInjection;
                                     namespace App
                                     {
                                         public interface ISingletonDep {}
                                         public class SingletonDep : ISingletonDep {}
                                         public interface IScopedDep {}
                                         public class ScopedDep : IScopedDep {}
                                         public interface ITransientDep {}
                                         public class TransientDep : ITransientDep {}
                                         public static class Wiring
                                         {
                                             public static void Configure(IServiceCollection services)
                                             {
                                                 services.AddSingleton<ISingletonDep, SingletonDep>();
                                                 services.AddScoped<IScopedDep, ScopedDep>();
                                                 services.AddTransient<ITransientDep, TransientDep>();
                                             }
                                         }
                                     }
                                     """;

    // Two unregistered types beside a registered one, so the ratchet has an identity to bless and another to
    // keep red.
    private const string RatchetScene = """
                                        using Microsoft.Extensions.DependencyInjection;
                                        namespace App
                                        {
                                            public class Known {}
                                            public class Orphan {}
                                            public class Stray {}
                                            public static class Wiring
                                            {
                                                public static void Configure(IServiceCollection services)
                                                {
                                                    services.AddSingleton<Known>();
                                                }
                                            }
                                        }
                                        """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.ExtractWithDi(("Scene.cs", Scene));

    private static readonly CodebaseModel LifetimesModel = CompilationFactory.ExtractWithDi(("Scene.cs", Lifetimes));

    private static readonly CodebaseModel RatchetModel = CompilationFactory.ExtractWithDi(("Scene.cs", RatchetScene));

    [Fact]
    public void MustBeRegistered_ServiceAndImplementationOfOneRegistration_BothSatisfy()
    {
        // AddScoped<IBar, Bar>() makes BOTH names members — the service ∪ implementation union the noun
        // resolves — so the interface and the class registered against it green under one rule. The subject
        // is non-empty by construction: an empty one would fail rather than pass.
        Checker.Run(SceneModel, arch =>
                arch.Rule("di/services-registered")
                    .Enforce(arch.Types.WithNameMatching("*Bar").MustBeRegistered())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBeRegistered_UnregisteredSubject_FailsWithSubjectAndDeclarationSites()
    {
        // A shape violation has no edge to cite, so the file:line an agent jumps to is where the unregistered
        // type is declared — the subject's own declaration sites, carried as evidence.
        Checker.Run(SceneModel, arch =>
                arch.Rule("di/services-registered")
                    .Enforce(arch.Types.Except(arch.Types.WithNameMatching("Wiring")).MustBeRegistered())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjectAtSites("App.Orphan", ["Scene.cs:6"]);
    }

    [Fact]
    public void MustBeRegistered_IsLifetimeAgnostic_SingletonScopedAndTransientAllSatisfy()
    {
        // Membership is arch.Registered() with no lifetime — the union across all three — so a rule using the
        // verb never has an opinion about how long an instance lives.
        Checker.Run(LifetimesModel, arch =>
                arch.Rule("di/services-registered")
                    .Enforce(arch.Types.WithSuffix("Dep").MustBeRegistered())
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBeRegistered_NoRegistrationsAtAll_RedsEverySubject()
    {
        // The verb's polarity, pinned as behaviour rather than left to be discovered. Registration is read
        // from source-visible container calls, so wiring the extraction cannot see — assembly scanning, a
        // keyed overload, a raw ServiceDescriptor, an extension compiled into a package — reds every subject
        // instead of missing a violation. An estate that registers by convention should not use the verb, and
        // this is what ignoring that looks like.
        RuleResult result = Checker.Run(
                "namespace App { public interface IBar {} public class Bar : IBar {} }",
                arch => arch.Rule("di/services-registered")
                    .Enforce(arch.Types.MustBeRegistered())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.IBar", "App.Bar"]);
    }

    [Fact]
    public void MustBeRegistered_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the completeness verb takes the same subject gate.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("di/empty")
                    .Enforce(arch.Namespace("Nowhere.*").MustBeRegistered())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustBeRegistered_GrandfatheredUnregisteredTypePasses_NewOneStaysRed()
    {
        // Identity is the subject symbol ID (GRAMMAR §4.3), one entry per unregistered type. App.Orphan is
        // blessed; App.Stray is a distinct identity the same rule keeps red.
        BaselineIndex index = Checker.Baselines("di/services-registered", BaselineEntry.ForSubject("T:App.Orphan"));

        RuleResult result = Checker.Run(RatchetModel, index, arch =>
                arch.Rule("di/services-registered")
                    .Migrate(
                        "Some services are still constructed by hand.",
                        arch.Types.Except(arch.Types.WithNameMatching("Wiring")).MustBeRegistered())
                    .Because("A service the container does not know cannot be replaced in a test."))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Stray"]);
        result.ShouldHaveGrandfathered(1);
    }
}
