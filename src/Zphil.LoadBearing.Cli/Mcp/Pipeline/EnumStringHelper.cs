using System.Numerics;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Helpers for enum tool-input coercion shared by <see cref="EnumArrayCoercerFactory" /> and
///     <see cref="EnumValidationConverterFactory" />.
/// </summary>
internal static class EnumStringHelper
{
    /// <summary>
    ///     The one enum-name rule both coercers apply: refuse anything
    ///     <see cref="ResolvesByArithmetic" /> would let through by ordinal arithmetic, then match a
    ///     member name case-insensitively and require that member to be defined. A name the caller may not
    ///     reach returns <c>false</c> with <paramref name="parsed" /> left at its default, never at the
    ///     undefined value <see cref="Enum.TryParse{T}(string,bool,out T)" /> would have bound it to.
    /// </summary>
    /// <remarks>
    ///     Here rather than in either factory because the array path and the scalar path must admit the
    ///     same spellings: a name a tool parameter refuses cannot be one the same tool's array parameter
    ///     accepts.
    /// </remarks>
    internal static bool TryParseName<T>(string name, out T parsed) where T : struct, Enum
    {
        parsed = default;
        if (ResolvesByArithmetic(name)) return false;

        if (!Enum.TryParse(name, true, out T candidate) || !Enum.IsDefined(typeof(T), candidate)) return false;

        parsed = candidate;
        return true;
    }

    /// <summary>
    ///     The refusal a rejected value gets, naming what was attempted and listing every valid value so
    ///     the model can self-correct on the next call. One builder, so the scalar path and the array path
    ///     cannot word the same rejection differently — the text is pinned as the spec.
    /// </summary>
    internal static string InvalidValueMessage<T>(string? attempted) where T : struct, Enum
    {
        return $"Invalid value \"{attempted}\" for parameter. Valid values: {string.Join(", ", Enum.GetNames<T>())}.";
    }

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
