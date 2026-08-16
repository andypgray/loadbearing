using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The xUnit adapter's two incomplete-model messages, pinned directly against a synthetic
///     <see cref="WorkspaceDiagnostics" /> rather than through a fixture solution.
/// </summary>
/// <remarks>
///     <para>
///         <c>AdapterTests</c> owns the adapter's <em>behaviour</em> — which test fails, which cases skip —
///         over the real <c>BrokenApp</c> bed that genuinely loads partially. There is no such bed for a
///         failed restore, and building one would cost a live SDK and a private package feed to prove
///         something these two methods already decide on their own: both are pure functions of the
///         diagnostics, and everything downstream of them is
///         <see cref="Zphil.LoadBearing.Tests.Cli.RestoreFailureSilentEdgeE2ETests" />' subject.
///     </para>
///     <para>
///         Both messages carry their evidence inline, unlike the four CLI verbs that point at warnings printed
///         above them: in a test report there is nothing above to point at.
///     </para>
/// </remarks>
public sealed class IncompleteModelGateAdapterMessagesTests
{
    private const string BrokenProject = "C:/repo/App.Broken/App.Broken.csproj";

    private const string UnrestoredProject = "C:/repo/App.Unrestored/App.Unrestored.csproj";

    [Fact]
    public void AdapterRefusal_RestoreFailure_NamesTheProjectTheStakesAndTheRestoreRemedy()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(Diagnostics(restoreFailed: [UnrestoredProject]));

        refusal.ShouldStartWith(
            "the model is incomplete — NuGet packages did not resolve for 1 project, so every rule test was "
            + "skipped:");
        refusal.ShouldContain(UnrestoredProject);
        refusal.ShouldContain(
            "Every rule about a package would be measured over a codebase where that package resolved to "
            + "nothing");
        refusal.ShouldContain("Restore the solution first (dotnet restore)");
        refusal.ShouldContain("override AllowWorkspaceDiagnostics to true on the test class.");
    }

    [Fact]
    public void AdapterRefusal_LoadFailureOnly_IsUnchanged()
    {
        // The byte-identity guard the whole two-block design rests on: a run with one cause reads exactly as
        // this class read before the second cause could gate, so no existing pin moves.
        string refusal = IncompleteModelGate.AdapterRefusal(Diagnostics(failed: [BrokenProject]));

        refusal.ShouldStartWith(
            "the model is incomplete — 1 project failed to load, so every rule test was skipped:");
        refusal.ShouldNotContain("NuGet packages did not resolve");
        refusal.ShouldContain("Restore and build the solution first (dotnet build)");
    }

    [Fact]
    public void AdapterRefusal_BothCauses_WritesTheLoadBlockThenTheRestoreBlock()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(
            Diagnostics([BrokenProject], [UnrestoredProject]));

        refusal.ShouldStartWith("the model is incomplete — 1 project failed to load");
        refusal.ShouldContain(BrokenProject);
        refusal.ShouldContain(UnrestoredProject);
        refusal.IndexOf("NuGet packages did not resolve for 1 project", StringComparison.Ordinal)
            .ShouldBeGreaterThan(refusal.IndexOf("1 project failed to load", StringComparison.Ordinal));
    }

    [Fact]
    public void AdapterOptedIn_RestoreFailure_SaysWhatTheVerdictsWereReachedDespite()
    {
        // Opting in restores the rule verdicts, but a test named Workspace_LoadedCompletely cannot pass while
        // the failure it is named for is real — so it skips, and the skip reason has to name which failure.
        string skip = IncompleteModelGate.AdapterOptedIn(Diagnostics(restoreFailed: [UnrestoredProject]));

        skip.ShouldStartWith(
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, "
            + "despite NuGet packages not resolving for 1 project:");
        skip.ShouldContain(UnrestoredProject);
    }

    [Fact]
    public void AdapterSkipReason_NamesBothCausesInTheOnePlaceTheProjectsCanBeRead()
    {
        // Deliberately constant and deliberately short: the projects themselves ride
        // Workspace_LoadedCompletely's failure, so this points there rather than repeating them once per rule.
        IncompleteModelGate.AdapterSkipReason.ShouldBe(
            "the workspace did not load completely, so no verdict was reached; see Workspace_LoadedCompletely "
            + "for the projects that failed to load or whose NuGet packages did not resolve.");
    }

    private static WorkspaceDiagnostics Diagnostics(
        IReadOnlyList<string>? failed = null, IReadOnlyList<string>? restoreFailed = null)
    {
        return new WorkspaceDiagnostics([], [], failed ?? [], [], restoreFailed ?? []);
    }
}
