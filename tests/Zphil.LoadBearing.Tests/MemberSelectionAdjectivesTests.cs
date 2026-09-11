using Shouldly;
using Xunit;
using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The member-adjective vocabulary's guards and self-type contract
///     (<see cref="MemberSelectionAdjectives" />), plus the two methods-only positions beside it —
///     <see cref="MethodSelection.Returning(Type,Type[])" /> and
///     <see cref="MethodSelectionConstraints.MustAcceptParameter(MethodSelection,Type)" />. Each public
///     narrowing routes its
///     string/predicate argument through <c>Guard.NotNull</c>, so a null argument is a programmer error
///     that throws <see cref="ArgumentNullException" /> at the call site — naming the offending parameter
///     — rather than minting a selection that resolves emptily later. Every receiver here is a concrete
///     member selection, so each call binds to the member-side vocabulary (GRAMMAR §5.7), never the
///     identically-named type-side twin; the guard rows spell a <see cref="MethodSelection" />, and the
///     closing row spells all three, because preserving each is its claim.
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

    [Fact]
    public void AttributedWith_NullAttributeType_ThrowsArgumentNullException()
    {
        // The cast picks the arm: a bare `null!` is ambiguous between the typeof and string overloads.
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.AttributedWith((Type)null!))
            .ParamName.ShouldBe("attributeType");
    }

    [Fact]
    public void AttributedWith_NullAttributeFullName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.AttributedWith((string)null!))
            .ParamName.ShouldBe("attributeFullName");
    }

    [Fact]
    public void Returning_NullType_ThrowsArgumentNullException()
    {
        // The cast picks the arm, as on AttributedWith. Both arms guard through the shared operand-list
        // builder, so the reported parameter is the list head's name rather than either overload's own.
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.Returning((Type)null!))
            .ParamName.ShouldBe("first");
    }

    [Fact]
    public void Returning_NullTypeName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.Returning((string)null!))
            .ParamName.ShouldBe("first");
    }

    [Fact]
    public void MustAcceptParameter_NullParameterType_ThrowsArgumentNullException()
    {
        // The methods-only verb guards its single anchor directly, so each arm names its own parameter.
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.MustAcceptParameter((Type)null!))
            .ParamName.ShouldBe("parameterType");
    }

    [Fact]
    public void MustAcceptParameter_NullParameterTypeFullName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Methods.MustAcceptParameter((string)null!))
            .ParamName.ShouldBe("parameterTypeFullName");
    }

    [Fact]
    public void ThatAreStatic_PreservesTheConcreteSelectionType()
    {
        // A COMPILE-TIME pin: the TSelf shape returns each projection's own type, so the kind-only verb
        // beside it stays reachable after the adjective. Each line below would fail to compile against a
        // bare MemberSelection return, which is the whole claim — the assertions merely observe that the
        // three chains do reify.
        Arch.Types.Methods.ThatAreStatic()
            .Returning(typeof(Task))
            .ShouldBeOfType<MethodSelection>();
        Arch.Types.Properties.ThatAreStatic()
            .MustBeGetOnly()
            .ShouldNotBeNull();
        Arch.Types.Fields.ThatAreStatic()
            .MustBeReadonly()
            .ShouldNotBeNull();
    }
}
