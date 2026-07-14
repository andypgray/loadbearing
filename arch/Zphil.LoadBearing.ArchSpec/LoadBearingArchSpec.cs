namespace Zphil.LoadBearing.ArchSpec;

/// <summary>
///     LoadBearing's own architecture spec — the dogfood render source. One real guard: Core (the
///     netstandard2.0 reified model both render targets consume) must not reference the Roslyn
///     extraction project (host machinery). Nothing in the build system prevents adding that
///     ProjectReference, so this is a genuine boundary; it passes today, and both selections resolve
///     non-empty (no inert-rule warning).
/// </summary>
public sealed class LoadBearingArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("layering/core-no-roslyn")
            .Enforce(arch.Project("Zphil.LoadBearing")
                .MustNotReference(arch.Project("Zphil.LoadBearing.Roslyn")))
            .Because("Core is the netstandard2.0 reified model both render targets consume; " +
                     "Roslyn extraction is host machinery.")
            .Fix("Depend on the Codebase model types in Core; keep Microsoft.CodeAnalysis behind " +
                 "Zphil.LoadBearing.Roslyn.");
    }
}