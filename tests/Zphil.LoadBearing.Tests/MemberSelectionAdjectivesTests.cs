using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     Null-argument guards on the member-adjective vocabulary (<see cref="MemberSelectionAdjectives" />):
///     each public narrowing extension routes its string/predicate argument through <c>Guard.NotNull</c>,
///     so a null argument is a programmer error that throws <see cref="ArgumentNullException" /> at the
///     call site — naming the offending parameter — rather than minting a selection that resolves emptily
///     later. The receiver is a <see cref="MethodSelection" />, so each call binds to the member-side
///     vocabulary (GRAMMAR §5.7), never the identically-named type-side twin.
/// </summary>
public sealed class MemberSelectionAdjectivesTests
{
    private static readonly Arch Arch = new();

    [Fact]
    public void WithSuffix_NullSuffix_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.WithSuffix(null!))
            .ParamName.ShouldBe("suffix");
    }

    [Fact]
    public void WithPrefix_NullPrefix_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.WithPrefix(null!))
            .ParamName.ShouldBe("prefix");
    }

    [Fact]
    public void WithNameMatching_NullGlob_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.WithNameMatching(null!))
            .ParamName.ShouldBe("glob");
    }

    [Fact]
    public void Where_NullPredicate_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.Where(null!, "d"))
            .ParamName.ShouldBe("predicate");
    }
}