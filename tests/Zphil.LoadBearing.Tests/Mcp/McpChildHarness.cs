using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The real-child MCP plumbing: the shipped <c>loadbearing.dll</c> started as a child process and
///     spoken to over real stdio with the client's stdin pipe held open — exactly what an MCP client does,
///     and the one condition the in-process MCP suites here cannot reproduce.
/// </summary>
/// <remarks>
///     <para>
///         Hand-written JSON-RPC frames rather than an SDK client, because for these suites the pipe
///         <em>is</em> part of the condition under test: stdin is written, flushed, and deliberately never
///         closed until the assertions are done.
///     </para>
///     <para>
///         Every read races a wall clock rather than cancelling — on Windows these streams are synchronous
///         underneath, so a token cannot interrupt a read already in flight. A wedged child therefore shows
///         up as a response that never arrived, which a caller turns into a named failure, rather than as an
///         indefinite hang.
///     </para>
/// </remarks>
internal static class McpChildHarness
{
    /// <summary>The client half of the MCP handshake.</summary>
    internal const string InitializeRequest =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"loadbearing-stdio-regression","version":"1.0.0"}}}""";

    /// <summary>The notification that completes it, after which tool calls are legal.</summary>
    internal const string InitializedNotification =
        """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     A start-info for <c>dotnet &lt;cliDllPath&gt; mcp &lt;solutionPath&gt; --spec &lt;specDllPath&gt;</c>
    ///     with all three streams redirected in UTF-8 and no BOM on stdin.
    /// </summary>
    /// <remarks>
    ///     The working directory is a parameter rather than a default because it is a subject in its own
    ///     right: a process holds an OS handle on its own working directory for as long as it lives, so a
    ///     test asserting on where the server holds handles has to choose it deliberately. The environment is
    ///     the same deployment-normal one the out-of-process replay smoke test uses — the test host's
    ///     MSBuild/VS registration stripped, so the child discovers MSBuild through its own vswhere probe.
    /// </remarks>
    internal static ProcessStartInfo ServerStartInfo(
        string cliDllPath,
        string solutionPath,
        string specDllPath,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(cliDllPath);
        startInfo.ArgumentList.Add("mcp");
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add("--spec");
        startInfo.ArgumentList.Add(specDllPath);

        DotnetCli.ApplyCleanSdkEnvironment(startInfo);

        return startInfo;
    }

    /// <summary>
    ///     The CLI build output beside the test assembly — the tests project references the CLI project, so
    ///     its output lands there. This is the in-repo server: a child launched from it maps its images out
    ///     of the repository's build tree.
    /// </summary>
    internal static string TestsBinCliDll()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "loadbearing.dll");
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"The CLI build output 'loadbearing.dll' was not found beside the test assembly at '{path}'.");

        return path;
    }

    /// <summary>Writes one newline-delimited frame to the child's stdin and flushes, leaving the pipe open.</summary>
    internal static async Task SendAsync(Process server, string frame)
    {
        await server.StandardInput.WriteAsync(frame + "\n");
        await server.StandardInput.FlushAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Reads newline-delimited frames until the response carrying <paramref name="id" /> arrives, or the
    ///     budget runs out. Null means "never arrived" — a wedge, an EOF, or a crash — which is what the
    ///     caller's assertions turn into a named failure.
    /// </summary>
    internal static async Task<string?> ReadResponseAsync(StreamReader stdout, int id, TimeSpan budget)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            TimeSpan remaining = budget - Stopwatch.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero) return null;

            string? line = await ReadLineWithinAsync(stdout, remaining);
            if (line is null) return null;
            if (line.Length > 0 && ResponseId(line) == id) return line;
        }
    }

    /// <summary>The numeric <c>id</c> of a JSON-RPC frame, or null for a notification or a stray line.</summary>
    internal static int? ResponseId(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("id", out JsonElement id)
                   && id.ValueKind == JsonValueKind.Number
                ? id.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            // Not a JSON-RPC frame — a stray diagnostic line. Keep reading.
            return null;
        }
    }

    /// <summary>
    ///     Awaits a stderr drain started at launch, within its own budget. Diagnostics only: a drain that
    ///     fails or overruns must not replace the caller's assertion failure with its own.
    /// </summary>
    internal static async Task<string> DrainAsync(Task<string> drain)
    {
        try
        {
            Task first = await Task.WhenAny(drain, Task.Delay(DrainBudget, TestContext.Current.CancellationToken));
            return first == drain ? await drain : "(stderr did not drain)";
        }
        catch (Exception ex)
        {
            return $"(stderr drain failed: {ex.GetType().Name})";
        }
    }

    /// <summary>
    ///     Races a single line read against a wall clock rather than cancelling it: on Windows these streams
    ///     are synchronous underneath, so a token cannot interrupt a read already in flight — the caller's
    ///     kill is what ends it. Null means the budget won.
    /// </summary>
    internal static async Task<string?> ReadLineWithinAsync(StreamReader stdout, TimeSpan budget)
    {
        var pending = stdout.ReadLineAsync();
        Task first = await Task.WhenAny(pending, Task.Delay(budget, TestContext.Current.CancellationToken));
        return first == pending ? await pending : null;
    }

    /// <summary>Kills the child and everything it started, tolerating a process that has already gone.</summary>
    internal static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Already gone — nothing left to clean up.
        }
    }

    /// <summary>
    ///     Copies <paramref name="sourceDirectory" /> to a fresh <paramref name="targetDirectory" />, so a
    ///     server can be launched from a location that is not the build tree it was built into — the same
    ///     move the production copy-launcher makes.
    /// </summary>
    internal static void StageDirectory(string sourceDirectory, string targetDirectory)
    {
        if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, true);
        CopyRecursive(sourceDirectory, targetDirectory);
    }

    private static void CopyRecursive(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)));

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory))
            CopyRecursive(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)));
    }
}
