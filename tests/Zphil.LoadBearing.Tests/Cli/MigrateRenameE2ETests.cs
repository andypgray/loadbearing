using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The rename acceptance: baselines key on stable symbol IDs, not
///     <c>file:line</c>, so they survive a file move. Against a private copy of the fixture whose
///     controllers' <c>DataTable</c> sites are fully grandfathered (the clean spec's
///     <c>arch/clean-baseline.json</c>), moving <c>InvoiceController.cs</c> into a <c>Controllers/</c>
///     subdirectory — its namespace, and therefore its DocID, unchanged — leaves the grandfathering
///     intact, so <c>check</c> still exits 0 while the underlying <c>file:line</c> has changed.
/// </summary>
[Collection("Serial")]
public sealed class MigrateRenameE2ETests
{
    [Fact]
    public async Task Check_AfterFileMove_GrandfatheredEntriesStillMatch()
    {
        using var workspace = new TempFixtureWorkspace();

        // Plain move within the project: the SDK glob still compiles it, and `namespace MyApp.Web;`
        // (declared in the file, not folder-derived) is unchanged — so T:MyApp.Web.InvoiceController holds.
        string source = workspace.PathOf("MyApp.Web", "InvoiceController.cs");
        string destination = workspace.PathOf("MyApp.Web", "Controllers", "InvoiceController.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);

        // The clean spec grandfathers both DataTable sites via arch/clean-baseline.json. The DocID
        // survived the move, so the Migrate rule stays fully grandfathered and the whole spec is clean.
        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll);
        check.Exit.ShouldBe(0);

        // Belt-and-braces: the burndown confirms both remain grandfathered and none went stale.
        CliResult status = await CliRunner.InvokeAsync(
            "status", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--json");
        status.Out.ShouldContain("\"remaining\": 2");
        status.Out.ShouldContain("\"stale\": 0");
    }
}