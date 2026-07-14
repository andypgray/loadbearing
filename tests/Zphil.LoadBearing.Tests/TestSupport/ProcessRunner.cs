using System.Diagnostics;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Runs a child process, draining stdout and stderr <em>concurrently and cancellably</em> under a
///     hard timeout, then returns its exit code and captured output.
/// </summary>
/// <remarks>
///     Guards the deadlock the parallel suite hit. The naive pattern —
///     <c>var e = StandardError.ReadToEndAsync(); var o = StandardOutput.ReadToEnd(); WaitForExit();</c> —
///     blocks on a <em>synchronous, unbounded</em> <see cref="TextReader.ReadToEnd" /> that only returns at
///     stdout EOF. Under the full parallel run a child's stdout write-handle can be inherited by a
///     concurrently-spawned long-lived process (a Roslyn <c>BuildHost</c> from a workspace-loading test, an
///     MSBuild worker node from a sibling <c>dotnet restore</c>), so EOF never arrives and the whole run
///     hangs — the timeout on the later <c>WaitForExit</c> never fires because control never reaches it.
///     Here both streams are read with a <see cref="CancellationToken" />, so once the timeout elapses the
///     reads and the wait unblock, the process tree is killed, and the caller gets a
///     <see cref="TimeoutException" /> instead of an indefinite hang.
/// </remarks>
internal static class ProcessRunner
{
    /// <summary>The ceiling for any single child process — generous, so it only trips on a genuine wedge.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Starts <paramref name="startInfo" /> (forcing the stream redirection this helper depends on),
    ///     drains both streams under <paramref name="timeout" /> (or <see cref="DefaultTimeout" />), and
    ///     returns the result. Throws <see cref="TimeoutException" /> — after killing the process tree — if
    ///     the child does not complete in time.
    /// </summary>
    internal static ProcessResult Run(ProcessStartInfo startInfo, TimeSpan? timeout = null)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException(
                                    $"Failed to start '{startInfo.FileName}'.");
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);

        // Both streams drain on the thread pool under the token; the sync ReadToEnd that used to wedge here
        // is gone, and the token bounds every wait below so no leaked pipe handle can hang the caller.
        var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            process.WaitForExitAsync(cts.Token).GetAwaiter().GetResult();
            return new ProcessResult(
                process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            TryKillTree(process);
            string arguments = startInfo.ArgumentList.Count > 0
                ? string.Join(" ", startInfo.ArgumentList)
                : startInfo.Arguments;
            string command = $"{startInfo.FileName} {arguments}".Trim();
            throw new TimeoutException(
                $"'{command}' did not complete within {(timeout ?? DefaultTimeout).TotalSeconds:0}s — "
                + "the concurrent-spawn pipe deadlock this helper guards against.");
        }
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch
        {
            // Already gone or inaccessible — nothing left to clean up.
        }
    }

    /// <summary>A completed child process's exit code and fully-drained standard streams.</summary>
    internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}