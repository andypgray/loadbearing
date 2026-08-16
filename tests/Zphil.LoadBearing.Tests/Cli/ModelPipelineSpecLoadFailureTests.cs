using System.Reflection;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Discovery;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The join between <see cref="ModelPipeline.LoadModel" />'s four spec-load catch arms and the
///     failures they exist for.
/// </summary>
/// <remarks>
///     Each arm's <em>message</em> is pinned elsewhere against a fabricated
///     exception (<see cref="ModelPipelineLoaderFailureTests" />,
///     <c>SpecDependencyLoadFailureTests</c>), and the runtime behaviour of the unloadable type is pinned
///     in <c>LegacySpecLoadingTests</c> — but proving both halves does not prove they meet. These tests
///     drive real spec DLLs through the entry point a user reaches, and assert the inner exception as
///     well as the text, because the inner exception is what identifies <em>which</em> arm caught.
///     <para>
///         Two failures come from one fixture. <c>Zphil.LoadBearing.LegacyBrokenSpec</c> declares a type
///         whose base lives in the product assembly and a <c>Define()</c> that anchors a type which cannot
///         load on .NET at all; withholding the product DLL therefore fails it during discovery, and
///         staging it intact fails it during <c>Define()</c>. See <see cref="SpecOutputStager" /> for why
///         withholding a real build beats committing a corrupt one.
///     </para>
///     <para>
///         The missing-dependency arm reaches two different remedies, and both are driven here from real
///         builds rather than from fabricated exceptions, because which remedy the reader is handed is the
///         part that was wrong in the field. <c>Zphil.LoadBearing.SharedFrameworkSpec</c> is the one no
///         build setting can fix — it needs no staging trick at all, because a shared framework is absent
///         from a spec's output however the spec project is written.
///     </para>
/// </remarks>
public sealed class ModelPipelineSpecLoadFailureTests
{
    private const string ProductDll = "Zphil.LoadBearing.LegacyProduct.dll";

    [Fact]
    public void LoadModel_AssemblyContainingNoSpec_ThrowsUserErrorNamingTheAssembly()
    {
        // Arrange: an ordinary product DLL, the thing a user points --spec at by mistake.
        string productPath = CliRunner.MyAppDomainDll;

        // Act
        var thrown = Should.Throw<UserErrorException>(() => ModelPipeline.LoadModel(productPath));

        // Assert
        thrown.InnerException.ShouldBeOfType<SpecDiscoveryException>();
        thrown.Message.ShouldBe(
            "No public IArchitectureSpec implementations found in assembly 'MyApp.Domain'.");
    }

    [Fact]
    public void LoadModel_SpecWithATypeWhoseBaseAssemblyIsMissing_ThrowsUserErrorCarryingTheLoaderDetail()
    {
        // Arrange: discovery's GetTypes() loads every declared type's base, so withholding the product
        // assembly fails the spec before Define() is ever reached.
        string stagedSpec = SpecOutputStager.StageWithout(CliRunner.LegacyBrokenSpecDll, ProductDll);

        // Act
        var thrown = Should.Throw<UserErrorException>(() => ModelPipeline.LoadModel(stagedSpec));

        // Assert: the frame is pinned; the loader's own wording inside it is the runtime's to choose.
        thrown.InnerException.ShouldBeOfType<ReflectionTypeLoadException>();
        thrown.Message.ShouldStartWith(
            "Could not load spec assembly 'Zphil.LoadBearing.LegacyBrokenSpec.dll'; "
            + "one or more types failed to load:\n");
        thrown.Message.ShouldContain("Zphil.LoadBearing.LegacyProduct");
        thrown.Message.ShouldEndWith(
            "name the type as a string rather than a typeof(), which needs no assembly load.");
    }

    [Fact]
    public void LoadModel_SpecWhoseAnchoredAssemblyIsMissing_ThrowsUserErrorNamingTheDependency()
    {
        // Arrange: LegacySpec's own types all load, so it survives discovery and fails on the typeof()
        // anchor inside Define() — the missing-dependency arm rather than the missing-type one.
        string stagedSpec = SpecOutputStager.StageWithout(CliRunner.LegacySpecDll, ProductDll);

        // Act
        var thrown = Should.Throw<UserErrorException>(() => ModelPipeline.LoadModel(stagedSpec));

        // Assert: a net48 spec has no .deps.json, so the remedy keys on an absent manifest — this pin
        // moved deliberately off the packaging remedy it used to assert, which was only ever right for a
        // package the manifest names.
        thrown.InnerException.ShouldBeOfType<FileNotFoundException>();
        thrown.Message.ShouldStartWith(
            "The spec assembly 'Zphil.LoadBearing.LegacySpec' failed to load its dependency "
            + "'Zphil.LoadBearing.LegacyProduct,");
        thrown.Message.ShouldContain("while running Define().");
        thrown.Message.ShouldContain("a .NET Framework spec has no manifest at all");
        thrown.Message.ShouldNotContain("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>");
    }

    [Fact]
    public void LoadModel_SpecAnchoringASharedFrameworkType_NamesTheFrameworkAndWithholdsThePackagingRemedy()
    {
        // Arrange: the fixture exactly as built, CopyLocalLockFileAssemblies already on. Guard the
        // precondition first — on a host that happened to carry ASP.NET Core the anchor would resolve and
        // this fixture would prove nothing, so say that rather than leave a bare "expected an exception".
        Should.Throw<FileNotFoundException>(
            () => Assembly.Load(new AssemblyName("Microsoft.AspNetCore.Mvc.Core")),
            "This test host can load Microsoft.AspNetCore.Mvc.Core itself, so the fixture's anchor would "
            + "resolve through the default context and could not reproduce the failure it exists for.");

        // Act
        var thrown = Should.Throw<UserErrorException>(() => ModelPipeline.LoadModel(CliRunner.SharedFrameworkSpecDll));

        // Assert: the third world named by name. The packaging remedy is mentioned only to be ruled out —
        // the field agent had already applied it before the failure, so silence about it would read as an
        // omission rather than an answer.
        thrown.InnerException.ShouldBeOfType<FileNotFoundException>();
        thrown.Message.ShouldStartWith(
            "The spec assembly 'Zphil.LoadBearing.SharedFrameworkSpec' failed to load its dependency "
            + "'Microsoft.AspNetCore.Mvc.Core,");
        thrown.Message.ShouldContain("a .NET shared framework pulled in by <FrameworkReference>");
        thrown.Message.ShouldContain("CopyLocalLockFileAssemblies has nothing to copy");
        thrown.Message.ShouldNotContain("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>");

        // And the remedy the whole fixture exists to advertise: naming the world is only half an answer if
        // the reader is not handed the hatch out of it, spelled on the very type that failed.
        thrown.Message.ShouldContain(
            ".DerivedFrom(\"Microsoft.AspNetCore.Mvc.ControllerBase\") renders identically to the typeof() form");
    }

    [Fact]
    public void LoadModel_SpecAnchoringAFrameworkOnlyType_ThrowsUserErrorNamingTheType()
    {
        // Arrange: the fixture exactly as built, product DLL beside it. Nothing is missing — the anchored
        // type's interface simply has no counterpart on .NET, which no build setting can fix.
        string specPath = CliRunner.LegacyBrokenSpecDll;

        // Act
        var thrown = Should.Throw<UserErrorException>(() => ModelPipeline.LoadModel(specPath));

        // Assert
        thrown.InnerException.ShouldBeOfType<TypeLoadException>();
        thrown.Message.ShouldStartWith(
            "The spec assembly 'Zphil.LoadBearing.LegacyBrokenSpec' could not load the type "
            + "'System.Web.IHttpHandler' while running Define().");
        thrown.Message.ShouldContain("arch.Namespace(...)");
    }
}
