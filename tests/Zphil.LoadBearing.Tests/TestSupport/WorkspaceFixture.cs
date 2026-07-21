using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Assembly-shared fixture: loads the checked-in <c>MyApp</c> fixture solution once through a
///     real <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace" /> and extracts the codebase
///     model once. Tests only read <see cref="Model" />, so a single shared instance is safe.
/// </summary>
public sealed class WorkspaceFixture : IAsyncLifetime
{
    private LoadedSolution? _loaded;

    /// <summary>Absolute path to the fixture solution in the test output directory.</summary>
    public string SolutionPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp", "MyApp.sln");

    /// <summary>The extracted model. Set during <see cref="InitializeAsync" />.</summary>
    public CodebaseModel Model { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _loaded = await WorkspaceLoader.LoadAsync(SolutionPath);
        Model = await CodebaseExtractor.ExtractFromSolutionAsync(_loaded.Solution);
    }

    public ValueTask DisposeAsync()
    {
        _loaded?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Solution-relative, forward-slash rendering of a location's file path.</summary>
    public string RelativePath(SourceLocation location)
    {
        return Path.GetRelativePath(Path.GetDirectoryName(SolutionPath)!, location.FilePath).Replace('\\', '/');
    }

    /// <summary>Renders an edge as the pinned agent-facing form: <c>src -&gt; tgt @ file:line, ...</c>.</summary>
    public string RenderEdge(ReferenceEdge edge)
    {
        string sites = string.Join(", ", edge.Sites.Select(s => $"{RelativePath(s)}:{s.Line}"));
        return $"{edge.Source.FullName} -> {edge.Target.FullName} @ {sites}";
    }

    /// <summary>Renders a member-use edge as <c>src -&gt; member SymbolId @ file:line, ...</c> (GRAMMAR §4.5).</summary>
    public string RenderMemberEdge(MemberEdge edge)
    {
        string sites = string.Join(", ", edge.Sites.Select(s => $"{RelativePath(s)}:{s.Line}"));
        return $"{edge.Source.FullName} -> {edge.Member.SymbolId} @ {sites}";
    }

    /// <summary>Renders a construction edge as <c>src -&gt; constructed @ file:line, ...</c> (GRAMMAR §4.5).</summary>
    public string RenderConstructorEdge(ConstructorEdge edge)
    {
        string sites = string.Join(", ", edge.Sites.Select(s => $"{RelativePath(s)}:{s.Line}"));
        return $"{edge.Source.FullName} -> {edge.Constructed.FullName} @ {sites}";
    }

    /// <summary>Renders an injection edge as <c>src -&gt; injected @ file:line, ...</c> (GRAMMAR §4.7).</summary>
    public string RenderInjectionEdge(InjectionEdge edge)
    {
        string sites = string.Join(", ", edge.Sites.Select(s => $"{RelativePath(s)}:{s.Line}"));
        return $"{edge.Source.FullName} -> {edge.Injected.FullName} @ {sites}";
    }

    /// <summary>
    ///     Renders a registration fact as <c>lifetime service -&gt; impl @ file:line, ...</c> (GRAMMAR §4.7);
    ///     the implementation renders as <c>(none)</c> when the registration names no distinct implementation.
    /// </summary>
    public string RenderRegistration(ServiceRegistration registration)
    {
        string sites = string.Join(", ", registration.Sites.Select(s => $"{RelativePath(s)}:{s.Line}"));
        string implementation = registration.ImplementationFullName ?? "(none)";
        return $"{registration.Lifetime} {registration.ServiceFullName} -> {implementation} @ {sites}";
    }
}