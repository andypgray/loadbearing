namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     Wraps <see cref="ColdSolutionSource" /> to print a one-line <c>warning: {notice}</c> the first — and
///     only — time a workspace is actually acquired. Used when a persisted capture is
///     <em>invalid</em> (stale or unreadable): the run falls back to a design-time build, and the notice must
///     land exactly once, at acquisition time rather than at startup, so a fragment-cache hit — which acquires
///     no workspace — prints nothing and stays byte-identical to a plain cached run. The runner acquires at
///     most once, so the guard flag is belt-and-suspenders.
/// </summary>
internal sealed class NoticingSolutionSource(string notice, TextWriter error, ISolutionSource inner) : ISolutionSource
{
    private bool _noticed;

    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
    {
        if (!_noticed)
        {
            error.WriteLine($"warning: {notice}");
            _noticed = true;
        }

        return inner.AcquireAsync(solution, workingDirectory, ct);
    }
}