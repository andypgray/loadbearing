using System.Reflection;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Discovery;

/// <summary>
///     Reflection-only spec discovery (netstandard2.0-safe, so it runs in the Core). Finds public,
///     non-abstract <see cref="IArchitectureSpec" /> classes, ordered deterministically by
///     <see cref="Type.FullName" /> ordinal (GRAMMAR §9 — law must load predictably), and
///     instantiates each. A zero result is loud (<see cref="SpecDiscoveryException" />). The
///     <c>AssemblyLoadContext</c> that isolates a prebuilt spec DLL is a host concern (the test
///     project today, the CLI in Phase 3), not the Core's.
/// </summary>
public static class SpecDiscovery
{
    /// <summary>Discovers and instantiates every public spec in the assembly, in deterministic order.</summary>
    public static IReadOnlyList<IArchitectureSpec> FindSpecs(Assembly assembly)
    {
        Guard.NotNull(assembly, nameof(assembly));

        var specTypes = assembly.GetTypes()
            .Where(type => type.IsClass
                           && !type.IsAbstract
                           && type.IsPublic
                           && typeof(IArchitectureSpec).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        if (specTypes.Count == 0)
            throw new SpecDiscoveryException(
                $"No public {nameof(IArchitectureSpec)} implementations found in assembly '{assembly.GetName().Name}'.");

        return specTypes.Select(type => (IArchitectureSpec)Activator.CreateInstance(type)!).ToList();
    }
}