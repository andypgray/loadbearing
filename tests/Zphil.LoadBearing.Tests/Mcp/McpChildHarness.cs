using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
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

    /// <summary>Budget for the handshake: a cold child, MSBuild registration, and the vswhere probe.</summary>
    internal static readonly TimeSpan HandshakeBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Budget for one tool call: a cold workspace load plus extraction plus whatever the tool shells out
    ///     to. A caller whose tool refuses before any workspace opens may pass a shorter one and say so.
    /// </summary>
    internal static readonly TimeSpan CallBudget = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     A start-info for <c>dotnet &lt;cliDllPath&gt; mcp &lt;solutionPath&gt; --spec &lt;specDllPath&gt;</c>,
    ///     over the bare-<c>mcp</c> overload below.
    /// </summary>
    internal static ProcessStartInfo ServerStartInfo(
        string cliDllPath,
        string solutionPath,
        string specDllPath,
        string workingDirectory)
    {
        ProcessStartInfo startInfo = ServerStartInfo(cliDllPath, workingDirectory);
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add("--spec");
        startInfo.ArgumentList.Add(specDllPath);

        return startInfo;
    }

    /// <summary>
    ///     A start-info for the bare <c>dotnet &lt;cliDllPath&gt; mcp</c> — no solution, no <c>--spec</c>:
    ///     the launch shape <c>.mcp/server.json</c> prescribes, where discovery happens inside the server
    ///     against <paramref name="workingDirectory" />. All three streams are redirected in UTF-8 with no
    ///     BOM on stdin.
    /// </summary>
    /// <remarks>
    ///     The working directory is a parameter rather than a default because it is a subject in its own
    ///     right: a process holds an OS handle on its own working directory for as long as it lives, and it
    ///     is also what the solution walk-up starts from, so a test asserting on either has to choose it
    ///     deliberately. The environment is the same deployment-normal one the out-of-process replay smoke
    ///     test uses — the test host's MSBuild/VS registration stripped, so the child discovers MSBuild
    ///     through its own vswhere probe — plus <see cref="LoadBearingEnvVars.SolutionPath" /> removed,
    ///     because it beats the walk-up: a developer machine that happens to have it set would silently bind
    ///     a server a test meant to leave unbound.
    /// </remarks>
    internal static ProcessStartInfo ServerStartInfo(string cliDllPath, string workingDirectory)
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

        DotnetCli.ApplyCleanSdkEnvironment(startInfo);
        startInfo.Environment.Remove(LoadBearingEnvVars.SolutionPath);

        return startInfo;
    }

    /// <summary>
    ///     Runs one whole client conversation against a child started from <paramref name="startInfo" />:
    ///     launch, drain stderr, handshake, each of <paramref name="calls" /> in order, kill the tree, drain.
    ///     Returns what came back rather than asserting on it, because every caller's "it never arrived"
    ///     message is site-specific and is the most useful thing that suite prints.
    /// </summary>
    /// <param name="startInfo">The child to launch — one of the <c>ServerStartInfo</c> overloads above.</param>
    /// <param name="calls">
    ///     The <c>tools/call</c> frames to send once the handshake is complete, in order: each with the
    ///     JSON-RPC id its response carries and the name its answer is filed under. May be empty, for a
    ///     caller whose subject is the handshake itself.
    /// </param>
    /// <param name="handshakeBudget">Overrides <see cref="HandshakeBudget" />.</param>
    /// <param name="callBudget">
    ///     Overrides <see cref="CallBudget" /> for every call. Worth passing, named, when the tool under test
    ///     refuses before it opens anything and does not need the cold-workspace allowance.
    /// </param>
    /// <param name="whileComplete">
    ///     Run against the live child, after the last response and before the kill, and only when the
    ///     conversation completed — the handshake answered, every call answered, the process still up. That
    ///     gate is what keeps a wedge reported as a wedge by the caller's own assertions rather than as
    ///     whatever this hook saw in a half-built state. It is here at all because a process footprint can
    ///     only be read from a process that still exists.
    /// </param>
    internal static async Task<ChildConversation> ConverseAsync(
        ProcessStartInfo startInfo,
        IReadOnlyList<(string Name, int Id, string Frame)> calls,
        TimeSpan? handshakeBudget = null,
        TimeSpan? callBudget = null,
        Action<Process>? whileComplete = null)
    {
        string? handshake = null;
        var answers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var stillAlive = false;
        string diagnostics;

        using (Process server = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Failed to start the MCP server child."))
        {
            // Drained from the start so a chatty child cannot fill its stderr pipe and stall.
            var errorDrain = server.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try
            {
                await SendAsync(server, InitializeRequest);
                handshake = await ReadResponseAsync(server.StandardOutput, id: 1, handshakeBudget ?? HandshakeBudget);

                if (handshake is not null)
                {
                    await SendAsync(server, InitializedNotification);
                    foreach ((string name, int id, string frame) in calls)
                    {
                        await SendAsync(server, frame);
                        answers[name] = await ReadResponseAsync(server.StandardOutput, id, callBudget ?? CallBudget);
                    }
                }

                stillAlive = !server.HasExited;

                if (stillAlive && handshake is not null && answers.Values.All(answer => answer is not null))
                    whileComplete?.Invoke(server);
            }
            catch (IOException)
            {
                // The child died mid-conversation and the pipe broke; the null results say so, with its
                // stderr attached — a better failure than an IOException stack.
            }
            finally
            {
                // stdin stays open until here: an open client pipe is what a real client holds, and for the
                // stdio suite it is the condition under test. The drain is inside the finally so it runs even
                // when whileComplete throws, and while the process object is still alive, so disposal cannot
                // fault the read out from under it.
                TryKillTree(server);
                diagnostics = await DrainAsync(errorDrain);
            }
        }

        return new ChildConversation(handshake, answers, stillAlive, diagnostics);
    }

    /// <summary>
    ///     The text payload of a <c>tools/call</c> response. A JSON-RPC error always fails — the tool never
    ///     ran — but a tool-level <c>isError</c> does not, because for some callers a refusal <em>is</em> the
    ///     subject and for others a rule the fixture spec cannot answer is legitimate. Read that flag
    ///     separately with <see cref="IsToolError" />.
    /// </summary>
    internal static string ShouldHaveToolText(string frame, string toolName)
    {
        using JsonDocument document = JsonDocument.Parse(frame);
        document.RootElement.TryGetProperty("error", out JsonElement error)
            .ShouldBeFalse($"{toolName} returned a JSON-RPC error rather than a tool result: {error}");

        return document.RootElement.GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    /// <summary>Whether a <c>tools/call</c> response carries a tool-level <c>isError</c>.</summary>
    internal static bool IsToolError(string frame)
    {
        using JsonDocument document = JsonDocument.Parse(frame);
        return document.RootElement.GetProperty("result")
                   .TryGetProperty("isError", out JsonElement flag)
               && flag.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    ///     The <c>instructions</c> an <c>initialize</c> response carries — one of the two in-band channels a
    ///     client actually reads, the other being a tool call's error result.
    /// </summary>
    internal static string ShouldHaveInstructions(string handshake)
    {
        using JsonDocument document = JsonDocument.Parse(handshake);
        document.RootElement.TryGetProperty("error", out JsonElement error)
            .ShouldBeFalse($"the server returned a JSON-RPC error to initialize: {error}");

        return document.RootElement.GetProperty("result")
            .GetProperty("instructions")
            .GetString() ?? string.Empty;
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
        // File.Copy carries the read-only attribute across, so a staged tree can only be re-staged by a
        // delete that clears it.
        ReadOnlyTolerant.DeleteTree(targetDirectory);
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

/// <summary>
///     What one <see cref="McpChildHarness.ConverseAsync" /> conversation came back with: the raw
///     <c>initialize</c> frame, each call's raw response by name, whether the child was still up when the
///     conversation finished, and its stderr. A null frame means "never arrived" — a wedge, an EOF, or a
///     crash — which the caller turns into a named failure with the diagnostics attached.
/// </summary>
internal sealed record ChildConversation(
    string? Handshake,
    IReadOnlyDictionary<string, string?> Answers,
    bool StillAlive,
    string Diagnostics);
