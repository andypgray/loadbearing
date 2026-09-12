using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.Verbs;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The plain <c>check</c> run every <see cref="CheckRunner" /> row starts from: the human report at full
///     grain, over the cache, with no diff base, no binlog, no SARIF, no rule filter and workspace
///     diagnostics not allowed. A row that cares about one of those says so with a <c>with</c> clause
///     instead of respelling twelve positional arguments, four of them a bare <c>false</c> and four a bare
///     <c>null</c>.
/// </summary>
/// <remarks>
///     The twin of <see cref="BaselineRequests" />, and for its reason: what a row is about should be the
///     only thing it spells.
/// </remarks>
internal static class CheckRequests
{
    /// <summary><c>check</c> of <paramref name="solution" /> against <paramref name="spec" />.</summary>
    internal static CheckRequest For(string solution, string? spec, string workingDirectory)
    {
        return new CheckRequest(
            solution, spec, Json: false, HookJson: false, DiffBase: null, workingDirectory, NoCache: false,
            Binlog: null, AllowWorkspaceDiagnostics: false, Sarif: null, Rules: null, DocumentGrain.Full);
    }
}
