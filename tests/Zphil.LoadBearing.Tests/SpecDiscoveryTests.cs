using System.Reflection;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Discovery;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     In-assembly spec discovery (GRAMMAR §9): deterministic ordering by full name, and a loud
///     failure on zero specs rather than a silent skip.
/// </summary>
public class SpecDiscoveryTests
{
    [Fact]
    public void FindSpecs_OrdersByFullNameOrdinal()
    {
        List<string?> names = SpecDiscovery.FindSpecs(typeof(ArchSpec).Assembly)
            .Select(spec => spec.GetType()
                .FullName)
            .ToList();

        names.ShouldBe(names.OrderBy(name => name, StringComparer.Ordinal)
            .ToList());
    }

    [Fact]
    public void FindSpecs_DiscoversPublicCanonicalSpec()
    {
        SpecDiscovery.FindSpecs(typeof(ArchSpec).Assembly)
            .ShouldContain(spec => spec is ArchSpec);
    }

    [Fact]
    public void FindSpecs_PublicSpecNestedInPublicType_IsDiscovered()
    {
        // Type.IsPublic is false for ANY nested type, so a predicate built on it silently skips a spec
        // nested in a public class; Type.IsVisible is true when the whole containing chain is public. This
        // spec — public, nested through a public chain — is the pin under that choice.
        SpecDiscovery.FindSpecs(typeof(PublicOuter.NestedSpec).Assembly)
            .ShouldContain(spec => spec is PublicOuter.NestedSpec);
    }

    [Fact]
    public void FindSpecs_NoSpecsInAssembly_ThrowsLoudly()
    {
        // The Core assembly declares no IArchitectureSpec — discovery must be loud, not silent.
        Should.Throw<SpecDiscoveryException>(() => SpecDiscovery.FindSpecs(typeof(Arch).Assembly));
    }

    [Fact]
    public void FindSpecs_NeverReachesIntoReferencedAssemblies()
    {
        Assembly assembly = typeof(ArchSpec).Assembly;

        // This project references the repo's own arch-spec project, which declares a public
        // IArchitectureSpec — and a rule pack, whose whole point is contributing rules. Discovery stays
        // inside the assembly it was handed either way: nothing a spec references can inject a rule the
        // spec did not ask for, which is what keeps rule packs plain libraries rather than a
        // conventions layer.
        SpecDiscovery.FindSpecs(assembly)
            .ShouldAllBe(spec => spec.GetType()
                .Assembly == assembly);
        typeof(LoadBearingArchSpec).Assembly.ShouldNotBe(assembly);
    }

    public static class PublicOuter
    {
        public sealed class NestedSpec : IArchitectureSpec
        {
            public void Define(Arch arch)
            {
                arch.Rule("discovery/nested-visible")
                    .Enforce(arch.Types.MustHavePrefix("I"))
                    .Because("Public specs nested in public types are discoverable via Type.IsVisible.");
            }
        }
    }
}
