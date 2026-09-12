using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Caching;

/// <summary>
///     The remap's oracle is a cold walk: a fragment extracted from one text, moved through the line maps
///     between that text and an edited one, must serialize to exactly what extracting the edited text
///     produces. Beside it, the three answers that are not a rebuilt fragment — the same instance where
///     nothing moved, null where a site's line has no entry — and the join that collapses two sites into
///     the one a cold walk reports.
/// </summary>
public sealed class FragmentSiteRemapperTests
{
    // A solution shaped to leave no site-bearing list empty: a partial class and partial method across two
    // files (multi-site type and member declarations), reference/member/construction edges, a constructor
    // taking an interface (injection), catch clauses filtered, swallowing and rethrowing, throws, public
    // signatures (exposure), and a composition root the registration recognizer reads.
    private static readonly (string Path, string Source)[] LibSources =
    [
        ("Contracts.cs", """
                         namespace N;
                         public interface IStore
                         {
                             Report Fetch();
                         }
                         public sealed class Report
                         {
                             public string Title = "t";
                         }
                         public sealed class StoreFailure : System.Exception
                         {
                         }
                         public sealed class Store : IStore
                         {
                             public Report Fetch()
                             {
                                 return new Report();
                             }
                         }
                         """),
        ("Ledger.Part1.cs", """
                            namespace N;
                            public partial class Ledger
                            {
                                public partial Report Snapshot();
                            }
                            """),
        ("Ledger.Part2.cs", """
                            namespace N;
                            public partial class Ledger
                            {
                                public partial Report Snapshot()
                                {
                                    return new Report();
                                }
                            }
                            """),
        ("Worker.cs", """
                      namespace N;
                      public sealed class Worker
                      {
                          private readonly IStore _store;
                          public Worker(IStore store)
                          {
                              _store = store;
                          }
                          public Report Run(Report seed)
                          {
                              Report first = _store.Fetch();
                              Report second = _store.Fetch();
                              try
                              {
                                  return Combine(first, second);
                              }
                              catch (StoreFailure failure) when (failure.Message.Length > 0)
                              {
                                  return seed;
                              }
                              catch (StoreFailure)
                              {
                                  return seed;
                              }
                              catch (System.InvalidOperationException)
                              {
                                  throw;
                              }
                          }
                          private static Report Combine(Report left, Report right)
                          {
                              if (left.Title.Length == 0) throw new StoreFailure();
                              if (right.Title.Length == 0) throw new StoreFailure();
                              return new Report();
                          }
                      }
                      """),
        ("Composition.cs", """
                           using Microsoft.Extensions.DependencyInjection;
                           namespace N;
                           public static class Composition
                           {
                               public static void Register(IServiceCollection services)
                               {
                                   services.AddScoped<IStore, Store>();
                                   services.AddSingleton<Ledger>();
                               }
                           }
                           """)
    ];

    private static readonly (string Path, string Source)[] AppSources =
    [
        ("Consumer.cs", """
                        namespace M;
                        public sealed class Consumer
                        {
                            public N.Report First;
                            public N.Report Second;
                        }
                        """)
    ];

    private const string NotedPair = """
                                     namespace N;
                                     // a note nothing extracts
                                     public class Pair
                                     {
                                         int first = 1;
                                     }
                                     """;

    private const string PairBefore = """
                                      namespace N;
                                      public class Pair
                                      {
                                          int first = 1;
                                          int second = 2;
                                      }
                                      """;

    private const string PairJoined = """
                                      namespace N;
                                      public class Pair
                                      { int first = 1;
                                          int second = 2;
                                      }
                                      """;

    // The compiled solutions every insertion case shares, and the two derivations taken over them. A
    // compile here binds the whole trusted-platform-assemblies closure, so each is worth building once.
    // Declaration order is load-bearing: static field initializers run in it, both LibSources and AppSources
    // are read by CompileSolution, and the last two fields read the two above them.
    private static readonly IReadOnlyList<CompilationInput> Before = CompileSolution(source => source);

    private static readonly IReadOnlyList<CompilationInput> AfterInsertions = CompileSolution(WithInsertedLines);

    private static readonly IReadOnlyDictionary<string, LineMap> InsertionMaps = MapsBetween(Before, AfterInsertions);

    private static readonly IReadOnlyList<CodebaseFragment> BeforeFragments = Before.Select(FragmentExtractor.Extract)
        .ToList();

    // One selector per site family, read twice by ShouldHaveMovedEverySiteFamily — once for the stored
    // fragment and once for the remapped one. Spelled once so a pair can never name two different families.
    private static readonly (string Family, Func<CodebaseFragment, IEnumerable<FragmentSite>> Sites)[] SiteFamilies =
    [
        ("type declaration sites", fragment => fragment.DeclaredTypes.SelectMany(type => type.DeclarationSites)),
        ("member declaration sites", fragment => fragment.DeclaredTypes.SelectMany(type => type.DeclaredMembers)
            .SelectMany(member => member.DeclarationSites)),
        ("reference edge sites", fragment => fragment.Edges.SelectMany(edge => edge.Sites)),
        ("member edge sites", fragment => fragment.MemberEdges.SelectMany(edge => edge.Sites)),
        ("construction edge sites", fragment => fragment.ConstructorEdges.SelectMany(edge => edge.Sites)),
        ("injection edge sites", fragment => fragment.InjectionEdges.SelectMany(edge => edge.Sites)),
        ("catch edge sites", fragment => fragment.CatchEdges.SelectMany(edge => edge.Sites)),
        ("unfiltered catch sites", fragment => fragment.CatchEdges.SelectMany(edge => edge.UnfilteredSites)),
        ("swallowing catch sites", fragment => fragment.CatchEdges.SelectMany(edge => edge.SwallowingSites)),
        ("throw edge sites", fragment => fragment.ThrowEdges.SelectMany(edge => edge.Sites)),
        ("exposure edge sites", fragment => fragment.ExposureEdges.SelectMany(edge => edge.Sites)),
        ("service registration sites",
            fragment => fragment.ServiceRegistrations.SelectMany(registration => registration.Sites))
    ];

    [Fact]
    public void TryRemap_LinesInsertedInEveryFile_SerializesLikeAColdWalk()
    {
        // Arrange
        List<CodebaseFragment> afterEdit = AfterInsertions.Select(FragmentExtractor.Extract)
            .ToList();

        // Act
        var remapped = new List<CodebaseFragment>();
        foreach (CodebaseFragment fragment in BeforeFragments)
            remapped.Add(FragmentSiteRemapper.TryRemap(fragment, InsertionMaps)
                .ShouldNotBeNull());

        // Assert — byte for byte, per fragment, so a dropped or misordered site cannot hide in a total.
        remapped.Count.ShouldBe(afterEdit.Count);
        for (var index = 0; index < remapped.Count; index++)
            Serialize(remapped[index])
                .ShouldBe(Serialize(afterEdit[index]));

        ShouldHaveMovedEverySiteFamily(BeforeFragments[0], remapped[0]);
    }

    [Fact]
    public void TryRemap_ArtifactSites_KeepTheirOwnLines()
    {
        // Arrange — the four sites that name a props or project file rather than a document. The `with`
        // copies, so the shared fragment the expression starts from is left as every other case sees it.
        var frameworksSite = new FragmentSite("/repo/Directory.Build.props", 7);
        var packableSite = new FragmentSite("/repo/Lib.csproj", 5);
        var locksSite = new FragmentSite("/repo/Lib.csproj", 6);
        var package = new FragmentPackageReference("Serilog", new FragmentSite("/repo/Directory.Packages.props", 12));
        CodebaseFragment fragment = BeforeFragments[0] with
        {
            DeclaredTargetFrameworks = ["net10.0"],
            TargetFrameworksSite = frameworksSite,
            PackageReferences = [package],
            IsPackable = true,
            IsPackableSite = packableSite,
            LocksPackages = true,
            LocksPackagesSite = locksSite
        };

        // Act
        CodebaseFragment remapped = FragmentSiteRemapper.TryRemap(fragment, InsertionMaps)
            .ShouldNotBeNull();

        // Assert — no document's map can claim those paths, so the four stand still while the rest moves.
        remapped.ShouldNotBeSameAs(fragment);
        remapped.TargetFrameworksSite.ShouldBe(frameworksSite);
        remapped.IsPackableSite.ShouldBe(packableSite);
        remapped.LocksPackagesSite.ShouldBe(locksSite);
        remapped.PackageReferences.ShouldNotBeNull()
            .ShouldHaveSingleItem()
            .ShouldBe(package);
    }

    [Fact]
    public void TryRemap_MapForAnotherFileOnly_ReturnsTheSameFragment()
    {
        // Arrange
        CodebaseFragment fragment = EdgeFragmentAt(3, 4);
        var maps = new Dictionary<string, LineMap>(PathComparison.Comparer)
        {
            ["Elsewhere.cs"] = MapBetween(PairBefore, PairJoined)
        };

        // Act
        CodebaseFragment? remapped = FragmentSiteRemapper.TryRemap(fragment, maps);

        // Assert
        remapped.ShouldBeSameAs(fragment);
    }

    [Fact]
    public void TryRemap_IdentityMaps_ReturnTheSameFragment()
    {
        // Arrange — a whitespace-only edit: every token keeps its line, so every map is the identity. The
        // reindented solution is this case's alone, so it stays here rather than joining the shared fields.
        IReadOnlyList<CompilationInput> after = CompileSolution(Reindented);
        IReadOnlyDictionary<string, LineMap> maps = MapsBetween(Before, after);
        maps.Values.ShouldAllBe(map => map.IsIdentity);
        CodebaseFragment fragment = BeforeFragments[0];

        // Act
        CodebaseFragment? remapped = FragmentSiteRemapper.TryRemap(fragment, maps);

        // Assert
        remapped.ShouldBeSameAs(fragment);
    }

    [Fact]
    public void TryRemap_SiteOnALineTheMapHasNoEntryFor_ReturnsNull()
    {
        // Arrange — line 2 is a comment line, so it starts no token and the map skips it.
        CodebaseFragment fragment = EdgeFragmentAt(2);
        var maps = new Dictionary<string, LineMap>(PathComparison.Comparer)
        {
            ["Pair.cs"] = MapBetween(NotedPair, NotedPair)
        };

        // Act
        CodebaseFragment? remapped = FragmentSiteRemapper.TryRemap(fragment, maps);

        // Assert
        remapped.ShouldBeNull();
    }

    [Fact]
    public void TryRemap_TwoSitesJoinedOntoOneLine_CollapseToOneSite()
    {
        // Arrange
        CodebaseFragment fragment = EdgeFragmentAt(3, 4);
        var maps = new Dictionary<string, LineMap>(PathComparison.Comparer)
        {
            ["Pair.cs"] = MapBetween(PairBefore, PairJoined)
        };

        // Act
        CodebaseFragment remapped = FragmentSiteRemapper.TryRemap(fragment, maps)
            .ShouldNotBeNull();

        // Assert — a cold walk of the joined text would report one site, and so does this.
        remapped.Edges.ShouldHaveSingleItem()
            .Sites.ShouldBe([new FragmentSite("Pair.cs", 3)]);
    }

    private static void ShouldHaveMovedEverySiteFamily(CodebaseFragment before, CodebaseFragment after)
    {
        foreach ((string family, Func<CodebaseFragment, IEnumerable<FragmentSite>> sites) in SiteFamilies)
            ShouldHaveMoved(family, sites(before), sites(after));
    }

    private static void ShouldHaveMoved(string family, IEnumerable<FragmentSite> before, IEnumerable<FragmentSite> after)
    {
        List<FragmentSite> original = before.ToList();
        List<FragmentSite> remapped = after.ToList();

        original.ShouldNotBeEmpty($"the fixture carries no {family}, so nothing about that family is tested");
        remapped.ShouldNotBe(original, $"no site among the {family} moved");
    }

    // A fragment carrying one edge and nothing else, its sites on the given lines of Pair.cs.
    private static CodebaseFragment EdgeFragmentAt(params int[] lines)
    {
        List<FragmentSite> sites = lines.Select(line => new FragmentSite("Pair.cs", line))
            .ToList();
        var edge = new FragmentEdge("N.Pair", "N.Other", sites);

        return new CodebaseFragment(
            ProjectName: "Lib",
            TargetFramework: null,
            ProjectReferences: [],
            DeclaredTypes: [],
            Externals: [],
            Edges: [edge],
            MemberEdges: [],
            ConstructorEdges: [],
            InjectionEdges: [],
            CatchEdges: [],
            ThrowEdges: [],
            ExposureEdges: [],
            ServiceRegistrations: []);
    }

    private static LineMap MapBetween(string before, string after)
    {
        SourceShape shapeBefore = SourceShape.Of(TreeOf(before));
        SourceShape shapeAfter = SourceShape.Of(TreeOf(after));

        return SourceShape.TryMapLines(shapeBefore, shapeAfter)
            .ShouldNotBeNull();
    }

    private static SyntaxTree TreeOf(string text)
    {
        return CSharpSyntaxTree.ParseText(text, path: "Pair.cs");
    }

    private static IReadOnlyDictionary<string, LineMap> MapsBetween(
        IReadOnlyList<CompilationInput> before, IReadOnlyList<CompilationInput> after)
    {
        Dictionary<string, SyntaxTree> afterByPath = after
            .SelectMany(input => input.Compilation.SyntaxTrees)
            .ToDictionary(tree => tree.FilePath, PathComparison.Comparer);

        var maps = new Dictionary<string, LineMap>(PathComparison.Comparer);
        foreach (SyntaxTree tree in before.SelectMany(input => input.Compilation.SyntaxTrees))
        {
            SourceShape shapeBefore = SourceShape.Of(tree);
            SourceShape shapeAfter = SourceShape.Of(afterByPath[tree.FilePath]);
            maps[tree.FilePath] = SourceShape.TryMapLines(shapeBefore, shapeAfter)
                .ShouldNotBeNull();
        }

        return maps;
    }

    private static IReadOnlyList<CompilationInput> CompileSolution(Func<string, string> reshape)
    {
        (string Path, string Source)[] lib = LibSources
            .Select(file => (file.Path, Source: reshape(file.Source)))
            .ToArray();
        (string Path, string Source)[] app = AppSources
            .Select(file => (file.Path, Source: reshape(file.Source)))
            .ToArray();
        CompilationInput libInput = CompilationFactory.CompileWithDi("Lib", lib);
        CompilationInput appInput = CompilationFactory.CompileReferencing("App", libInput.Compilation, "Lib", app);

        return [libInput, appInput];
    }

    // Two comment lines above the file and a blank line above every brace-only line: the edit that moves
    // every site in the fragment and changes no fact.
    private static string WithInsertedLines(string source)
    {
        var rebuilt = new StringBuilder();
        rebuilt.Append("// a note nothing extracts\n");
        rebuilt.Append("// and a second one\n");
        foreach (string line in source.Split('\n'))
        {
            if (line.Trim() == "{") rebuilt.Append('\n');

            rebuilt.Append(line);
            rebuilt.Append('\n');
        }

        return rebuilt.ToString();
    }

    // Two spaces at each end of every non-empty line: every token keeps the line it was on.
    private static string Reindented(string source)
    {
        IEnumerable<string> padded = source.Split('\n')
            .Select(line => line.Length == 0 ? line : "  " + line + "  ");

        return string.Join("\n", padded);
    }

    private static string Serialize(CodebaseFragment fragment)
    {
        IReadOnlyList<CodebaseFragment> one = [fragment];

        return JsonSerializer.Serialize(one, ManifestJson.Options);
    }
}
