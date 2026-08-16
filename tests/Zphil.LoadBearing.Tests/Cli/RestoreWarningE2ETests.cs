using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The measured field defect, end to end over a solution that really does raise it: a NuGet restore
///     warning must render and must not decide anything.
/// </summary>
/// <remarks>
///     <para>
///         <b>What was measured.</b> A published build refused a solution — exit 2, "the model is incomplete"
///         — in the same run in which it evaluated the solution's one rule and passed it. The whole cause was
///         <c>NU1510</c>, NuGet's advice that a package the shared framework now carries could be removed
///         from the csproj. Silencing that one warning flipped the run to exit 0, so which warning gated was
///         measured rather than inferred. .NET 10 emits it for a large and growing set of packages, so the
///         population is every repository still referencing one.
///     </para>
///     <para>
///         <b>Why a real bed rather than an injected string.</b>
///         <see cref="WorkspaceDiagnosticsGateE2ETests" /> pins the decision with the field text injected,
///         which is the sharper test of the predicate; this class pins that the text still arrives. The
///         warning is baked into <c>project.assets.json</c> at restore time and replayed into every later
///         design-time build, so nothing short of a real restore proves the diagnostic reaches a workspace
///         at all — and a fix that quietly stopped surfacing it would pass the injected tests.
///     </para>
///     <para>
///         The package is <c>Microsoft.CSharp</c>, chosen because it raises <c>NU1510</c> and nothing else.
///         The package the field measurement used raises a critical-severity advisory alongside it, which has
///         no business being a committed dependency of this repository's test tree.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class RestoreWarningE2ETests
{
    // A package absorbed into the shared framework, which is what makes NuGet emit the pruning advice.
    private const string PrunablePackage =
        "    <ItemGroup>\n        <PackageReference Include=\"Microsoft.CSharp\" Version=\"4.7.0\" />\n    </ItemGroup>\n";

    [Fact]
    public async Task Check_SolutionRaisingAPruningRestoreWarning_RendersItAndPassesTheRule()
    {
        using var workspace = new TempFixtureWorkspace();
        AddPrunablePackageReference(workspace);

        CliResult check = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache");

        // Exit 0: the rule passed and nothing failed to load, so there is nothing to refuse.
        check.ShouldSucceed();
        // The warning is real and present — without this the assertion above would pass vacuously on a
        // solution that simply never raised one.
        check.Err.ShouldContain("warning: ");
        check.Err.ShouldContain("will not be pruned");
        check.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task CheckJson_SolutionRaisingAPruningRestoreWarning_CarriesItWithoutStampingIncomplete()
    {
        using var workspace = new TempFixtureWorkspace();
        AddPrunablePackageReference(workspace);

        CliResult check = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json");

        // The MCP-facing half: a client reading modelIncomplete must not be told the answer is untrustworthy
        // because a package could stand to be removed from a csproj.
        check.ShouldSucceed("\"workspaceDiagnostics\"");
        check.Out.ShouldContain("will not be pruned");
        check.Out.ShouldNotContain("\"modelIncomplete\"");
        check.Out.ShouldNotContain("\"failedProjects\"");
    }

    // Adds the package to MyApp.Domain and restores, so the warning is written into the assets file the
    // design-time build replays. Domain is the one fixture project with NuGetAudit already off, which keeps
    // this bed's diagnostics to the single warning it is about whatever feeds the host machine has
    // configured. The lease reset restores the pristine csproj for the next test in this class.
    private static void AddPrunablePackageReference(TempFixtureWorkspace workspace)
    {
        string csproj = workspace.PathOf("MyApp.Domain", "MyApp.Domain.csproj");
        string text = File.ReadAllText(csproj);
        File.WriteAllText(csproj, text.Replace("</Project>", PrunablePackage + "</Project>"));
        FixtureRestorer.Restore(workspace.SolutionPath);
    }
}
