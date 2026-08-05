using System.Reflection;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     Maps "the spec calls contract API this host does not have" into an actionable
///     <see cref="UserErrorException" /> naming the missing member, both contract versions where they
///     differ, and the two remedies.
/// </summary>
/// <remarks>
///     This is the residual failure of the version-agnostic contract bind (see
///     <see cref="SpecLoadContext.Load" />), and the trade is deliberate. Binding on version failed every
///     spec built against a newer contract, including ones that would have run perfectly, and failed them
///     at load with a missing-file error naming a file that was present. Binding on the assembly loads
///     them all and defers the failure to the one case that genuinely cannot work — a spec reaching for a
///     member this copy does not define — where the exception arrives mid-<c>Define()</c> as a
///     <see cref="MissingMemberException" /> or <see cref="TypeLoadException" /> with the member named.
///     Both host surfaces render message-only, so the member name is folded into the text rather than
///     left on the inner exception where nobody sees it.
/// </remarks>
internal static class SpecContractMismatch
{
    /// <summary>
    ///     Renders the message, keeping <paramref name="inner" /> as the inner exception.
    ///     <paramref name="spec" /> supplies the contract version it was compiled against.
    /// </summary>
    internal static UserErrorException Map(Assembly spec, string specDllPath, Exception inner)
    {
        string name = Path.GetFileNameWithoutExtension(specDllPath);
        string detail = string.IsNullOrWhiteSpace(inner.Message) ? "  (the runtime named no member)" : "  " + inner.Message;
        string message =
            $"The spec assembly '{name}' calls LoadBearing API this tool's contract does not have{DescribeDelta(spec)}:\n"
            + detail
            + "\nThat is a spec built against a newer Zphil.LoadBearing package than the tool running it: the spec loads, and Define() runs until it reaches the member that is missing."
            + $"\nUpdate the tool to at least the spec's version (dotnet tool update -g Zphil.LoadBearing.Cli — this one is {ServerVersion.SemVer}), or reference the Zphil.LoadBearing package that matches this tool from the spec project and rebuild it.";
        return new UserErrorException(message, inner);
    }

    /// <summary>
    ///     The parenthetical naming both contract versions, or empty when they are identical — which is
    ///     the normal case, since the shipped contract identity is pinned and no longer tracks the package
    ///     version. A delta only shows for a spec built against a pre-pin package or a source-built
    ///     contract, and there it is the whole explanation, so it leads.
    /// </summary>
    private static string DescribeDelta(Assembly spec)
    {
        Version? host = typeof(IArchitectureSpec).Assembly.GetName().Version;
        Version? referenced = spec.GetReferencedAssemblies()
            .FirstOrDefault(a => a.Name == SpecLoadContext.ContractAssemblyName)?.Version;

        if (host is null || referenced is null || referenced.Equals(host)) return "";

        return $" (the spec was built against contract {referenced}, this tool carries {host})";
    }
}