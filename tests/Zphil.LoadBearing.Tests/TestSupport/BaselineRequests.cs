using Zphil.LoadBearing.Cli.Verbs;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     <see cref="BaselineRequest" />s for the runner seams that take a report rather than load a workspace
///     (<see cref="BaselineRunner.AddEntry" />, <see cref="BaselineRunner.ApplyFiles" />): no solution, no
///     spec, the cache on and workspace diagnostics not allowed, because none of those reach the seam. One
///     factory per mode, so a row names the verb it is about instead of spelling five booleans in a row.
/// </summary>
internal static class BaselineRequests
{
    /// <summary><c>--add</c> of one edge entry, resolved against <paramref name="workingDirectory" />.</summary>
    internal static BaselineRequest Add(string ruleId, string because, string source, string target, string workingDirectory)
    {
        return Request(init: false, acceptReductions: false, add: true, ruleId, because, source, target, workingDirectory);
    }

    /// <summary><c>--init</c> over every ratcheted rule.</summary>
    internal static BaselineRequest Init(string workingDirectory)
    {
        return Request(init: true, acceptReductions: false, add: false, null, null, null, null, workingDirectory);
    }

    /// <summary><c>--accept-reductions</c> over every ratcheted rule.</summary>
    internal static BaselineRequest AcceptReductions(string workingDirectory)
    {
        return Request(init: false, acceptReductions: true, add: false, null, null, null, null, workingDirectory);
    }

    private static BaselineRequest Request(
        bool init, bool acceptReductions, bool add,
        string? ruleId, string? because, string? source, string? target, string workingDirectory)
    {
        return new BaselineRequest(
            null, null, init, acceptReductions, add, ruleId, because, source, target, null, workingDirectory, false, false);
    }
}
