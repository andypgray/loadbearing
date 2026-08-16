using System.Reflection;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Discovery;

/// <summary>
///     Reflection-only spec discovery: finds the publicly-visible, non-abstract
///     <see cref="IArchitectureSpec" /> classes in an assembly and instantiates each.
/// </summary>
/// <remarks>
///     netstandard2.0-safe, so it runs in the Core. Visibility is <see cref="Type.IsVisible" /> —
///     top-level public, or public nested through a public chain — not <see cref="Type.IsPublic" />,
///     which is false for any nested type. Order is <see cref="Type.FullName" /> ordinal, because law
///     must load predictably (GRAMMAR §9). The <c>AssemblyLoadContext</c> that isolates a prebuilt
///     spec DLL is a host concern, not the Core's.
/// </remarks>
public static class SpecDiscovery
{
    /// <summary>Discovers and instantiates every public spec in the assembly, in deterministic order.</summary>
    /// <exception cref="SpecDiscoveryException">The assembly declares no public spec.</exception>
    public static IReadOnlyList<IArchitectureSpec> FindSpecs(Assembly assembly)
    {
        Guard.NotNull(assembly, nameof(assembly));

        List<Type> specTypes = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsVisible: true }
                           && typeof(IArchitectureSpec).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        if (specTypes.Count == 0)
            throw new SpecDiscoveryException(
                $"No public {nameof(IArchitectureSpec)} implementations found in assembly '{assembly.GetName().Name}'.");

        return specTypes.Select(type => (IArchitectureSpec)Activator.CreateInstance(type)!).ToList();
    }
}
