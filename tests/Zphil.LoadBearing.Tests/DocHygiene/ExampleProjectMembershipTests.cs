using System.Xml.Linq;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate over which projects under <c>examples/</c> a solution declares. Every one of them is
///     declared by the example it belongs to, with a single recorded exception: the xUnit adapter test
///     project under <c>Meridian.Quoting</c>, which is a member of nothing on purpose. Both halves are
///     asserted, because each is only safe while the other holds.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why the exception is not a defect.</b> The adapter project demonstrates the consumer
///         outside the solution it checks — the walk-up that <c>ArchRuleTests&lt;T&gt;</c> performs to
///         find its target — and declaring it as a member dissolves the thing being demonstrated. It
///         would also break two working guarantees: spec discovery picks the unique declared member
///         referencing the contract library, and this project references it transitively through the
///         adapter, so a declared one turns every spec-less <c>check</c> in the example, in the
///         packaged quickstart walk and in the published quickstart into a "Multiple spec projects
///         found" refusal; and <see cref="ExampleArchitectureDocTests" /> holds the committed survey
///         fence to exactly the declared set, so it would demand a redrawn drawing with a test node in
///         it. The csproj carries the same reasoning where somebody editing it will meet it.
///     </para>
///     <para>
///         <b>Why the workflow fact sits beside it.</b> "In no solution" is only tolerable because
///         something else builds and runs the project, and that something is one line of one workflow.
///         Delete the line and the first arm still passes while the project silently stops being
///         compiled at all — the exact shape of rot the exception was reasoned into being. Reading the
///         workflow from a test is the same move
///         <see cref="CodeQlActionPinTests" /> and <see cref="DependabotCoverageTests" /> make.
///     </para>
///     <para>
///         <b>Why the first arm is an inventory rather than a pin on one path.</b> A fact that only
///         said "this project is in no solution" would say nothing about the next project that lands in
///         no solution. Phrased as "every example project is declared except this one", a new orphan
///         reds here on the day it appears, and the exception stays one line rather than a policy.
///     </para>
/// </remarks>
public sealed class ExampleProjectMembershipTests
{
    private const string AdapterProject =
        "examples/Meridian.Quoting/tests/Meridian.Quoting.ArchTests/Meridian.Quoting.ArchTests.csproj";

    private const string Workflow = ".github/workflows/ci.yml";

    // Every project path any tracked solution declares, repository-relative. Built once: the arms
    // below ask about it for each of the example projects, and the answer cannot change mid-run.
    private static readonly Lazy<IReadOnlySet<string>> LazyDeclaredProjects = new(DeclaredProjects);

    [Fact]
    public void EveryExampleProject_IsDeclaredByASolution_ExceptTheAdapter()
    {
        // Act
        List<string> undeclared = ExampleProjects()
            .Where(static path => path != AdapterProject)
            .Where(static path => !LazyDeclaredProjects.Value.Contains(path))
            .ToList();

        // Assert: an example project no solution declares is built by nothing, checked by nothing and
        // reached by no inspection — and it looks exactly like one that is, because the file is there
        // and it compiles when something else happens to reference it.
        undeclared.ShouldReportNothing(
            "Example project(s) that no solution declares, so nothing above them builds or inspects them");
    }

    [Fact]
    public void TheAdapterProject_IsDeclaredByNoSolution()
    {
        // Arrange: the arm is worthless if the project has been moved or deleted, because a path
        // nothing declares and a path that does not exist read identically from here.
        ExampleProjects()
            .ShouldContain(AdapterProject, $"{AdapterProject} is no longer a tracked project.");

        // Act
        List<string> declaring = TrackedSolutions()
            .Where(static solution => DeclaredBy(solution)
                .Contains(AdapterProject, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Assert
        declaring.ShouldReportNothing(
            "The xUnit adapter test project is a member of no solution on purpose — its csproj says "
            + "why, at length. Declaring it makes it a second spec-project candidate, which turns "
            + "every spec-less check of this example into a \"Multiple spec projects found\" refusal, "
            + "and it obliges the committed drawing to grow a test node. Solution(s) now declaring it");
    }

    [Fact]
    public void TheAdapterProject_IsBuiltAndRunByTheExamplesWorkflow()
    {
        // Act
        string workflow = RepoRoot.ReadText(Workflow);

        // Assert: the coverage that makes membership of no solution safe. Nothing else in CI compiles
        // this project, so this line going missing removes it from the build entirely, in silence.
        workflow.ShouldContain(
            $"dotnet test {AdapterProject}",
            $"{Workflow} no longer runs the xUnit adapter test project. It is a member of no solution, "
            + "so this line is the only thing that builds it — without it the project is compiled by "
            + "nothing and its rule tests run nowhere, with no other gate saying so.");
    }

    private static IReadOnlyList<string> ExampleProjects()
    {
        return TrackedFiles.All
            .Where(static path => path.StartsWith("examples/", StringComparison.Ordinal))
            .Where(static path => path.EndsWith(".csproj", StringComparison.Ordinal))
            .ToList();
    }

    private static IReadOnlyList<string> TrackedSolutions()
    {
        return TrackedFiles.All
            .Where(static path => path.EndsWith(".slnx", StringComparison.Ordinal))
            .ToList();
    }

    private static IReadOnlySet<string> DeclaredProjects()
    {
        return TrackedSolutions()
            .SelectMany(DeclaredBy)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // The projects one solution declares, as repository-relative paths — resolved against the
    // solution's own directory, because a declared path is relative to the file that declares it.
    private static IReadOnlyList<string> DeclaredBy(string solutionRelativePath)
    {
        string solutionPath = RepoRoot.Absolute(solutionRelativePath);
        string solutionDirectory = Path.GetDirectoryName(solutionPath)!;

        return XDocument.Load(solutionPath)
            .Descendants("Project")
            .Select(static element => (string?)element.Attribute("Path"))
            .Where(static path => path is not null)
            .Select(path => Path.Combine(solutionDirectory, path!.Replace('\\', '/')))
            .Select(RepoRoot.Relative)
            .ToList();
    }
}
