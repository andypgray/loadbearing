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
///     failures they exist for. Each arm's <em>message</em> is pinned elsewhere against a fabricated
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
/// </summary>
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
        thrown.Message.ShouldEndWith("Build the spec project and restore its dependencies, then retry.");
    }

    [Fact]
    public void LoadModel_SpecWhoseAnchoredAssemblyIsMissing_ThrowsUserErrorNamingTheDependency()
    {
        // Arrange: LegacySpec's own types all load, so it survives discovery and fails on the typeof()
        // anchor inside Define() — the missing-dependency arm rather than the missing-type one.
        string stagedSpec = SpecOutputStager.StageWithout(CliRunner.LegacySpecDll, ProductDll);

        // Act
        var thrown = Should.Throw<UserErrorException>(() => ModelPipeline.LoadModel(stagedSpec));

        // Assert
        thrown.InnerException.ShouldBeOfType<FileNotFoundException>();
        thrown.Message.ShouldStartWith(
            "The spec assembly 'Zphil.LoadBearing.LegacySpec' failed to load its dependency "
            + "'Zphil.LoadBearing.LegacyProduct,");
        thrown.Message.ShouldContain("while running Define().");
        thrown.Message.ShouldContain("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>");
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