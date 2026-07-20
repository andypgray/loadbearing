using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Tests.Replay;

/// <summary>
///     The hermetic corners of <see cref="BinlogReplayer" /> — the ones that never reach MSBuild: the
///     blank-path guard that throws on the first line before any reader is opened, and
///     <see cref="BinlogReplayer.EnsureAssemblyExtension" />'s OutputKind → assembly-extension mapping (a pure
///     string function, exposed <c>internal</c> for this test). The full replay path is exercised end-to-end by
///     the Serial <c>BinlogReplayFidelityTests</c>; these need no binlog fixture, so they are not Serial.
/// </summary>
public sealed class BinlogReplayerUnitTests
{
    [Fact]
    public void Replay_BlankPath_ThrowsArgumentExceptionBeforeAnyBuild()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace is the very first statement — no SolutionReader, no MSBuild.
        Should.Throw<ArgumentException>(() => BinlogReplayer.Replay(""));
    }

    [Theory]
    // No assembly extension yet: the OutputKind decides which one is appended.
    [InlineData("MyApp.Domain", OutputKind.DynamicallyLinkedLibrary, "MyApp.Domain.dll")]
    [InlineData("MyApp.Domain", null, "MyApp.Domain.dll")]
    [InlineData("MyApp.Cli", OutputKind.ConsoleApplication, "MyApp.Cli.exe")]
    [InlineData("MyApp.WinForms", OutputKind.WindowsApplication, "MyApp.WinForms.exe")]
    [InlineData("MyApp.WinRt", OutputKind.WindowsRuntimeApplication, "MyApp.WinRt.exe")]
    [InlineData("MyApp.Mod", OutputKind.NetModule, "MyApp.Mod.netmodule")]
    [InlineData("MyApp.Component", OutputKind.WindowsRuntimeMetadata, "MyApp.Component.winmdobj")]
    // A dotted assembly name is the whole reason the guard tests known extensions explicitly instead of using
    // Path.GetExtension: ".Billing" is not an assembly extension, so the real one is still appended.
    [InlineData("MyApp.Legacy.Billing", OutputKind.DynamicallyLinkedLibrary, "MyApp.Legacy.Billing.dll")]
    // Already carries a known assembly extension (case-insensitively): early return, the OutputKind is ignored.
    [InlineData("MyApp.Domain.dll", OutputKind.ConsoleApplication, "MyApp.Domain.dll")]
    [InlineData("tool.EXE", OutputKind.DynamicallyLinkedLibrary, "tool.EXE")]
    public void EnsureAssemblyExtension_AppendsKindExtension_UnlessOneIsAlreadyPresent(
        string outputPath, OutputKind? outputKind, string expected)
    {
        BinlogReplayer.EnsureAssemblyExtension(outputPath, outputKind).ShouldBe(expected);
    }
}