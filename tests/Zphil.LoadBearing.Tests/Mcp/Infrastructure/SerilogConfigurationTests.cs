using Microsoft.Extensions.Logging;
using Serilog.Events;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Roslyn.Hosting;

namespace Zphil.LoadBearing.Tests.Mcp.Infrastructure;

/// <summary>
///     The <c>LOADBEARING_LOG_LEVEL</c> parse (<see cref="SerilogConfiguration.ParseLogLevel" />) and the
///     log location it writes to. Both halves of the level vocabulary are accepted — Microsoft's
///     <see cref="LogLevel" /> names and Serilog's <see cref="LogEventLevel" /> names — because an
///     operator setting this variable has no way to know which library is underneath, and every
///     unrecognised value has to land on a working default rather than failing a server that is starting
///     up to serve a tool call.
/// </summary>
/// <remarks>
///     Deliberately does not exercise <c>InitializeFileLogger</c> or <c>RegisterCrashHandlers</c>: they
///     mutate process-global state (the static <c>Log.Logger</c>, <c>AppDomain</c> and
///     <c>TaskScheduler</c> handlers) that would outlive the test and follow the rest of the suite.
/// </remarks>
public sealed class SerilogConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseLogLevel_NullOrBlank_FallsBackToWarning(string? envValue)
    {
        SerilogConfiguration.ParseLogLevel(envValue)
            .ShouldBe(LogEventLevel.Warning);
    }

    [Theory]
    [InlineData("Trace", LogEventLevel.Verbose)]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("Information", LogEventLevel.Information)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("Critical", LogEventLevel.Fatal)]
    public void ParseLogLevel_MicrosoftLevelName_MapsToItsSerilogEquivalent(string envValue, LogEventLevel expected)
    {
        SerilogConfiguration.ParseLogLevel(envValue)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("Verbose", LogEventLevel.Verbose)]
    [InlineData("Fatal", LogEventLevel.Fatal)]
    public void ParseLogLevel_SerilogOnlyLevelName_IsAcceptedDirectly(string envValue, LogEventLevel expected)
    {
        // Verbose and Fatal have no Microsoft spelling, so they can only come through the second parse.
        SerilogConfiguration.ParseLogLevel(envValue)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("warning")]
    [InlineData("WARNING")]
    [InlineData("wArNiNg")]
    public void ParseLogLevel_IsCaseInsensitive(string envValue)
    {
        SerilogConfiguration.ParseLogLevel(envValue)
            .ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void ParseLogLevel_None_SilencesLogging()
    {
        // Microsoft's LogLevel.None has no Serilog level; it converts to the off sentinel above Fatal.
        SerilogConfiguration.ParseLogLevel("None")
            .ShouldBe(LevelAlias.Off);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("Loud")]
    [InlineData("Warn")]
    public void ParseLogLevel_UnrecognisedOrOutOfRange_FallsBackToWarning(string envValue)
    {
        // Enum.TryParse binds any numeric string to an enum value, defined or not — "99" would otherwise
        // become a level nothing is ever logged at, silencing the log file without saying so.
        SerilogConfiguration.ParseLogLevel(envValue)
            .ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void LogLevelVariable_IsThePrefixedName()
    {
        LoadBearingEnvVars.LogLevel.ShouldBe("LOADBEARING_LOG_LEVEL");
    }

    [Fact]
    public void LogDirectory_IsUnderTheLocalApplicationDataFolder()
    {
        // The path docs and troubleshooting instructions send a reader to; a silent move strands them.
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zphil.LoadBearing",
            "logs");

        SerilogConfiguration.LogDirectory.ShouldBe(expected);
        Path.IsPathRooted(SerilogConfiguration.LogDirectory)
            .ShouldBeTrue();
    }
}
