using Shouldly;
using Xunit;
using Xunit.Sdk;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The pins under <see cref="TempFixtureWorkspace" />'s restore trigger and its serial-collection guard.
///     A leased tree is reset between tests and re-restored only when a project or solution file actually
///     changed, so a solution format the reset does not recognise costs the next test a stale
///     <c>project.assets.json</c> — and says nothing. Both halves are held: the accepted set directly, and
///     the whole lease-reset-restore path over the one fixture solution written in the XML format.
/// </summary>
[Collection("Serial")]
public sealed class TempFixtureWorkspaceTests
{
    private const string SlnxFixtureDirectory = "TestSolutions/SlnxApp";

    private const string SlnxSolutionFileName = "SlnxApp.slnx";

    private const string SlnxProjectDirectory = "SlnxApp.Core";

    [Theory]
    [InlineData("MyApp.csproj")]
    [InlineData("MyApp.sln")]
    [InlineData("MyApp.slnx")]
    [InlineData("BillingOnly.slnf")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("SHOUTING.SLNX")]
    public void IsProjectFile_AFileARestoreDependsOn_IsRecognised(string fileName)
    {
        TempFixtureWorkspace.IsProjectFile(fileName)
            .ShouldBeTrue($"'{fileName}' can change what a restore resolves, so a copy of it must trigger one.");
    }

    [Theory]
    [InlineData("Order.cs")]
    [InlineData("clean-baseline.json")]
    [InlineData("README.md")]
    [InlineData("MyApp.sln.bak")]
    [InlineData("Makefile")]
    public void IsProjectFile_AFileNoRestoreDependsOn_IsNotRecognised(string fileName)
    {
        TempFixtureWorkspace.IsProjectFile(fileName)
            .ShouldBeFalse($"'{fileName}' cannot change what a restore resolves, so copying it must not cost one.");
    }

    /// <summary>
    ///     The end-to-end half, over the one solution format that had no fixture to prove it with. The
    ///     arrangement leaves the second lease exactly one changed file and exactly one reason to restore, so
    ///     an unrecognised <c>.slnx</c> shows up as the restore that never ran rather than as nothing at all.
    /// </summary>
    [Fact]
    public void LeaseReset_WhenOnlyAnSlnxChanged_RestoresTheCopyAgain()
    {
        string leasedSolution;
        string projectDirectory;
        using (var first = new TempFixtureWorkspace(SlnxFixtureDirectory, SlnxSolutionFileName))
        {
            leasedSolution = first.SolutionPath;
            projectDirectory = first.PathOf(SlnxProjectDirectory);
            File.Exists(AssetsFileIn(projectDirectory))
                .ShouldBeTrue($"the first lease of '{SlnxFixtureDirectory}' copied the fixture in but never restored it.");
        }

        // Strip the restore's output and edit the leased solution file: the source tree is untouched, so the
        // next reset copies the .slnx back and nothing else differs.
        ReadOnlyTolerant.DeleteTree(Path.Combine(projectDirectory, "obj"));
        File.AppendAllText(leasedSolution, "<!-- edited between leases -->\n");

        using var second = new TempFixtureWorkspace(SlnxFixtureDirectory, SlnxSolutionFileName);

        // Same tree, or the assertion below would be reading a directory the first lease never wrote to.
        second.SolutionPath.ShouldBe(leasedSolution);
        File.Exists(AssetsFileIn(projectDirectory))
            .ShouldBeTrue($"the reset restored '{SlnxSolutionFileName}' to its fixture content without re-restoring the copy.");
    }

    /// <summary>
    ///     The value the guard compares against, read from the runner rather than assumed. This class is a
    ///     member of the collection, so a runner that reported the name some other way would make every
    ///     construction in the suite refuse — and this is the one assertion that would say why.
    /// </summary>
    [Fact]
    public void SerialMembership_IsReportedAsTheDefinitionTypesFullName()
    {
        ITestCollection? collection = TestContext.Current.TestCollection;

        collection.ShouldNotBeNull("a running test always belongs to some collection.");
        collection.TestCollectionClassName.ShouldBe(
            typeof(SerialCollection).FullName,
            "the guard recognises membership by this name, so nothing else can be in the \"Serial\" collection.");
    }

    /// <summary>
    ///     <c>null</c> is the case that matters: it is what a test class with no <c>[Collection]</c>
    ///     attribute reports, which is exactly the drift the guard exists to catch.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Zphil.LoadBearing.Tests.TestSupport.SomeOtherCollection")]
    public void RequireSerialCollection_ACallerOutsideTheCollection_IsRefusedByName(string? collectionClassName)
    {
        Action construct = () => TempFixtureWorkspace.RequireSerialCollection(
            collectionClassName, "SomeParallelE2ETests");

        var refusal = construct.ShouldThrow<InvalidOperationException>();
        refusal.Message.ShouldContain("SomeParallelE2ETests");
        refusal.Message.ShouldContain("[Collection(\"Serial\")]");
    }

    [Fact]
    public void RequireSerialCollection_ACallerInTheCollection_IsAllowed()
    {
        Action construct = () => TempFixtureWorkspace.RequireSerialCollection(
            typeof(SerialCollection).FullName, nameof(TempFixtureWorkspaceTests));

        construct.ShouldNotThrow();
    }

    private static string AssetsFileIn(string projectDirectory)
    {
        return Path.Combine(projectDirectory, "obj", "project.assets.json");
    }
}
