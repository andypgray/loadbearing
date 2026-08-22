using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The MSBuild tier of edge attribution where one source file compiles into several projects
///     (GRAMMAR §4.1), over the real <c>LinkedApp</c> fixture solution: <c>LinkedApp.Tool</c> compiles
///     <c>LinkedApp.Core</c>'s <c>Shared/Widget.cs</c> as its own source through a
///     <c>&lt;Compile Include&gt;</c> link and declares no <c>ProjectReference</c> to Core at all, while
///     <c>LinkedApp.Client</c> reaches Core through a real one. The fast-path bed in
///     <c>Checking.MultiplyDeclaredAttributionTests</c> hands one source text to each declaring project
///     itself, so it supplies the very shape it then reasons over; this pins the half only a real workspace
///     can, that a link written in a project file is what puts the second declarer on the node — the roster
///     both verdicts below turn on, and the one a hand-built input cannot establish.
/// </summary>
/// <remarks>
///     <para>
///         <b>The checker's half of the fixture.</b> <c>Cli.LinkedSourceGraphE2ETests</c> drives the
///         <c>graph</c> verb over these same three projects through the CLI — the rendered survey, the
///         absent Tool→Core edge, and the coverage statement naming every declarer. This class carries the
///         checker over the extracted model: which rule, anchored on which project, reaches which verdict,
///         which is the question that coverage statement exists to answer for a spec author.
///     </para>
///     <para>
///         <b>Why the red row stands beside the green one.</b> Green on its own would prove nothing — an
///         empty subject, an empty target, or a model that never extracted is green too. The two rows run
///         the same verb against the same target operand and part only on the project the subject names, so
///         the red is reachable only if both selections are live over a model that really loaded.
///     </para>
///     <para>
///         The bed is read in place through <see cref="WarmWorkspacePool" /> rather than leased through
///         <see cref="TempFixtureWorkspace" />: nothing here writes to the tree, and
///         <see cref="FixtureRestorer" />'s startup sweep has already restored every <c>TestSolutions</c>
///         bed in the output directory, so the projects load with their references resolved.
///     </para>
///     <para>
///         In the <see cref="SerialCollection">Serial</see> collection: both tests load an MSBuild
///         workspace, and the first opens one of its own.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class LinkedSourceCheckTests
{
    private const string Core = "LinkedApp.Core";
    private const string Tool = "LinkedApp.Tool";
    private const string Client = "LinkedApp.Client";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "LinkedApp", "LinkedApp.slnx");

    private static readonly Lazy<Task<CodebaseModel>> LinkedCodebase = new(ExtractAsync);

    [Fact]
    public async Task Check_ToolMustNotReferenceCore_PassesOverTheRealWorkspace()
    {
        CodebaseModel model = await LinkedCodebase.Value;

        // Tool's one outward reference is into the copy of Widget its own csproj compiles, so the edge is
        // intra-project and counts against Tool. A ban anchored on Core forbids Core's declaration, and
        // that reference never reaches it.
        Checker.Run(model, arch =>
                arch.Rule("layering/tool-must-not-reference-core")
                    .Enforce(arch.Project(Tool).MustNotReference(arch.Project(Core)))
                    .Because("The tool ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_ClientMustNotReferenceCore_FailsOnTheGenuineCrossProjectEdge()
    {
        CodebaseModel model = await LinkedCodebase.Value;

        // The control the row above is measured against: the same verb and the same target operand, moved
        // to the project that compiles no copy of the linked file. Its field crosses a project boundary
        // into a type Core alone declares, so the ban catches it.
        Checker.Run(model, arch =>
                arch.Rule("layering/client-must-not-reference-core")
                    .Enforce(arch.Project(Client).MustNotReference(arch.Project(Core)))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(
                ViolationKind.Reference, "LinkedApp.Client.Consumer", "LinkedApp.Core.CoreOnly");
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
