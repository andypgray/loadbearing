using System.Text.Json;
using Shouldly;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The SARIF render of an in-memory <see cref="CheckReport" />, and the Shouldly surface over it: the
///     workspace-free <see cref="SarifReportRenderer.Serialize" /> seam, the one run's results and rule
///     catalog, and the exactly-one-error-saying-X claim a message row makes.
/// </summary>
/// <remarks>
///     <para>
///         Shared because every row driving the renderer directly makes the same five-argument call and
///         then walks the same <c>runs[0]</c>, whatever the row's subject is — the per-site mapping, or a
///         project violation's message text.
///     </para>
///     <para>
///         The readers sit beside the assertion rather than in a <c>SarifJson</c> of their own, as
///         <see cref="CheckJson" /> does for the check document: both exist only to reach the arrays this
///         file's assertions read, and neither is a wire format a consumer scripts against on its own.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />: leaving it off is what
///         makes an inner failure name the SARIF property under test — <c>level</c>, <c>message.text</c> —
///         rather than this file's own local.
///     </para>
/// </remarks>
internal static class SarifAssertions
{
    /// <summary>
    ///     <paramref name="report" /> rendered as SARIF over a healthy load: nothing on the diagnostics
    ///     stream and no incomplete-model evidence, so the invocation block is the one a clean run writes.
    ///     The populated shape is the gate suites' subject, and the golden's.
    /// </summary>
    /// <remarks>
    ///     No caller chooses the solution directory. An in-memory report's site paths are already relative
    ///     — a bare <c>Test.cs</c> from the MSBuild-free source factory, a bare <c>Internal.csproj</c> from
    ///     the project-facts one — so every directory relativizes them to themselves, and no row built on
    ///     this asserts on a resolved URI.
    /// </remarks>
    internal static string ToSarif(this CheckReport report)
    {
        return SarifReportRenderer.Serialize(
            report, Directory.GetCurrentDirectory(), true, [], WorkspaceDiagnostics.None);
    }

    /// <summary>
    ///     Asserts <paramref name="sarif" /> carries exactly one result, at <c>error</c> level, saying
    ///     <paramref name="message" /> — and hands it back for any further per-site read.
    /// </summary>
    internal static JsonElement ShouldBeOneErrorSaying(this string sarif, string message)
    {
        JsonElement result = sarif.SarifResults()
            .ShouldHaveSingleItem();
        result.GetProperty("level")
            .GetString()
            .ShouldBe("error");
        result.GetProperty("message")
            .GetProperty("text")
            .GetString()
            .ShouldBe(message);

        return result;
    }

    /// <summary>The run's results, in render order — one per violation site.</summary>
    internal static IReadOnlyList<JsonElement> SarifResults(this string sarif)
    {
        return Run(sarif)
            .GetProperty("results")
            .EnumerateArray()
            .ToList();
    }

    /// <summary>
    ///     The driver's reporting descriptors, in model order — every rule the spec declared, not just the
    ///     ones that produced a result.
    /// </summary>
    internal static IReadOnlyList<JsonElement> SarifRules(this string sarif)
    {
        return Run(sarif)
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules")
            .EnumerateArray()
            .ToList();
    }

    /// <summary>
    ///     The run's tool-execution notifications, in render order — the invocation channel a partial
    ///     survey warns on, this log format having neither an exit code nor prose to carry it.
    /// </summary>
    internal static IReadOnlyList<JsonElement> SarifNotifications(this string sarif)
    {
        return Run(sarif)
            .GetProperty("invocations")[0]
            .GetProperty("toolExecutionNotifications")
            .EnumerateArray()
            .ToList();
    }

    // The single run a LoadBearing SARIF log always carries.
    private static JsonElement Run(string sarif)
    {
        return JsonDocument.Parse(sarif)
            .RootElement.GetProperty("runs")[0];
    }
}
