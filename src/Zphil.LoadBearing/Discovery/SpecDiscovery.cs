using System.Reflection;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Discovery;

/// <summary>
///     Finds the <see cref="IArchitectureSpec" /> classes an assembly declares and creates one of each.
///     A class is found when it is a non-abstract class visible from outside the assembly: public, or
///     public nested inside a public chain. Each must also declare a public parameterless constructor,
///     since one of each is created here; a found class without one fails creation with a
///     <see cref="System.MissingMethodException" />. Reflection only: nothing in the assembly runs
///     until <c>Define</c> is called.
/// </summary>
// netstandard2.0-safe, so it runs in the Core. Visibility is Type.IsVisible — top-level public, or
// public nested through a public chain — not Type.IsPublic, which is false for any nested type.
// Order is Type.FullName ordinal, because a spec must load predictably (GRAMMAR §9). The
// AssemblyLoadContext that isolates a prebuilt spec DLL is a host concern, not the Core's.
public static class SpecDiscovery
{
    /// <summary>
    ///     Finds every visible <see cref="IArchitectureSpec" /> class in the assembly and creates one of
    ///     each, ordered by full type name so that the same assembly always yields the same order. Hand the
    ///     result to <c>ArchModelBuilder.Build</c>.
    /// </summary>
    /// <exception cref="SpecDiscoveryException">The assembly declares no such class.</exception>
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
