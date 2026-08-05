using System.Reflection;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.SpecLoading;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The spec contract binds on the assembly, never on its version. A host carries exactly one copy of
///     <c>Zphil.LoadBearing</c>, and the default binder rejects a copy older than the reference a spec was
///     compiled against — so while the shipped identity tracked the package version, every release minted a
///     new one and any spec built against a newer contract failed to load at all, reported as a plain
///     missing file (issue #19). These pin the bind that replaced it, and the one failure it defers to.
/// </summary>
public sealed class SpecContractBindTests
{
    private static Version HostContractVersion => typeof(IArchitectureSpec).Assembly.GetName().Version!;

    [Theory]
    [InlineData("0.1.0.0")] // the identities shipped before the pin, whose specs are still out there
    [InlineData("0.2.0.0")]
    [InlineData("1.0.0.0")] // the pinned identity, and what a source-built or vendored contract carries
    [InlineData("9.9.9.9")] // and a version no host will ever carry
    public void LoadFromAssemblyName_ContractAtAnyVersion_ResolvesToTheHostsOwnCopy(string version)
    {
        var context = new SpecLoadContext(CliRunner.CleanSpecDll);
        var requested = new AssemblyName(SpecLoadContext.ContractAssemblyName) { Version = Version.Parse(version) };

        try
        {
            Assembly resolved = context.LoadFromAssemblyName(requested);

            // Same instance, not merely the same name: this is what makes `spec is IArchitectureSpec` hold
            // across the load-context boundary, and it is the whole reason the contract is short-circuited.
            resolved.ShouldBeSameAs(
                typeof(IArchitectureSpec).Assembly,
                $"a spec compiled against contract {version} must bind to the contract this host carries; "
                + "binding on the version instead is what failed specs that would have run perfectly.");
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Map_SpecBuiltAgainstANewerContract_NamesBothVersionsTheMissingMemberAndTheRemedy()
    {
        Assembly spec = SpecReferencing(new Version(2, 5, 0, 0));
        var missing = new MissingMethodException("Method not found: 'Void Zphil.LoadBearing.Arch.FutureVerb()'.");

        string message = SpecContractMismatch.Map(spec, Path.Combine("out", "Sync.ArchSpec.dll"), missing).Message;

        message.ShouldBe(
            "The spec assembly 'Sync.ArchSpec' calls LoadBearing API this tool's contract does not have "
            + $"(the spec was built against contract 2.5.0.0, this tool carries {HostContractVersion}):\n"
            + "  Method not found: 'Void Zphil.LoadBearing.Arch.FutureVerb()'.\n"
            + "That is a spec built against a newer Zphil.LoadBearing package than the tool running it: the spec "
            + "loads, and Define() runs until it reaches the member that is missing.\n"
            + "Update the tool to at least the spec's version (dotnet tool update -g Zphil.LoadBearing.Cli — this "
            + $"one is {ServerVersion.SemVer}), or reference the Zphil.LoadBearing package that matches this tool "
            + "from the spec project and rebuild it.");
    }

    [Fact]
    public void Map_SpecBuiltAgainstThisContract_OmitsTheVersionParenthetical()
    {
        // The normal case now that the identity is pinned: the versions match and naming them twice would
        // say nothing. Whatever went missing is still named, because that is the part the operator can act on.
        Assembly spec = SpecReferencing(HostContractVersion);
        var missing = new MissingMethodException("Method not found: 'Void Zphil.LoadBearing.Arch.FutureVerb()'.");

        string message = SpecContractMismatch.Map(spec, "Sync.ArchSpec.dll", missing).Message;

        message.ShouldStartWith(
            "The spec assembly 'Sync.ArchSpec' calls LoadBearing API this tool's contract does not have:\n"
            + "  Method not found: 'Void Zphil.LoadBearing.Arch.FutureVerb()'.");
    }

    /// <summary>
    ///     A stand-in spec assembly whose only interesting property is the contract version it references —
    ///     faked rather than built, so both arms pin exactly one variable and neither needs a second contract
    ///     compiled at a different identity. Hand-rolled rather than mocked: this project carries no mocking
    ///     package, and one override is not worth adding one to a locked dependency set.
    /// </summary>
    private static Assembly SpecReferencing(Version contractVersion)
    {
        return new SpecAssemblyStub(contractVersion);
    }

    private sealed class SpecAssemblyStub(Version contractVersion) : Assembly
    {
        public override AssemblyName[] GetReferencedAssemblies()
        {
            return [new AssemblyName(SpecLoadContext.ContractAssemblyName) { Version = contractVersion }];
        }
    }
}