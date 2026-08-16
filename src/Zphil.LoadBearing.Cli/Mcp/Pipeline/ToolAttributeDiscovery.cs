using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Reflects over the current assembly to discover MCP tool methods, and answers which of their
///     parameters the server binds from JSON — the one definition of "the tool surface" every part of the
///     pipeline reasons against.
/// </summary>
internal static class ToolAttributeDiscovery
{
    /// <summary>
    ///     Returns all public methods on <see cref="McpServerToolTypeAttribute" />-annotated classes.
    /// </summary>
    internal static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(ToolAttributeDiscovery).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
    }

    /// <summary>
    ///     True when a parameter is bound from JSON arguments (and thus part of the advertised
    ///     schema), false when the SDK binds it from request context or DI. In this server the only
    ///     excluded type in practice is <see cref="CancellationToken" /> (every tool) — services
    ///     arrive via primary constructors, not method parameters. The remaining types are excluded
    ///     defensively, mirroring the SDK's own augmentation set, so a future context-bound parameter
    ///     cannot become a false positive.
    /// </summary>
    internal static bool IsJsonBoundParameter(ParameterInfo p)
    {
        Type t = p.ParameterType;

        if (t == typeof(CancellationToken) || t == typeof(AIFunctionArguments)) return false;

        if (typeof(IServiceProvider).IsAssignableFrom(t) || typeof(McpServer).IsAssignableFrom(t)) return false;

        if (t.IsGenericType)
        {
            Type definition = t.GetGenericTypeDefinition();
            if (definition == typeof(RequestContext<>) || definition == typeof(IProgress<>)) return false;
        }

        if (p.GetCustomAttribute<FromKeyedServicesAttribute>() is not null) return false;

        return p.Name is not null;
    }
}
