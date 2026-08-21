using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The generated-code contract over real generator output, on the <c>RazorApp</c> fixture solution: a
///     Web-SDK project whose two <c>.cshtml</c> views the Razor source generator compiles into
///     <c>AspNetCoreGeneratedDocument</c> types, beside a plain project and two hand-written classes.
/// </summary>
/// <remarks>
///     <para>
///         A compiled view is the shape the attribute alone cannot see — it carries
///         <c>[RazorCompiledItemMetadata]</c> and a banner, and no <c>[GeneratedCode]</c> or
///         <c>[CompilerGenerated]</c> — which is why the regression pin uses one rather than a hand-written
///         type named to look generated. On a real web tier these outnumber the authored types, so a rule
///         written on the project noun reports against code nobody can fix.
///     </para>
///     <para>
///         In the <see cref="SerialCollection">Serial</see> collection: every test here loads an MSBuild
///         workspace, and the first opens one of its own.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class RazorGeneratedTypeTests
{
    private const string Web = "RazorApp.Web";
    private const string GeneratedNamespace = "AspNetCoreGeneratedDocument";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "RazorApp", "RazorApp.slnx");

    private static readonly Lazy<Task<CodebaseModel>> RazorCodebase = new(ExtractAsync);

    [Fact]
    public async Task ExtractFromSolutionAsync_TheCompiledViews_AreDeclaredTypesOfTheWebProject()
    {
        CodebaseModel model = await RazorCodebase.Value;

        // The guard, asserted before the flag: an empty set here means the Razor generator never ran, which
        // must not read as "the views were not generated" — the two are opposite findings and a bare
        // ShouldBeTrue on an empty sequence would report neither.
        IReadOnlyList<TypeNode> views = ViewsOf(model);
        views.Count.ShouldBe(2, "the Razor generator emitted no view types — the fixture, not the fact, is broken");
        views.ShouldAllBe(type => !type.IsExternal);
        views.ShouldAllBe(type => type.ProjectName == Web);
    }

    [Fact]
    public async Task ExtractFacts_TheCompiledViews_AreGeneratedWithoutCarryingTheAttribute()
    {
        CodebaseModel model = await RazorCodebase.Value;

        // Both halves in one assertion, because either alone would pass for the wrong reason: the views
        // report generated, AND none of them wears either attribute a detector might have leaned on. What
        // they do carry is Razor's own hosting metadata, which says nothing about provenance to anyone not
        // already looking for Razor.
        IReadOnlyList<TypeNode> views = ViewsOf(model);
        views.Count.ShouldBe(2);
        views.ShouldAllBe(type => type.IsGenerated);

        IReadOnlyList<string> attributes = views
            .SelectMany(type => type.Attributes)
            .Select(attribute => attribute.Name)
            .ToList();

        attributes.ShouldNotBeEmpty();
        attributes.ShouldNotContain("GeneratedCodeAttribute");
        attributes.ShouldNotContain("CompilerGeneratedAttribute");
    }

    [Fact]
    public async Task ExtractFacts_TheHandWrittenTypes_StayAuthored()
    {
        CodebaseModel model = await RazorCodebase.Value;

        // The contrast arm, inside the same solution: an explicit entry point and a controller in the Web
        // project — which is where a widened signal would do its damage — and the plain project beside it.
        model.Type("RazorApp.Web.Program")
            .IsGenerated.ShouldBeFalse();
        model.Type("RazorApp.Web.HomeController")
            .IsGenerated.ShouldBeFalse();
        model.Type("RazorApp.Core.CatalogService")
            .IsGenerated.ShouldBeFalse();
    }

    [Fact]
    public async Task ApplyAdjective_AuthoredOnTheWebProject_DropsTheViewsAndKeepsTheRest()
    {
        CodebaseModel model = await RazorCodebase.Value;

        var arch = new Arch();
        var evaluator = new SelectionEvaluator(model);
        Selection webProject = arch.Project(Web);

        IReadOnlyList<string> declared = Names(evaluator.Evaluate(webProject, SelectionPosition.Subject));
        IReadOnlyList<string> authored = Names(evaluator.Evaluate(webProject.Authored(), SelectionPosition.Subject));

        // The cure, end to end: the project noun sweeps the views, `.Authored()` takes exactly them, and the
        // hand-written types survive. Asserted as a set difference rather than a containment, because a
        // narrowing that took an authored type with it would be the worse failure.
        declared.ShouldContain($"{GeneratedNamespace}.Views_Home_Index");
        declared.Except(authored)
            .ShouldAllBe(name => name.StartsWith(GeneratedNamespace + ".", StringComparison.Ordinal));
        declared.Except(authored)
            .Count()
            .ShouldBe(2);
        authored.ShouldBe(["RazorApp.Web.HomeController", "RazorApp.Web.Program"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetSourceGeneratedDocumentsAsync_EveryReportedTree_IsTheCompilationsOwnInstance()
    {
        // The assumption the provenance arm rests on, pinned in its own right: the trees a workspace reports
        // as source-generated documents are the SAME INSTANCES the compilation holds. They are matched by
        // reference — a generator's pseudo-path moves the moment a build sets EmitCompilerGeneratedFiles —
        // so if Roslyn ever handed back copies, provenance would go quiet with nothing to show for it. The
        // banner would still catch Razor, which is exactly why this failure would otherwise be invisible.
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(SolutionPath, Ct);
        Project web = snapshot.Solution.Projects.Single(project => project.Name == Web);

        Compilation compilation = (await web.GetCompilationAsync(Ct))!;
        IEnumerable<SourceGeneratedDocument> documents = await web.GetSourceGeneratedDocumentsAsync(Ct);

        var compilationTrees = new HashSet<SyntaxTree>(compilation.SyntaxTrees);
        var generatedTrees = new List<SyntaxTree>();
        foreach (SourceGeneratedDocument document in documents)
            if (await document.GetSyntaxTreeAsync(Ct) is { } tree)
                generatedTrees.Add(tree);

        generatedTrees.ShouldNotBeEmpty();
        generatedTrees.ShouldAllBe(tree => compilationTrees.Contains(tree));
    }

    private static IReadOnlyList<TypeNode> ViewsOf(CodebaseModel model)
    {
        return model.Types
            .Where(type => type.Namespace == GeneratedNamespace)
            .ToList();
    }

    private static IReadOnlyList<string> Names(IEnumerable<TypeNode> types)
    {
        return types.Select(type => type.FullName)
            .ToList();
    }

    // The shared read: the pool loads the fixture once for the whole class. It takes no test's cancellation
    // token, because the task it produces outlives the test that first awaits it.
    private static async Task<CodebaseModel> ExtractAsync()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(SolutionPath, CancellationToken.None);
        return await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, null, snapshot.TargetFrameworks, null, CancellationToken.None);
    }
}
