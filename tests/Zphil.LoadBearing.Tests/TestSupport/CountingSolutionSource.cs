using Zphil.LoadBearing.Cli.Pipeline;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A <see cref="ColdSolutionSource" /> that counts how many workspaces a run acquired, so
///     "this run never opened MSBuild at all" is observable without any timing.
/// </summary>
/// <remarks>
///     Zero is the fact every persisted-cache suite is really after: a hit replays the recorded spec
///     resolution and merges stored fragments, so the whole of MSBuild — the design-time build, the
///     workspace, the solution load — is skipped, and an acquisition count is the only observable that says
///     so without measuring a clock. Cold underneath rather than warm: a pooled workspace would be acquired
///     once for a whole class and count nothing per run.
/// </remarks>
internal sealed class CountingSolutionSource : ISolutionSource
{
    private readonly ColdSolutionSource inner = new();

    /// <summary>How many times a run asked this source for a loaded solution.</summary>
    public int AcquireCount { get; private set; }

    public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        AcquireCount++;
        return inner.AcquireAsync(solutionPath, ct);
    }
}
