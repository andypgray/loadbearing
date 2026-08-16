using System.Reflection;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The shared-framework spec-load failure end to end: a spec whose <c>typeof()</c> anchor names an
///     ASP.NET Core type, driven through the real CLI, refused with exit 2 on stderr — and handed the
///     string overload that makes the same rule work.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ModelPipelineSpecLoadFailureTests" /> drives this fixture as far as
///         <c>ModelPipeline.LoadModel</c> and asserts the thrown <c>UserErrorException</c>; nothing until
///         now carried it the last step, so the exit code and the channel a user actually reads were
///         asserted only generically. A diagnostic that never reaches stderr is not a diagnostic.
///     </para>
///     <para>
///         The <c>explain</c> DLL fast path short-circuits ahead of the workspace, so no solution is
///         loaded and the call is sub-second — the same shape <see cref="SpecContractSkewE2ETests" /> uses
///         for its refusals. The rule ID is the fixture's own, though nothing reaches a rule lookup:
///         <c>Define()</c> throws on the first anchor.
///     </para>
/// </remarks>
public sealed class SharedFrameworkSpecE2ETests
{
    private const string RuleId = "shared-framework/controllers-suffixed";

    [Fact]
    public async Task Explain_OverASharedFrameworkSpec_RefusesWithTheFrameworkNamedAndTheStringHatchOffered()
    {
        // Arrange: guard the precondition rather than leave a bare "expected exit 2" — on a host that
        // happened to carry ASP.NET Core the anchor would resolve and this fixture would prove nothing.
        Should.Throw<FileNotFoundException>(
            () => Assembly.Load(new AssemblyName("Microsoft.AspNetCore.Mvc.Core")),
            "This test host can load Microsoft.AspNetCore.Mvc.Core itself, so the fixture's anchor would "
            + "resolve through the default context and could not reproduce the failure it exists for.");

        // Act
        CliResult result = await CliRunner.InvokeAsync("explain", RuleId, "--spec", CliRunner.SharedFrameworkSpecDll);

        // Assert: the world named, the packaging remedy ruled out, and the escape hatch spelled on the very
        // type that failed — the three things the field recovery (deleting the rules) was missing.
        result.ShouldRefuseWith(
            "The spec assembly 'Zphil.LoadBearing.SharedFrameworkSpec' failed to load its dependency",
            "'Microsoft.AspNetCore.Mvc.Core,",
            "a .NET shared framework pulled in by <FrameworkReference>",
            "CopyLocalLockFileAssemblies has nothing to copy",
            ".DerivedFrom(\"Microsoft.AspNetCore.Mvc.ControllerBase\") renders identically to the typeof() form");
    }
}
