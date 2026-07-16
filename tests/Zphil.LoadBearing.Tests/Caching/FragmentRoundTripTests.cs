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