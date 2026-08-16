using Zphil.LoadBearing.Packs.DotNet;

namespace Zphil.LoadBearing.Tests.Packs;

/// <summary>
///     A spec class whose only rule comes from the pack. Paired with
///     <see cref="LocalCollidingSpec" /> it collides on <c>di/no-buildserviceprovider</c>; paired with
///     <see cref="LocalDistinctSpec" /> it does not, and the two just compose.
/// </summary>
internal sealed class PackCallSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        DotNetGuidance.NoBuildServiceProvider(arch, arch.Types.InNamespace("Sample.*"), PackPosture.Enforce);
    }
}

/// <summary>
///     A spec class that hand-writes a rule the pack also offers. Taking a rule from a pack and writing
///     it locally is a duplicate ID, which is loud — the reason a pack needs no ID-prefix scheme.
/// </summary>
internal sealed class LocalCollidingSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("di/no-buildserviceprovider")
            .Enforce(arch.Types.InNamespace("Sample.*").MustHavePrefix("Sample"))
            .Because("A locally authored rule that happens to claim an ID the pack also declares.");
    }
}

/// <summary>A spec class whose rule ID nothing in the pack claims, so both simply land.</summary>
internal sealed class LocalDistinctSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("sample/local-only")
            .Enforce(arch.Types.InNamespace("Sample.*").MustHavePrefix("Sample"))
            .Because("A project-specific rule no shared pack could know about.");
    }
}
