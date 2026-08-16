using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The version-skew path end to end: a spec built against a contract identity this host does not
///     carry, driven through the real CLI. Two skewed identities — one older than the host's, one newer —
///     bind and explain fine, and a spec reaching for a contract member this host does not define is
///     refused with the member, both versions, and both remedies named.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="SpecContractBindTests" /> pins both halves separately and neither meets the other:
///         its bind arms resolve a hand-built <see cref="AssemblyName" /> through
///         <see cref="SpecLoadContext" /> without ever loading a spec, and its message arms map a
///         <em>fabricated</em> <see cref="MissingMethodException" /> off an <see cref="Assembly" /> subclass
///         that overrides nothing but <see cref="Assembly.GetReferencedAssemblies" />. These drive a real
///         DLL through a real <see cref="System.Runtime.Loader.AssemblyLoadContext" /> to a real
///         <see cref="MissingMethodException" /> raised by the runtime mid-<c>Define()</c>, and assert the
///         exit code a user sees.
///     </para>
///     <para>
///         All three arms take the <c>explain</c> DLL fast path, which short-circuits ahead of the
///         workspace, so no solution is loaded and each call is sub-second.
///     </para>
///     <para>
///         The absent-member refusal was measured once by hand, against contracts built outside the
///         repository so the deliberate <c>AssemblyVersion</c> pin would not apply. This is the permanent
///         guard that measurement asked for, with the out-of-tree build replaced by
///         <see cref="SkewedContract" />.
///     </para>
/// </remarks>
public sealed class SpecContractSkewE2ETests
{
    private const string RuleId = "naming/services-suffixed";

    private const string Because = "A service's name is the first thing a reader has to go on.";

    private const string SpecAssemblyName = "Zphil.LoadBearing.ContractSkewSpec";

    private const string AbsentMemberCall = "arch.FutureVerb(\"a verb only the newer contract declares\");";

    private static Version HostContractVersion => typeof(IArchitectureSpec).Assembly.GetName()
        .Version!;

    [Theory]
    [InlineData("0.2.0.0")] // older than the host's pinned identity…
    [InlineData("2.0.0.0")] // …and newer, the direction that used to fail at load with a missing-file error
    public async Task Explain_SpecBuiltAgainstASkewedContract_BindsAndExplainsTheRule(string version)
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("spec-contract-skew");
        string specDll = EmitSkewedSpec(temp, Version.Parse(version), "");
        ShouldReferenceContract(specDll, Version.Parse(version));

        // Act
        CliResult result = await CliRunner.InvokeAsync("explain", RuleId, "--spec", specDll);

        // Assert: the rule's own fields, so a green that rendered nothing cannot pass for a bind.
        result.ShouldSucceed($"{RuleId} (enforce)", $"  because: {Because}");
    }

    [Fact]
    public async Task Explain_SkewedSpecCallingAnAbsentMember_RefusesNamingTheMemberBothVersionsAndBothRemedies()
    {
        // Arrange: the same spec, with the one call the host's contract cannot satisfy switched in. A spec
        // that does not CALL the member emits no memberref for it, which is why one contract serves both
        // directions.
        using TempDirectory temp = TestTempRoot.Fresh("spec-contract-skew");
        string specDll = EmitSkewedSpec(temp, new Version(2, 0, 0, 0), AbsentMemberCall);

        // Act
        CliResult result = await CliRunner.InvokeAsync("explain", RuleId, "--spec", specDll);

        // Assert: the frame is pinned, and the runtime's own "Method not found:" wording is quoted only as
        // far as the member signature — the part the reader acts on is ours, the sentence around it is not.
        result.ShouldRefuseWith(
            $"The spec assembly '{SpecAssemblyName}' calls LoadBearing API this tool's contract does not have",
            $"(the spec was built against contract 2.0.0.0, this tool carries {HostContractVersion})",
            "Zphil.LoadBearing.Arch.FutureVerb(System.String)",
            "dotnet tool update -g Zphil.LoadBearing.Cli",
            "reference the Zphil.LoadBearing package that matches this tool");
    }

    private static string EmitSkewedSpec(TempDirectory temp, Version contractVersion, string absentMemberCall)
    {
        string specDll = temp.PathOf(SpecAssemblyName + ".dll");
        SpecAssemblyCompiler.EmitSpecDll(
            SpecSource(absentMemberCall), specDll, SpecAssemblyName, SkewedContract.Reference(contractVersion));
        return specDll;
    }

    private static string SpecSource(string absentMemberCall)
    {
        return $$"""
                 using Zphil.LoadBearing;

                 namespace ContractSkew
                 {
                     public sealed class ContractSkewSpec : IArchitectureSpec
                     {
                         public void Define(Arch arch)
                         {
                             {{absentMemberCall}}
                             arch.Rule("{{RuleId}}")
                                 .Enforce(arch.Types.MustHaveSuffix("Service"))
                                 .Because("{{Because}}");
                         }
                     }
                 }
                 """;
    }

    /// <summary>
    ///     Asserts the emitted spec really carries the skewed contract identity. Without it the green arms
    ///     would pass just as happily on a spec that had been built against the host's own contract — which
    ///     is the one thing they exist to rule out.
    /// </summary>
    private static void ShouldReferenceContract(string specDllPath, Version expected)
    {
        using FileStream stream = File.OpenRead(specDllPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();

        var referenced = metadata.AssemblyReferences
            .Select(metadata.GetAssemblyReference)
            .Where(reference => metadata.GetString(reference.Name) == SpecLoadContext.ContractAssemblyName)
            .Select(reference => reference.Version)
            .ToList();

        referenced.ShouldBe(
            [expected],
            $"'{Path.GetFileName(specDllPath)}' must reference exactly one {SpecLoadContext.ContractAssemblyName}, "
            + "at the skewed identity it was built against.");
    }
}
