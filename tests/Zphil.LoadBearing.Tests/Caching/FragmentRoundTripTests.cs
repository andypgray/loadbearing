using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Caching;

/// <summary>
///     Pins that persisting fragments cannot change the model: extracting a non-trivial multi-project
///     solution to <see cref="CodebaseFragment" />s, serializing them to JSON with the cache's own options,
///     deserializing, and merging yields a model whose every fact (via <see cref="ModelDump" />) equals a
///     direct merge of the in-memory fragments. This is the fidelity guarantee behind the persisted cache —
///     a hit replays these fragments through the same <see cref="FragmentMerger" /> the cold path uses.
/// </summary>
public sealed class FragmentRoundTripTests
{
    [Fact]
    public void SerializeDeserializeMerge_RichSolution_EqualsDirectMerge()
    {
        // Arrange — a solution shape that exercises every fragment DTO field: kinds (class/interface/struct/
        // enum/delegate/record), modifiers (sealed/static/abstract), a base chain, direct + transitive +
        // constructed interfaces, attributes (with an external System.Attribute), cross-project references,
        // partials across files (declaration-site union), a multi-site edge, externals, and a multi-TFM project.
        var fragments = ExtractRichSolution();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);

        string json = JsonSerializer.Serialize(fragments, ExtractionCacheStore.JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<List<CodebaseFragment>>(json, ExtractionCacheStore.JsonOptions)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — total-fact equality: the round-trip is invisible to the merged model.
        ModelDump.Render(fromCache).ShouldBe(ModelDump.Render(direct));
    }

    [Fact]
    public void Serialize_TypeKindEnum_IsWrittenAsItsName()
    {
        // Pins the enum-serialization choice: names, not integers, so a reorder cannot silently remap and a
        // rename degrades to a parse-error miss rather than a wrong value.
        var fragments = ExtractRichSolution();

        string json = JsonSerializer.Serialize(fragments, ExtractionCacheStore.JsonOptions);

        json.ShouldContain("\"Kind\":\"Interface\"");
        json.ShouldContain("\"Accessibility\":\"Public\"");
        json.ShouldNotContain("\"Kind\":0");
    }

    [Fact]
    public void SerializeDeserializeMerge_MemberEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source exercising every member-edge field and all four MemberKinds
        // (method, property, field, event), so a lost kind or dropped site would move the merged dump.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Api.cs", """
                       namespace N;
                       public class Api
                       {
                           public int Field;
                           public int Prop { get; set; }
                           public event System.Action Evt;
                           public void Do() {}
                       }
                       """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Use.cs", """
                       namespace M;
                       public class User
                       {
                           public void Go(N.Api api)
                           {
                               api.Do();
                               int p = api.Prop;
                               int f = api.Field;
                               api.Evt += OnEvt;
                           }
                           private void OnEvt() {}
                       }
                       """));
        var fragments = new[] { lib, app }.Select(FragmentExtractor.Extract).ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ExtractionCacheStore.JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<List<CodebaseFragment>>(json, ExtractionCacheStore.JsonOptions)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the member kinds serialize as names, the edges are present, and the round-trip is invisible.
        json.ShouldContain("\"MemberKind\":\"Event\"");
        direct.MemberEdges.Select(e => e.Member.SymbolId).ShouldBe(
            ["E:N.Api.Evt", "F:N.Api.Field", "M:N.Api.Do", "P:N.Api.Prop"]);
        ModelDump.Render(fromCache).ShouldBe(ModelDump.Render(direct));
    }

    [Fact]
    public void SerializeDeserializeMerge_ConstructorEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source with explicit, target-typed, and constructed-generic creations, plus
        // a delegate creation as a must-not-mint control, so a lost edge or dropped site would move the dump.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Api.cs", """
                       namespace N;
                       public class Widget {}
                       public class Box<T> {}
                       public delegate void Notify();
                       """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Use.cs", """
                       namespace M;
                       public class User
                       {
                           public object Explicit() => new N.Widget();
                           public N.Widget Implicit() { N.Widget w = new(); return w; }
                           public object Generic() => new N.Box<int>();
                           public N.Notify Del() => new N.Notify(H);
                           private static void H() {}
                       }
                       """));
        var fragments = new[] { lib, app }.Select(FragmentExtractor.Extract).ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ExtractionCacheStore.JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<List<CodebaseFragment>>(json, ExtractionCacheStore.JsonOptions)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the ctor edges are present (explicit+target-typed union to Widget, generic normalized to the
        // open definition, the delegate creation excluded), and the round-trip is invisible to the merged model.
        direct.ConstructorEdges.Select(e => (e.Source.FullName, e.Constructed.FullName)).ShouldBe(
            [("M.User", "N.Box<T>"), ("M.User", "N.Widget")]);
        ModelDump.Render(fromCache).ShouldBe(ModelDump.Render(direct));
    }

    [Fact]
    public void SerializeDeserializeMerge_InjectionEdgesAndRegistrations_SurviveRoundTripByteStably()
    {
        // Arrange — a source exercising both new fact families (GRAMMAR §4.7): a constructor-injection edge
        // and two registrations (a two-arg service/impl and a one-arg self), so a lost edge, dropped
        // registration, or mis-serialized lifetime would move the merged dump.
        CompilationInput app = CompilationFactory.CompileWithDi("App", ("Wire.cs", """
                                                                                   using Microsoft.Extensions.DependencyInjection;
                                                                                   namespace N;
                                                                                   public interface IFoo {}
                                                                                   public class Foo : IFoo {}
                                                                                   public class Svc { public Svc(IFoo foo) {} }
                                                                                   public static class Reg
                                                                                   {
                                                                                       public static void Configure(IServiceCollection services)
                                                                                       {
                                                                                           services.AddSingleton<IFoo, Foo>();
                                                                                           services.AddScoped<Svc>();
                                                                                       }
                                                                                   }
                                                                                   """));
        var fragments = new[] { app }.Select(FragmentExtractor.Extract).ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ExtractionCacheStore.JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<List<CodebaseFragment>>(json, ExtractionCacheStore.JsonOptions)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the lifetime enum serializes as a name, both fact families are present, and the round-trip
        // is invisible to the merged model.
        json.ShouldContain("\"Lifetime\":\"Singleton\"");
        direct.InjectionEdges.Select(e => (e.Source.FullName, e.Injected.FullName)).ShouldContain(("N.Svc", "N.IFoo"));
        direct.ServiceRegistrations
            .Any(r => r.Lifetime == Lifetime.Singleton && r.ServiceFullName == "N.IFoo" && r.ImplementationFullName == "N.Foo")
            .ShouldBeTrue();
        ModelDump.Render(fromCache).ShouldBe(ModelDump.Render(direct));
    }

    [Fact]
    public void SerializeDeserializeMerge_CatchAndThrowEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source exercising both new edge families (GRAMMAR §4.8): a typed catch of a
        // declared exception, a bare catch (synthesized System.Exception), a throw of a declared exception, and
        // a throw of an external one — so a lost edge, dropped site, or mis-unified endpoint would move the dump.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Errors.cs", """
                          namespace N;
                          public class DomainError : System.Exception {}
                          """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Use.cs", """
                       using System;
                       using N;
                       namespace M;
                       public class Handler
                       {
                           public void Do()
                           {
                               try { Work(); }
                               catch (DomainError) { throw; }
                               catch { throw new DomainError(); }
                           }
                           public int Parse(string s) => int.TryParse(s, out int v) ? v : throw new FormatException();
                           private void Work() {}
                       }
                       """));
        var fragments = new[] { lib, app }.Select(FragmentExtractor.Extract).ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ExtractionCacheStore.JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<List<CodebaseFragment>>(json, ExtractionCacheStore.JsonOptions)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — both families present (declared + external endpoints, bare catch → System.Exception), and the
        // round-trip is invisible to the merged model.
        direct.CatchEdges.Select(e => (e.Source.FullName, e.Caught.FullName)).ShouldBe(
            [("M.Handler", "N.DomainError"), ("M.Handler", "System.Exception")]);
        direct.ThrowEdges.Select(e => (e.Source.FullName, e.Thrown.FullName)).ShouldBe(
            [("M.Handler", "N.DomainError"), ("M.Handler", "System.FormatException")]);
        ModelDump.Render(fromCache).ShouldBe(ModelDump.Render(direct));
    }

    [Fact]
    public void SerializeDeserializeMerge_ExposureEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source exercising every exposure signature position (GRAMMAR §4.9): a
        // method's return + parameter types, a property/field/event type, plus a constructed-generic return that
        // decomposes to the open definition AND its argument (an external Task`1 endpoint riding along) — so a
        // lost edge, dropped site, or dropped decomposition endpoint would move the merged dump. Exposure edges
        // otherwise ride only ExtractRichSolution's omnibus fragment; this pins them on their own.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Types.cs", """
                         namespace N;
                         public class Widget {}
                         public class Gadget {}
                         public class Cog {}
                         public class Sprocket {}
                         public delegate void Notify();
                         """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Api.cs", """
                       namespace M;
                       public class Service
                       {
                           public N.Widget Make(N.Gadget g) => null;
                           public System.Threading.Tasks.Task<N.Widget> Load() => null;
                           public N.Cog Setting { get; set; }
                           public N.Sprocket Field;
                           public event N.Notify Ev;
                       }
                       """));
        var fragments = new[] { lib, app }.Select(FragmentExtractor.Extract).ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ExtractionCacheStore.JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<List<CodebaseFragment>>(json, ExtractionCacheStore.JsonOptions)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — every signature position surfaced (the constructed generic split to open definition + argument,
        // the external Task`1 endpoint kept whole), and the round-trip is invisible to the merged model.
        direct.ExposureEdges.Select(e => (e.Source.FullName, e.Exposed.FullName)).ShouldBe(
        [
            ("M.Service", "N.Cog"),
            ("M.Service", "N.Gadget"),
            ("M.Service", "N.Notify"),
            ("M.Service", "N.Sprocket"),
            ("M.Service", "N.Widget"),
            ("M.Service", "System.Threading.Tasks.Task<TResult>")
        ]);
        ModelDump.Render(fromCache).ShouldBe(ModelDump.Render(direct));
    }

    private static IReadOnlyList<CodebaseFragment> ExtractRichSolution()
    {
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Contracts.cs", """
                             namespace N;
                             public interface IBase {}
                             public interface IHandler<T> {}
                             public interface IDerived<T> : IBase {}
                             public abstract class Animal {}
                             public class Dog : Animal {}
                             public sealed class Puppy : Dog {}
                             public static class Helpers {}
                             public record Money(decimal Amount);
                             public struct Point {}
                             public enum Color { Red }
                             public delegate void Notify();
                             public sealed class MarkAttribute : System.Attribute {}
                             public class Msg {}
                             [Mark] public class Handler : IHandler<Msg>, IDerived<Msg> {}
                             """),
            ("SplitA.cs", """
                          namespace N;
                          public partial class Split { public System.Exception First; }
                          """),
            ("SplitB.cs", """
                          namespace N;
                          public partial class Split { public System.Exception Second; }
                          """));

        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Consumer.cs", """
                            namespace M;
                            public class Consumer
                            {
                                public N.Msg A;
                                public N.Msg B;
                                public N.Handler H;
                            }
                            """));

        // A multi-target-framework project: the same name twice with different references, so the merge's
        // BuildProjects union and cross-declarer declaration-site union are exercised on the round-trip too.
        (string Path, string Source) shared = ("Widget.cs", """
                                                            namespace P;
                                                            public class Widget {}
                                                            """);
        var legacy = new CompilationInput(CompilationFactory.Compile("P", shared).Compilation, "P", ["Legacy"]);
        var modern = new CompilationInput(CompilationFactory.Compile("P", shared).Compilation, "P", ["Modern"]);

        return new[] { lib, app, legacy, modern }
            .Select(FragmentExtractor.Extract)
            .ToList();
    }
}