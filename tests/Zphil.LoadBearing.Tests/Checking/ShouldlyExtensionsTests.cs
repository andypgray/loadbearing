using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     That the case-sensitive string overloads are the ones a test file binds to. Deliberately declared
///     in a <em>nested</em> namespace: the whole cure rests on extension lookup reaching the suite's root
///     namespace before the compilation unit's <c>using Shouldly</c>, and only a file below the root can
///     demonstrate it. Moving <see cref="ShouldlyExtensions" /> or this file reds this.
/// </summary>
public sealed class ShouldlyExtensionsTests
{
    [Fact]
    public void ShouldContain_WrongCase_Reds()
    {
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldContain("homecontroller"));
        "App.HomeController".ShouldContain("HomeController");
    }

    [Fact]
    public void ShouldContain_ExplicitInsensitive_FallsThroughToShouldly()
    {
        // The opt-out: naming a Case has no overload here, so the call binds to Shouldly's own.
        "App.HomeController".ShouldContain("homecontroller", Case.Insensitive);
    }

    [Fact]
    public void ShouldStartWithAndShouldEndWith_WrongCase_Red()
    {
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldStartWith("app."));
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldEndWith("controller"));
    }

    [Fact]
    public void ShouldNotContain_DifferingOnlyInCase_Passes()
    {
        // The mirror image, and the reason this half of the cure can never turn a green red.
        "App.HomeController".ShouldNotContain("homecontroller");
    }
}
