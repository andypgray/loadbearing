namespace Zphil.LoadBearing.SpecFixture;

/// <summary>
///     A spec in a separate csproj that references only Core. Loaded in an isolated
///     <c>AssemblyLoadContext</c> by the test project to prove the spec-loading contract: reflect,
///     discover, build, and enumerate across the ALC boundary with the shared contract type
///     resolving from the Default context.
/// </summary>
public sealed class FixtureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("fixture/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("Fixture.*").MustHavePrefix("I"))
            .Because("Fixture naming convention.");
    }
}