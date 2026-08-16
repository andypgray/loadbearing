using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The pins under the startup sweep's reach. A fixture solution the sweep does not recognise loads
///     without its references instead of failing, so what the sweep accepts is held two ways: the extension
///     set directly, and the restore output of the one fixture solution written in the XML format.
/// </summary>
public sealed class FixtureRestorerTests
{
    private static string TestSolutionsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions");

    [Theory]
    [InlineData("MyApp.sln")]
    [InlineData("SlnxApp.slnx")]
    [InlineData("SHOUTING.SLNX")]
    public void IsSolutionFile_AFullSolution_IsSwept(string fileName)
    {
        FixtureRestorer.IsSolutionFile(fileName)
            .ShouldBeTrue($"'{fileName}' is a solution the sweep has to restore before anything opens it.");
    }

    [Theory]
    [InlineData("BillingOnly.slnf")]
    [InlineData("Malformed.slnf")]
    [InlineData("MyApp.Domain.csproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("Order.cs")]
    public void IsSolutionFile_AnythingElse_IsLeftAlone(string fileName)
    {
        FixtureRestorer.IsSolutionFile(fileName)
            .ShouldBeFalse($"'{fileName}' is not a solution this sweep restores, and a filter never is.");
    }

    /// <summary>
    ///     The behavioural half: the committed <c>.slnx</c> fixture arrives restored. It is the only fixture
    ///     the sweep can prove that format on, and an unrestored one would show up much later as a solution
    ///     that loaded without its references.
    /// </summary>
    [Fact]
    public void EnsureRestored_ASolutionInTheXmlFormat_IsRestoredLikeAnyOther()
    {
        FixtureRestorer.EnsureRestored();

        string assetsFile = Path.Combine(
            TestSolutionsDirectory, "SlnxApp", "SlnxApp.Core", "obj", "project.assets.json");

        File.Exists(assetsFile)
            .ShouldBeTrue($"the startup sweep left the .slnx fixture unrestored: nothing at '{assetsFile}'.");
    }

    /// <summary>
    ///     The premise the sweep's deliberate blind spot rests on. Filters are excluded because restoring one
    ///     is restoring the solution it references, and because a fixture filter may be malformed on purpose;
    ///     both hold only while no filter stands under this root. A filter fixture belongs beside its siblings
    ///     in <c>Fixtures/FilteredSolutions/</c>, which the sweep never reads.
    /// </summary>
    [Fact]
    public void TestSolutions_HoldsNoSolutionFilter_SoTheSweepMissesNothing()
    {
        string[] filters = Directory
            .EnumerateFiles(TestSolutionsDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path)
                .Equals(".slnf", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        filters.ShouldBeEmpty(
            "the startup sweep restores no filter, so one under this root would never be restored at all — "
            + "move it to Fixtures/FilteredSolutions/, or teach FixtureRestorer.IsSolutionFile to take it.");
    }
}
