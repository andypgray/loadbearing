using System.Numerics;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Helpers for enum tool-input coercion shared by <see cref="EnumArrayCoercerFactory" /> and
///     <see cref="EnumValidationConverterFactory" />.
/// </summary>
internal static class EnumStringHelper
{
    /// <summary>
    ///     Returns <c>true</c> when <see cref="Enum.TryParse{T}(string,bool,out T)" /> would resolve
    ///     <paramref name="value" /> by arithmetic over ordinals rather than by matching one member
    ///     name, which is how a caller ends up bound to a member they never named.
    /// </summary>
    /// <remarks>
    ///     Two routes, both blocked here: an integer in disguise (see <see cref="LooksNumeric" />), and a
    ///     comma-separated name list, which <see cref="Enum.TryParse{T}(string,bool,out T)" /> ORs together
    ///     for <em>every</em> enum rather than only <see cref="FlagsAttribute" /> ones. The OR can land on
    ///     a defined member — with consecutive ordinals, <c>"A, B"</c> is <c>1|2 == 3</c> — so
    ///     <see cref="Enum.IsDefined" /> does not catch it. A comma is decisive on its own: no enum member
    ///     name can contain one.
    /// </remarks>
    internal static bool ResolvesByArithmetic(string value)
    {
        return value.Contains(',') || LooksNumeric(value);
    }

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="value" /> is an integer in disguise — a string
    ///     <see cref="Enum.TryParse{T}(string,bool,out T)" /> would bind to a numeric ordinal rather
    ///     than a name (e.g. <c>"5"</c> → <c>Method</c>), violating the documented "integers are not
    ///     admitted as enum values" contract.
    /// </summary>
    /// <remarks>
    ///     Trims surrounding whitespace first, then tests
    ///     <see cref="long.TryParse(ReadOnlySpan{char}, out long)" /> and
    ///     <see cref="BigInteger.TryParse(ReadOnlySpan{char}, out BigInteger)" />. A leading-digit
    ///     check is insufficient: <c>"5"</c>, <c>"+5"</c>, <c>" 5 "</c>, <c>"5 "</c>, and ordinals
    ///     wider than <see cref="long" /> must all be rejected.
    /// </remarks>
    internal static bool LooksNumeric(string value)
    {
        var trimmed = value.AsSpan().Trim();
        return long.TryParse(trimmed, out _) || BigInteger.TryParse(trimmed, out _);
    }
}
