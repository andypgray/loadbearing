using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Container-registration facts (GRAMMAR §4.7), over the MSBuild-free fast path compiled against the real
///     DI/Hosting abstractions (and, for EF Core / Microsoft.Extensions.Http — absent repo-wide — source
///     stubs in the <c>Microsoft.Extensions.DependencyInjection</c> namespace, faithful because recognition is
///     namespace + name + first-parameter). Covers the whole recognized-call table, the typeof/factory/instance
///     disambiguation, the symbol-first gate negatives, the wrapper-body-seen positive, and the honesty
///     boundary (Configure, keyed services, ServiceDescriptor).
/// </summary>
public sealed class CodebaseExtractorRegistrationTests
{
    // ── AddDbContext / AddDbContextPool (source stubs; EF Core absent repo-wide) ───────────────────────────

    private const string DbContextStub = """
                                         namespace Microsoft.Extensions.DependencyInjection
                                         {
                                             public static class EntityFrameworkServiceCollectionExtensions
                                             {
                                                 public static IServiceCollection AddDbContext<TContext>(
                                                     this IServiceCollection services,
                                                     System.Action<object>? optionsAction = null,
                                                     ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
                                                     ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
                                                     where TContext : class => services;

                                                 public static IServiceCollection AddDbContextPool<TContext>(
                                                     this IServiceCollection services,
                                                     System.Action<object> optionsAction,
                                                     int poolSize = 1024)
                                                     where TContext : class => services;
                                             }
                                         }
                                         """;

    // ── AddHttpClient (source stubs; Microsoft.Extensions.Http absent repo-wide) ───────────────────────────

    private const string HttpClientStub = """
                                          namespace Microsoft.Extensions.DependencyInjection
                                          {
                                              public interface IHttpClientBuilder {}
                                              public static class HttpClientFactoryServiceCollectionExtensions
                                              {
                                                  public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
                                                      where TClient : class => null!;
                                                  public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services)
                                                      where TClient : class where TImplementation : class, TClient => null!;
                                                  public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                                              }
                                          }
                                          """;
    // ── Add{Singleton,Scoped,Transient} — generic forms ───────────────────────────────────────────────────

    [Fact]
    public void Recognize_AddFamilyTwoTypeArgs_RecordsServiceAndImplementationPerLifetime()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public interface IBar {}
                                                                          public class Bar : IBar {}
                                                                          public interface IBaz {}
                                                                          public class Baz : IBaz {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services)
                                                                              {
                                                                                  services.AddSingleton<IFoo, Foo>();
                                                                                  services.AddScoped<IBar, Bar>();
                                                                                  services.AddTransient<IBaz, Baz>();
                                                                              }
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "N.IFoo", "N.Foo").ShouldBeTrue();
        model.HasRegistration(Lifetime.Scoped, "N.IBar", "N.Bar").ShouldBeTrue();
        model.HasRegistration(Lifetime.Transient, "N.IBaz", "N.Baz").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddSingletonOneTypeArgReceiverOnly_RecordsSelfRegistration()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Foo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton<Foo>();
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "N.Foo", "N.Foo").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddSingletonOneTypeArgWithFactory_RecordsNoImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Foo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton<Foo>(sp => new Foo());
                                                                          }
                                                                          """));

        // A factory registration names no implementation type — (Foo, null), not (Foo, Foo).
        model.HasRegistration(Lifetime.Singleton, "N.Foo", null).ShouldBeTrue();
        model.HasRegistration(Lifetime.Singleton, "N.Foo", "N.Foo").ShouldBeFalse();
    }

    // ── Add{...} — typeof forms ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_AddTypeofTwoArgs_RecordsServiceAndImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton(typeof(IFoo), typeof(Foo));
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "N.IFoo", "N.Foo").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddTypeofOneArg_RecordsSelfRegistration()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Bar {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddScoped(typeof(Bar));
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Scoped, "N.Bar", "N.Bar").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddTypeofWithFactory_RecordsNoImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton(typeof(IFoo), sp => new Foo());
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "N.IFoo", null).ShouldBeTrue();
    }

    [Fact]
    public void Recognize_OpenGenericTypeofRegistration_RecordsDefinitionLevelFqns()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IRepo<T> {}
                                                                          public class Repo<T> : IRepo<T> {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "N.IRepo<T>", "N.Repo<T>").ShouldBeTrue();
    }

    // ── TryAdd twins ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_TryAddTwins_RecordPerLifetimeLikeTheirAddSiblings()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          using Microsoft.Extensions.DependencyInjection.Extensions;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public interface IBar {}
                                                                          public class Bar : IBar {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services)
                                                                              {
                                                                                  services.TryAddSingleton<IFoo, Foo>();
                                                                                  services.TryAddScoped<IBar, Bar>();
                                                                              }
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "N.IFoo", "N.Foo").ShouldBeTrue();
        model.HasRegistration(Lifetime.Scoped, "N.IBar", "N.Bar").ShouldBeTrue();
    }

    // ── AddHostedService ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_AddHostedService_RecordsSynthesizedIHostedServiceAsServiceAndTAsImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          using Microsoft.Extensions.Hosting;
                                                                          using System.Threading;
                                                                          using System.Threading.Tasks;
                                                                          namespace N;
                                                                          public class Worker : IHostedService
                                                                          {
                                                                              public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
                                                                              public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
                                                                          }
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "Microsoft.Extensions.Hosting.IHostedService", "N.Worker").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddHostedServiceWhenIHostedServiceUnresolvable_RecordsImplementationOnly()
    {
        // Hosting.Abstractions absent, so IHostedService does not resolve; the recognizer falls back to the
        // implementation-only form (the worker in the service slot, no implementation). The call is a source
        // stub (the real extension lives in the removed assembly).
        CodebaseModel model = CompilationFactory.ExtractWithDiNoHosting(("Reg.cs", """
                                                                                   namespace Microsoft.Extensions.DependencyInjection
                                                                                   {
                                                                                       public static class HostedServiceStub
                                                                                       {
                                                                                           public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services)
                                                                                               where THostedService : class => services;
                                                                                       }
                                                                                   }
                                                                                   namespace N
                                                                                   {
                                                                                       using Microsoft.Extensions.DependencyInjection;
                                                                                       public class Worker {}
                                                                                       public static class Reg
                                                                                       {
                                                                                           public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
                                                                                       }
                                                                                   }
                                                                                   """));

        model.HasRegistration(Lifetime.Singleton, "N.Worker", null).ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddDbContextDefault_RecordsScoped()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddDbContext<MyContext>();
                       }
                       """));

        model.HasRegistration(Lifetime.Scoped, "N.MyContext", "N.MyContext").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddDbContextLiteralLifetime_IsHonored()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) =>
                               services.AddDbContext<MyContext>(null, ServiceLifetime.Singleton);
                       }
                       """));

        model.HasRegistration(Lifetime.Singleton, "N.MyContext", "N.MyContext").ShouldBeTrue();
        model.HasRegistration(Lifetime.Scoped, "N.MyContext", "N.MyContext").ShouldBeFalse();
    }

    [Fact]
    public void Recognize_AddDbContextNonLiteralLifetime_RecordsNoFact()
    {
        // A non-literal lifetime argument (a variable) is not guessed — no fact is recorded at all.
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services, ServiceLifetime lifetime) =>
                               services.AddDbContext<MyContext>(null, lifetime);
                       }
                       """));

        model.ServiceRegistrations.Any(r => r.ServiceFullName == "N.MyContext").ShouldBeFalse();
    }

    [Fact]
    public void Recognize_AddDbContextPool_RecordsScoped()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddDbContextPool<MyContext>(_ => {});
                       }
                       """));

        model.HasRegistration(Lifetime.Scoped, "N.MyContext", "N.MyContext").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddHttpClientOneTypeArg_RecordsTransientSelfClient()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", HttpClientStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class Client {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddHttpClient<Client>();
                       }
                       """));

        model.HasRegistration(Lifetime.Transient, "N.Client", "N.Client").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddHttpClientTwoTypeArgs_RecordsTransientClientAndImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", HttpClientStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public interface IClient {}
                       public class Client : IClient {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddHttpClient<IClient, Client>();
                       }
                       """));

        model.HasRegistration(Lifetime.Transient, "N.IClient", "N.Client").ShouldBeTrue();
    }

    [Fact]
    public void Recognize_AddHttpClientNamedOnly_RecordsNoFact()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", HttpClientStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddHttpClient("named");
                       }
                       """));

        model.ServiceRegistrations.ShouldBeEmpty();
    }

    // ── Gate negatives ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_UserNamespaceLookAlike_IsNotRecognized()
    {
        // AddSingleton in a user namespace (only it is in scope — MEDI's extension is not imported here) fails
        // the namespace gate.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          namespace MyApp
                                                                          {
                                                                              public static class DiExtensions
                                                                              {
                                                                                  public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddSingleton<T>(
                                                                                      this Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;
                                                                              }
                                                                          }
                                                                          namespace Consumer
                                                                          {
                                                                              using MyApp;
                                                                              public class Foo {}
                                                                              public static class Root
                                                                              {
                                                                                  public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
                                                                                      services.AddSingleton<Foo>();
                                                                              }
                                                                          }
                                                                          """));

        model.ServiceRegistrations.ShouldBeEmpty();
    }

    [Fact]
    public void Recognize_NonServiceCollectionFirstParameter_IsNotRecognized()
    {
        // A MEDI-namespace extension whose receiver is not IServiceCollection fails the first-parameter gate.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using System.Text;
                                                                          namespace Microsoft.Extensions.DependencyInjection
                                                                          {
                                                                              public static class FakeExtensions
                                                                              {
                                                                                  public static void AddSingleton<T>(this StringBuilder sb) {}
                                                                              }
                                                                          }
                                                                          namespace N
                                                                          {
                                                                              using Microsoft.Extensions.DependencyInjection;
                                                                              public class Foo {}
                                                                              public static class Root
                                                                              {
                                                                                  public static void Configure()
                                                                                  {
                                                                                      var sb = new StringBuilder();
                                                                                      sb.AddSingleton<Foo>();
                                                                                  }
                                                                              }
                                                                          }
                                                                          """));

        model.ServiceRegistrations.ShouldBeEmpty();
    }

    // ── Wrapper body seen ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_InSolutionWrapperBody_SeesTheRealCallInside()
    {
        // The wrapper call (AddMyServices, a user-namespace extension) is not recognized, but the real
        // AddSingleton inside its body IS — the pass walks every tree, wrapper bodies included.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class MyWiring
                                                                          {
                                                                              public static IServiceCollection AddMyServices(this IServiceCollection services)
                                                                              {
                                                                                  services.AddSingleton<IFoo, Foo>();
                                                                                  return services;
                                                                              }
                                                                          }
                                                                          public static class Root
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddMyServices();
                                                                          }
                                                                          """));

        model.HasRegistration(Lifetime.Singleton, "N.IFoo", "N.Foo").ShouldBeTrue();
        model.ServiceRegistrations.Count.ShouldBe(1); // only the inner real call; AddMyServices is not recognized
    }

    // ── Honesty boundary ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_Configure_IsNotRecognized()
    {
        // Configure passes the namespace + first-parameter gate but its name is not in the recognized-call
        // table — the options interfaces are framework-registered and invisible (GRAMMAR §4.7).
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class MyOptions {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.Configure<MyOptions>(o => {});
                                                                          }
                                                                          """));

        model.ServiceRegistrations.ShouldBeEmpty();
    }

    [Fact]
    public void Recognize_KeyedService_IsNotRecognized()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddKeyedSingleton<IFoo, Foo>("key");
                                                                          }
                                                                          """));

        model.ServiceRegistrations.ShouldBeEmpty();
    }

    [Fact]
    public void Recognize_ServiceDescriptorTryAddEnumerable_IsNotRecognized()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          using Microsoft.Extensions.DependencyInjection.Extensions;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) =>
                                                                                  services.TryAddEnumerable(ServiceDescriptor.Singleton<IFoo, Foo>());
                                                                          }
                                                                          """));

        model.ServiceRegistrations.ShouldBeEmpty();
    }

    // ── Whole-compilation reach (top-level Program) ───────────────────────────────────────────────────────

    [Fact]
    public void Recognize_RegistrationInTopLevelProgram_IsSeen()
    {
        // A top-level-statements Program is not a declared type, so this only holds because the registration
        // pass walks every syntax tree, not each declared type.
        CodebaseModel model = CompilationFactory.ExtractConsoleAppWithDi(("Program.cs", """
                                                                                        using Microsoft.Extensions.DependencyInjection;
                                                                                        var services = new ServiceCollection();
                                                                                        services.AddSingleton<N.IFoo, N.Foo>();

                                                                                        namespace N
                                                                                        {
                                                                                            public interface IFoo {}
                                                                                            public class Foo : IFoo {}
                                                                                        }
                                                                                        """));

        model.HasRegistration(Lifetime.Singleton, "N.IFoo", "N.Foo").ShouldBeTrue();
    }

    // ── Sites ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_RegistrationSite_IsTheCallLine()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services)
                                                                              {
                                                                                  services.AddSingleton<IFoo, Foo>();
                                                                              }
                                                                          }
                                                                          """));

        model.Registration(Lifetime.Singleton, "N.IFoo", "N.Foo").Lines().ShouldBe([9]);
    }
}