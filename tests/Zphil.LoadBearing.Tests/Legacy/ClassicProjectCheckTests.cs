using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.Cli;

namespace Zphil.LoadBearing.Tests.Legacy;

/// <summary>
///     <c>check</c> end to end against the ClassicApp fixture: a non-SDK-style .NET Framework project in
///     the 2003 MSBuild XML namespace, driven by a net48 spec whose anchors are all patterns. The point is
///     <c>file:line</c> on a project shape that predates the SDK — the verdicts below name lines in a
///     <c>Classic.Billing</c> source file, from a tree nothing built first.
/// </summary>
/// <remarks>
///     Windows-gated: MSBuildWorkspace loads a project like this through the .NET Framework build host,
///     which needs Visual Studio or Build Tools MSBuild on the machine. In the "Serial" collection because
///     it opens a real workspace.
/// </remarks>
[Collection("Serial")]
public sealed class ClassicProjectCheckTests
{
    [Fact]
    public async Task Check_NonSdkFrameworkSolution_ExitsOneWithTheDesignedVerdictsAndFileLine()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "A non-SDK-style .NET Framework project loads through the net472 build host, which needs "
            + "Windows with Visual Studio or Build Tools MSBuild.");

        // Act: --no-cache so the run is a real workspace load every time, never a fragment-cache hit.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.ClassicAppSolution, "--spec", CliRunner.ClassicAppSpecDll, "--no-cache");

        // Assert
        result.Exit.ShouldBe(1);

        // The naming rule, red at the unprefixed interface's own line.
        result.Out.ShouldContain("FAIL naming/interfaces — Interfaces in `Classic.*` must be named `I*`.");
        result.Out.ShouldContain("Classic.Billing/IBillingGateway.cs:9 — Classic.Billing.BillingSink");

        // The namespace-target rule, red at every inline ADO.NET site. A namespace target needs no
        // assembly load, which is why it works from a spec that cannot see System.Data at all.
        result.Out.ShouldContain(
            "FAIL data-access/no-inline-sql — Types in `Classic.*` must not reference types in `System.Data.*`.");
        result.Out.ShouldContain(
            "Classic.Billing/BillingCalculator.cs:10 — Classic.Billing.BillingCalculator references System.Data.SqlClient.SqlConnection");
        result.Out.ShouldContain(
            "Classic.Billing/BillingCalculator.cs:13 — Classic.Billing.BillingCalculator references System.Data.SqlClient.SqlCommand");

        // The green rule, so the run is a verdict rather than a blanket failure.
        result.Out.ShouldContain(
            "pass billing/no-direct-calculator — Types in `Classic.*`, except types whose name matches "
            + "`BillingGateway` must not reference types whose name matches `BillingCalculator`.");
        result.Out.ShouldContain("Checked 3 rules: 1 passed, 2 failed, 0 skipped (4 violations, 0 warnings).");
    }

    [Fact]
    public void BuildHostNet472_IsDeployedBesideTheCliOutput()
    {
        // The .NET Framework build host ships as content in Microsoft.CodeAnalysis.Workspaces.MSBuild and
        // lands beside the CLI, so a deployed tool has it without any extra install step. What this asserts
        // is deployment only: that the net472 host — rather than the .NET one — is the process that loads a
        // non-SDK project was established by watching the process tree during a run, not by this test.
        string buildHost = Path.Combine(
            AppContext.BaseDirectory,
            "BuildHost-net472",
            "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.exe");

        File.Exists(buildHost).ShouldBeTrue(
            $"The net472 build host was not deployed beside the CLI output at '{buildHost}'.");
    }
}