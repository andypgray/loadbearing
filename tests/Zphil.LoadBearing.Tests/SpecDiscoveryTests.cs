using Shouldly;
using Xunit;
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
        var names = SpecDiscovery.FindSpecs(typeof(ArchSpec).Assembly)
            .Select(spec => spec.GetType().FullName)
            .ToList();

        names.ShouldBe(names.OrderBy(name => name, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void FindSpecs_DiscoversPublicCanonicalSpec()
    {
        SpecDiscovery.FindSpecs(typeof(ArchSpec).Assembly).ShouldContain(spec => spec is ArchSpec);
    }

    [Fact]
    public void FindSpecs_PublicSpecNestedInPublicType_IsDiscovered()
    {
        // Type.IsPublic is false for ANY nested type, so the old predicate silently skipped a spec nested
        // in a public class; Type.IsVisible is true when the whole containing chain is public (L3). This
        // spec — public, nested through a public chain — is the pin that the switch to IsVisible restores.
        SpecDiscovery.FindSpecs(typeof(PublicOuter.NestedSpec).Assembly)
            .ShouldContain(spec => spec is PublicOuter.NestedSpec);
    }

    [Fact]
    public void FindSpecs_NoSpecsInAssembly_ThrowsLoudly()
    {
        // The Core assembly declares no IArchitectureSpec — discovery must be loud, not silent.
        Should.Throw<SpecDiscoveryException>(() => SpecDiscovery.FindSpecs(typeof(Arch).Assembly));
    }

    public static class PublicOuter
    {
        public sealed class NestedSpec : IArchitectureSpec
        {
            public void Define(Arch arch)
            {
                arch.Rule("discovery/nested-visible").Enforce(arch.Types.MustHavePrefix("I"))
                    .Because("Public specs nested in public types are discoverable via Type.IsVisible.");
            }
        }
    }
}