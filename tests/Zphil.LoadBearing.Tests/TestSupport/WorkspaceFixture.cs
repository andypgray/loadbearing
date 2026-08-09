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
    /// <summary>Absolute path to the fixture solution in the test output directory.</summary>
    public string SolutionPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp", "MyApp.sln");

    /// <summary>The extracted model. Set during <see cref="InitializeAsync" />.</summary>
    public CodebaseModel Model { get; private set; } = null!;

    /// <summary>Opens the fixture solution and extracts <see cref="Model" /> from it.</summary>
    /// <remarks>
    ///     Loads through the shared <see cref="WarmWorkspacePool" /> rather than owning a workspace of its
    ///     own, so the load is shared with anything else reading this solution and the workspace stays
    ///     bounded by the pool's lifetime instead of being pinned for the whole run.
    /// </remarks>
    public async ValueTask InitializeAsync()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(SolutionPath, CancellationToken.None);
        Model = await CodebaseExtractor.ExtractFromSolutionAsync(snapshot.Solution);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>Solution-relative, forward-slash rendering of a location's file path.</summary>
    public string RelativePath(SourceLocation location)
    {
        return Path.GetRelativePath(Path.GetDirectoryName(SolutionPath)!, location.FilePath)
            .Replace('\\', '/');
    }

    /// <summary>Renders an edge as the pinned agent-facing form: <c>src -&gt; tgt @ file:line, ...</c>.</summary>
    public string RenderEdge(ReferenceEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Target.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders a member-use edge as <c>src -&gt; member SymbolId @ file:line, ...</c> (GRAMMAR §4.5).</summary>
    public string RenderMemberEdge(MemberEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Member.SymbolId} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders a construction edge as <c>src -&gt; constructed @ file:line, ...</c> (GRAMMAR §4.5).</summary>
    public string RenderConstructorEdge(ConstructorEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Constructed.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders an injection edge as <c>src -&gt; injected @ file:line, ...</c> (GRAMMAR §4.7).</summary>
    public string RenderInjectionEdge(InjectionEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Injected.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders an exposure edge as <c>src -&gt; exposed @ file:line, ...</c> (GRAMMAR §4.9).</summary>
    public string RenderExposureEdge(ExposureEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Exposed.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>
    ///     Renders a registration fact as <c>lifetime service -&gt; impl @ file:line, ...</c> (GRAMMAR §4.7);
    ///     the implementation renders as <c>(none)</c> when the registration names no distinct implementation.
    /// </summary>
    public string RenderRegistration(ServiceRegistration registration)
    {
        string implementation = registration.ImplementationFullName ?? "(none)";
        return $"{registration.Lifetime} {registration.ServiceFullName} -> {implementation} @ {Sites(registration.Sites)}";
    }

    /// <summary>The site list every render above ends in: <c>file:line</c>, comma-separated, in model order.</summary>
    private string Sites(IEnumerable<SourceLocation> sites)
    {
        return string.Join(", ", sites.Select(s => $"{RelativePath(s)}:{s.Line}"));
    }
}
