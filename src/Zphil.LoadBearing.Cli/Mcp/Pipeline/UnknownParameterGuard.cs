using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Rejects tool calls that carry a JSON argument key matching no declared parameter,
///     turning a silent drop into an actionable, self-correcting error.
/// </summary>
/// <remarks>
///     <para>
///         The MCP SDK marshals JSON-RPC arguments with <c>UnmappedMemberHandling = Skip</c>,
///         so a hallucinated key (the model guessing <c>file</c>/<c>ruleId</c> variants instead
///         of the real parameter) is dropped before the tool runs. The model never learns it
///         sent a typo — it sees only a downstream "missing required argument" error and
///         re-guesses. This guard inspects the raw argument keys ahead of binding and names both
///         the bad keys and the real parameter list so the next call self-corrects, mirroring the
///         forgiving-input policy of <see cref="EnumValidationConverterFactory" /> and
///         <see cref="StringArrayCoercerFactory" />.
///     </para>
///     <para>
///         The SDK <em>has</em> a dormant strict check, but it is gated on
///         <c>JsonUnmappedMemberHandling.Disallow</c> AND <c>!HasCustomParameterBinding</c>;
///         neither holds here (we register custom converters), so it never fires. Do NOT
///         "fix" this by flipping <c>UnmappedMemberHandling</c> — that path is unreachable for
///         our tools; this guard is the working equivalent.
///     </para>
///     <para>
///         Parameter names are read verbatim from <see cref="ParameterInfo.Name" />, the exact
///         source the SDK feeds into <c>AIJsonUtilities.CreateFunctionJsonSchema</c> (parameter
///         names are not snake-cased — only the tool method name is), so reflection here is
///         identical to the advertised schema, not an approximation. Context/service-bound
///         parameters (everything the SDK binds rather than reading from JSON) are excluded;
///         see <see cref="ToolAttributeDiscovery.IsJsonBoundParameter" />.
///     </para>
/// </remarks>
internal static class UnknownParameterGuard
{
    private static readonly FrozenDictionary<string, ToolParamInfo> ToolParameters = BuildMap();

    /// <summary>
    ///     Returns an error message if <paramref name="arguments" /> contains a key that
    ///     matches no declared parameter of <paramref name="toolName" /> (case-insensitive),
    ///     otherwise <see langword="null" />. Only key identity is inspected — values are
    ///     never read, so a present-but-null valid argument is fine.
    /// </summary>
    internal static string? Validate(string toolName, IDictionary<string, JsonElement>? arguments)
    {
        // Unknown tool name is the SDK's dispatch concern, not ours — never block it here.
        if (!ToolParameters.TryGetValue(toolName, out ToolParamInfo? info)) return null;

        if (arguments is null || arguments.Count == 0) return null;

        List<string>? unknown = null;
        foreach (string key in arguments.Keys)
            if (!info.Lookup.Contains(key))
                (unknown ??= []).Add(key);

        if (unknown is null) return null;

        string badKeys = string.Join(", ", unknown.Select(k => $"\"{k}\""));
        string validNames = string.Join(", ", info.OrderedNames);
        return $"Unknown parameter {badKeys} on \"{toolName}\". Valid: {validNames}.";
    }

    private static FrozenDictionary<string, ToolParamInfo> BuildMap()
    {
        Dictionary<string, ToolParamInfo> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (MethodInfo method in ToolAttributeDiscovery.GetToolMethods())
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is not { } toolName) continue;

            string[] orderedNames = method.GetParameters()
                .Where(ToolAttributeDiscovery.IsJsonBoundParameter)
                .Select(p => p.Name!)
                .ToArray();

            map[toolName] = new ToolParamInfo(
                orderedNames,
                orderedNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
        }

        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ToolParamInfo(string[] OrderedNames, FrozenSet<string> Lookup);
}
