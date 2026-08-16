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
        // constructed interfaces, attributes (with an external System.Attribute) on a type AND on a member, a
        // [GeneratedCode] type and the nested type that inherits the flag through the containing-type walk,
        // cross-project references, partials across files (declaration-site union), a multi-site edge,
        // externals, and a multi-TFM project.
        var fragments = ExtractRichSolution();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);

        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the member-attribute fact is non-vacuous here: without a member that actually carries one,
        // both dumps would render an empty list and a dropped field would still round-trip equal.
        direct.Type("N.Handler")
            .Member("M:N.Handler.Handle(N.Msg)")
            .AttributeNames()
            .ShouldBe([("N.MarkAttribute", "N.MarkAttribute")]);
        fromCache.Type("N.Handler")
            .Member("M:N.Handler.Handle(N.Msg)")
            .AttributeNames()
            .ShouldBe([("N.MarkAttribute", "N.MarkAttribute")]);

        // Total-fact equality: the round-trip is invisible to the merged model.
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void Serialize_TypeKindEnum_IsWrittenAsItsName()
    {
        // Pins the enum-serialization choice: names, not integers, so a reorder cannot silently remap and a
        // rename degrades to a parse-error miss rather than a wrong value.
        var fragments = ExtractRichSolution();

        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);

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
        IReadOnlyList<CodebaseFragment> fragments = new[] { lib, app }.Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the member kinds serialize as names, the edges are present, and the round-trip is invisible.
        json.ShouldContain("\"MemberKind\":\"Event\"");
        direct.MemberEdges.Select(e => e.Member.SymbolId)
            .ShouldBe(
                ["E:N.Api.Evt", "F:N.Api.Field", "M:N.Api.Do", "P:N.Api.Prop"]);
        fromCache.ShouldModelTheSameAs(direct);
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
        IReadOnlyList<CodebaseFragment> fragments = new[] { lib, app }.Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the ctor edges are present (explicit+target-typed union to Widget, generic normalized to the
        // open definition, the delegate creation excluded), and the round-trip is invisible to the merged model.
        direct.ConstructorEdges.Select(e => (e.Source.FullName, e.Constructed.FullName))
            .ShouldBe(
                [("M.User", "N.Box<T>"), ("M.User", "N.Widget")]);
        fromCache.ShouldModelTheSameAs(direct);
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
        IReadOnlyList<CodebaseFragment> fragments = new[] { app }.Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the lifetime enum serializes as a name, both fact families are present, and the round-trip
        // is invisible to the merged model.
        json.ShouldContain("\"Lifetime\":\"Singleton\"");
        direct.InjectionEdges.Select(e => (e.Source.FullName, e.Injected.FullName))
            .ShouldContain(("N.Svc", "N.IFoo"));
        direct.ServiceRegistrations
            .Any(r => r.Lifetime == Lifetime.Singleton && r.ServiceFullName == "N.IFoo" && r.ImplementationFullName == "N.Foo")
            .ShouldBeTrue();
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void SerializeDeserializeMerge_CatchAndThrowEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source exercising both new edge families (GRAMMAR §4.8): a typed catch of a
        // declared exception, a bare catch (synthesized System.Exception), a throw of a declared exception, and
        // a throw of an external one — so a lost edge, dropped site, or mis-unified endpoint would move the dump.
        // The `when`-filtered clause keeps the unfiltered-site subset non-vacuous: without it every site would
        // be unfiltered and a subset dropped in serialization would still round-trip equal.
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
                               catch (DomainError e) when (e.Message.Length > 0) { }
                               catch (DomainError) { throw; }
                               catch { throw new DomainError(); }
                           }
                           public int Parse(string s) => int.TryParse(s, out int v) ? v : throw new FormatException();
                           private void Work() {}
                           public void Swallow()
                           {
                               try { Work(); }
                               catch (DomainError) { }
                           }
                       }
                       """));
        IReadOnlyList<CodebaseFragment> fragments = new[] { lib, app }.Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — both families present (declared + external endpoints, bare catch → System.Exception), the
        // DomainError edge's two nested subsets are each proper subsets of the one before (the filtered clause at
        // line 9 is absent from the unfiltered set; the rethrowing clause at line 10 is unfiltered but not
        // swallowing; only the bare-block clause at line 18 swallows), the bare `catch` at line 11 counts as
        // unfiltered and its `throw new` keeps it out of the swallowing set, and the round-trip is invisible.
        direct.CatchEdges.Select(e => (e.Source.FullName, e.Caught.FullName))
            .ShouldBe(
                [("M.Handler", "N.DomainError"), ("M.Handler", "System.Exception")]);
        direct.ThrowEdges.Select(e => (e.Source.FullName, e.Thrown.FullName))
            .ShouldBe(
                [("M.Handler", "N.DomainError"), ("M.Handler", "System.FormatException")]);
        direct.CatchEdge("M.Handler", "N.DomainError")
            .Lines()
            .ShouldBe([9, 10, 18]);
        direct.CatchEdge("M.Handler", "N.DomainError")
            .UnfilteredLines()
            .ShouldBe([10, 18]);
        direct.CatchEdge("M.Handler", "N.DomainError")
            .SwallowingLines()
            .ShouldBe([18]);
        direct.CatchEdge("M.Handler", "System.Exception")
            .UnfilteredLines()
            .ShouldBe([11]);
        direct.CatchEdge("M.Handler", "System.Exception")
            .SwallowingLines()
            .ShouldBeEmpty();
        fromCache.ShouldModelTheSameAs(direct);
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
        IReadOnlyList<CodebaseFragment> fragments = new[] { lib, app }.Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — every signature position surfaced (the constructed generic split to open definition + argument,
        // the external Task`1 endpoint kept whole), and the round-trip is invisible to the merged model.
        direct.ExposureEdges.Select(e => (e.Source.FullName, e.Exposed.FullName))
            .ShouldBe(
            [
                ("M.Service", "N.Cog"),
                ("M.Service", "N.Gadget"),
                ("M.Service", "N.Notify"),
                ("M.Service", "N.Sprocket"),
                ("M.Service", "N.Widget"),
                ("M.Service", "System.Threading.Tasks.Task<TResult>")
            ]);
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void RoundTrip_MultiFrameworkFragments_PreserveTheirTargetFramework()
    {
        // Arrange — one project's two frameworks, both declaring P.Widget. The framework is the half of a
        // fragment's identity the project name no longer supplies, so it has to survive the cache.
        (string Path, string Source) shared = ("Widget.cs", """
                                                            namespace P;
                                                            public class Widget {}
                                                            """);
        var modern = new CompilationInput(CompilationFactory.Compile("P", shared)
            .Compilation, "P", [], "net10.0");
        var legacy = new CompilationInput(CompilationFactory.Compile("P", shared)
            .Compilation, "P", [], "netstandard2.0");
        IReadOnlyList<CodebaseFragment> fragments = new[] { modern, legacy }
            .Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;

        // Assert — the field itself, and then the observable that depends on it. The merge's
        // multi-framework note is a pure function of the fragments' frameworks, so a dropped field would
        // replay as silence on a cache hit while the cold run spoke — which no dump comparison would catch,
        // since ModelDump does not render the notes.
        roundTripped.Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);
        direct.MergeNotes.ShouldNotBeEmpty();
        fromCache.MergeNotes.ShouldBe(direct.MergeNotes);
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void RoundTrip_SolutionMembership_SurvivesAllThreeStatesOntoTheProjectNodes()
    {
        // Arrange — a declared member, a passenger, and a project nothing was read about. The cache is the
        // one place all three can be flattened into the same absent-or-default value, which is precisely the
        // failure this pins: a hit replaying a passenger as a member would have `render` draw it.
        CompilationInput member = CompilationFactory.Compile("Member", ("Member.cs", """
                                                                                     namespace Member;
                                                                                     public class A {}
                                                                                     """)) with
        {
            SolutionMember = true
        };
        CompilationInput passenger = CompilationFactory.Compile("Passenger", ("Passenger.cs", """
                                                                                              namespace Passenger;
                                                                                              public class B {}
                                                                                              """)) with
        {
            SolutionMember = false
        };
        CompilationInput unread = CompilationFactory.Compile("Unread", ("Unread.cs", """
                                                                                     namespace Unread;
                                                                                     public class C {}
                                                                                     """));
        IReadOnlyList<CodebaseFragment> fragments = new[] { member, passenger, unread }
            .Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        string json = JsonSerializer.Serialize(fragments, ManifestJson.Options);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;

        // Assert — the field, then the model it lands on, since the fragment is only a carrier.
        roundTripped.Select(fragment => fragment.SolutionMember)
            .ShouldBe([true, false, null]);
        FragmentMerger.Merge(roundTripped)
            .Projects.Select(project => (project.Name, project.SolutionMember))
            .ShouldBe([("Member", true), ("Passenger", false), ("Unread", null)]);
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
                             [Mark] public class Handler : IHandler<Msg>, IDerived<Msg> { [Mark] public void Handle(Msg m) {} }
                             [System.CodeDom.Compiler.GeneratedCode("Tool", "1.0")] public class Emitted { public class Inner {} }
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
        var legacy = new CompilationInput(CompilationFactory.Compile("P", shared)
            .Compilation, "P", ["Legacy"]);
        var modern = new CompilationInput(CompilationFactory.Compile("P", shared)
            .Compilation, "P", ["Modern"]);

        return new[] { lib, app, legacy, modern }
            .Select(FragmentExtractor.Extract)
            .ToList();
    }
}
