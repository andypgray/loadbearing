using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The MSBuild tier of the shadowed-name split, over the real <c>ShadowedApp</c> fixture solution: a
///     product project binding <c>Microsoft.Extensions.DependencyInjection.ServiceDescriptor</c> from the
///     package, and a test project beside it declaring its own type of that full name. The fast-path bed in
///     <c>Extraction.PackageShadowedNameTests</c> pins the merge; this pins the half only a real workspace
///     can, that a project's assembly name reaches the fragment from its csproj at all — the fact the whole
///     split is decided by, and the one a hand-built input can never exercise.
/// </summary>
/// <remarks>
///     In the <see cref="SerialCollection">Serial</see> collection: every test here loads an MSBuild
///     workspace, and the first opens one of its own.
/// </remarks>
[Collection("Serial")]
public sealed class ShadowedPackageNameTests
{
    private const string Product = "ShadowedApp.Product";
    private const string Tests = "ShadowedApp.Product.Tests";
    private const string Shadowed = "Microsoft.Extensions.DependencyInjection.ServiceDescriptor";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "ShadowedApp", "ShadowedApp.slnx");

    private static readonly Lazy<Task<CodebaseModel>> ShadowedCodebase = new(ExtractAsync);

    [Fact]
    public async Task ExtractFromSolutionAsync_TheProductBindsThePackage_ReachesNoTypeOfTheTestProject()
    {
        CodebaseModel model = await ShadowedCodebase.Value;

        // The acceptance criterion as an absence: nothing the product references is attributed to the test
        // project, whatever names the two happen to share.
        model.Edges
            .Where(edge => edge.Source.ProjectName == Product)
            .Select(edge => edge.Target.ProjectName)
            .ShouldNotContain(Tests);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheShadowedName_CarriesTheDeclarationAndTheAssembly()
    {
        CodebaseModel model = await ShadowedCodebase.Value;

        model.Types
            .Where(type => type.FullName == Shadowed)
            .Select(type => $"{type.ProjectName} external={type.IsExternal}")
            .ShouldBe([$"{Tests} external=False", "Microsoft.Extensions.DependencyInjection.Abstractions external=True"]);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheTestProjectUsesItsOwnStandIn_ReachesItsOwnDeclaration()
    {
        CodebaseModel model = await ShadowedCodebase.Value;

        model.Edges
            .Single(edge => edge.Source.FullName == "ShadowedApp.Product.Tests.RegistryTests"
                            && edge.Target.FullName == Shadowed)
            .Target.ProjectName.ShouldBe(Tests);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheGenuineProjectReference_Survives()
    {
        CodebaseModel model = await ShadowedCodebase.Value;

        // The control the fixture's csproj comment names. A project reference records an external too, of the
        // referenced project's assembly, so a split decided on the wrong comparison would take this out.
        model.Edges
            .Single(edge => edge.Source.FullName == "ShadowedApp.Product.Tests.RegistryTests"
                            && edge.Target.FullName == "ShadowedApp.Product.Registry")
            .Target.ProjectName.ShouldBe(Product);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheShadowedName_IsReportedAsAMergeNote()
    {
        CodebaseModel model = await ShadowedCodebase.Value;

        model.MergeNotes.ShouldBe([
            $"Project '{Tests}' declares '{Shadowed}', which referenced assembly "
            + "'Microsoft.Extensions.DependencyInjection.Abstractions' also supplies; every reference resolves "
            + "to whichever of the two the referencing compilation bound, so a selection over project "
            + $"'{Tests}' reaches the declared one alone."
        ]);
    }

    [Fact]
    public async Task Summarize_TheShadowedName_IsStatedAsACoverageKey()
    {
        CodebaseModel model = await ShadowedCodebase.Value;

        ShadowedTypeSummary shadowed = GraphSummarizer.Summarize(model)
            .ShadowedTypes.ShouldHaveSingleItem();

        shadowed.ShouldSatisfyAllConditions(
            () => shadowed.Type.ShouldBe(Shadowed),
            () => shadowed.DeclaredBy.ShouldBe(Tests),
            () => shadowed.SuppliedBy.ShouldBe(["Microsoft.Extensions.DependencyInjection.Abstractions"]),
            () => shadowed.BoundFromAssemblyBy.ShouldBe([Product]));
    }

    [Fact]
    public async Task Check_ProductMustNotReferenceTests_PassesOverTheRealWorkspace()
    {
        CodebaseModel model = await ShadowedCodebase.Value;

        // Acceptance criterion #1 as the rule the field test wrote. Green alone would prove nothing — an
        // empty subject or target set is green too — so the row below reds on the same two selections
        // inverted, which is only possible if both are live.
        Checker.Run(model, arch =>
                arch.Rule("layering/product-must-not-reference-tests")
                    .Enforce(arch.Project(Product).MustNotReference(arch.Project(Tests)))
                    .Because("Shipping code must not depend on test assemblies."))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(model, arch =>
                arch.Rule("layering/inverted")
                    .Enforce(arch.Project(Tests).MustNotReference(arch.Project(Product)))
                    .Because("The inverse, so neither selection can be silently empty."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(
                ViolationKind.Reference, "ShadowedApp.Product.Tests.RegistryTests", "ShadowedApp.Product.Registry");
    }

    // The shared read: the pool loads the fixture once for the whole class. It takes no test's cancellation
    // token, because the task it produces outlives the test that first awaits it.
    private static async Task<CodebaseModel> ExtractAsync()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(SolutionPath, CancellationToken.None);
        return await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, null, snapshot.TargetFrameworks, null, CancellationToken.None);
    }
}
