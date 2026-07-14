using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The exception → stderr + exit-code mapping, unit-tested without a workspace: a
///     <see cref="SpecValidationException" /> lists every error at once (agents fix a spec in one
///     pass), a <see cref="UserErrorException" /> renders message-only, and both exit 2.
/// </summary>
public sealed class CliErrorMapperTests
{
    [Fact]
    public void Write_SpecValidationException_ListsEveryErrorAndExitsTwo()
    {
        // A real validation failure: two rules each missing the required .Because.
        var validation = Should.Throw<SpecValidationException>(() =>
            ArchModelBuilder.Build(new InlineSpec(arch =>
            {
                arch.Rule("area/one").Enforce(arch.Types.MustHavePrefix("X"));
                arch.Rule("area/two").Enforce(arch.Types.MustHaveSuffix("Y"));
            })));

        var error = new StringWriter();
        int exit = CliErrorMapper.Write(validation, error);

        exit.ShouldBe(2);
        var text = error.ToString();
        text.ShouldContain("Spec validation failed:");
        text.ShouldContain("area/one");
        text.ShouldContain("area/two");
    }

    [Fact]
    public void Write_UserError_RendersMessageOnlyAndExitsTwo()
    {
        var error = new StringWriter();

        int exit = CliErrorMapper.Write(new UserErrorException("no spec project found"), error);

        exit.ShouldBe(2);
        error.ToString().Trim().ShouldBe("no spec project found");
    }

    [Fact]
    public void UserFacingMessage_UnexpectedError_ReturnsNull()
    {
        CliErrorMapper.UserFacingMessage(new InvalidCastException("bug")).ShouldBeNull();
    }

    // The MCP GlobalCallToolFilter renders UserFacingMessage; the CLI renders Write. This pins them to
    // the identical multi-line body (after newline normalization) so the two surfaces stay in parity —
    // for a multi-line UserErrorException (the unknown-rule listing) and for spec validation.
    [Fact]
    public void Write_MatchesUserFacingMessage_ForMultilineUserError()
    {
        var exception = new UserErrorException("Unknown rule ID 'x'. Available rule IDs:\n  a/one\n  b/two");
        var error = new StringWriter();

        CliErrorMapper.Write(exception, error);

        Normalize(error.ToString()).ShouldBe(CliErrorMapper.UserFacingMessage(exception));
    }

    [Fact]
    public void Write_MatchesUserFacingMessage_ForSpecValidation()
    {
        var validation = Should.Throw<SpecValidationException>(() =>
            ArchModelBuilder.Build(new InlineSpec(arch =>
            {
                arch.Rule("area/one").Enforce(arch.Types.MustHavePrefix("X"));
                arch.Rule("area/two").Enforce(arch.Types.MustHaveSuffix("Y"));
            })));
        var error = new StringWriter();

        CliErrorMapper.Write(validation, error);

        Normalize(error.ToString()).ShouldBe(CliErrorMapper.UserFacingMessage(validation));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").TrimEnd('\n');
    }
}