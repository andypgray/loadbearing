using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Tests.Baselines;

/// <summary>The conventional default baseline path (GRAMMAR §4.4): a rule ID's slashes become subdirectories.</summary>
public sealed class BaselineConventionsTests
{
    [Fact]
    public void DefaultPath_RuleIdWithSlashes_MapsToSubdirectories()
    {
        BaselineConventions.DefaultPath("data-access/no-inline-sql")
            .ShouldBe("arch/baselines/data-access/no-inline-sql.json");
    }

    [Fact]
    public void DefaultPath_SingleSegmentId_HasNoSubdirectory()
    {
        BaselineConventions.DefaultPath("naming").ShouldBe("arch/baselines/naming.json");
    }
}