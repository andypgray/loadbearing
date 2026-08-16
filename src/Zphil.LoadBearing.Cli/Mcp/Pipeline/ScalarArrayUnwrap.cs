using System.Text.Json;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The one-element-array unwrap the scalar coercers share. Models routinely wrap a single value in an
///     array (<c>["A"]</c>, <c>[true]</c>) where a scalar is advertised, and every coercer forgives it the
///     same way: read one element, insist the array ends there, and refuse an array holding more.
/// </summary>
/// <remarks>
///     <para>
///         Only the protocol lives here. What counts as a readable scalar, what the empty array means, and
///         the wording of every refusal stay with the coercer they belong to — the two differ on all three
///         (a <c>string</c> reads <c>[]</c> as "absent"; a non-nullable <c>bool</c> has no null to fall back
///         to), and their messages are pinned text.
///     </para>
///     <para>
///         Hand-rolled rather than <c>JsonSerializer.Deserialize&lt;T&gt;</c> for the element, which would
///         recurse straight back through the coercer's own factory.
///     </para>
/// </remarks>
internal static class ScalarArrayUnwrap
{
    /// <summary>
    ///     Reads one scalar of <typeparamref name="T" /> from the token <paramref name="reader" /> is
    ///     positioned at. A custom delegate rather than a <see cref="Func{T,TResult}" /> because a
    ///     <see cref="Utf8JsonReader" /> is a ref struct: it crosses a call boundary only by
    ///     <see langword="ref" />, which no lambda may capture, so every caller passes a static method group.
    /// </summary>
    /// <param name="reader">The reader, positioned at the element's first token.</param>
    /// <param name="insideArray">
    ///     Whether the element came out of an array. The coercer's own wording depends on it: naming an
    ///     "array element" tells the caller the coercer did look inside their array.
    /// </param>
    internal delegate T ScalarReader<out T>(ref Utf8JsonReader reader, bool insideArray);

    /// <summary>
    ///     Collapses the array <paramref name="reader" /> has opened to the single scalar it holds, applying
    ///     <paramref name="readScalar" /> to that one element.
    /// </summary>
    /// <param name="reader">The reader, positioned at the <see cref="JsonTokenType.StartArray" />.</param>
    /// <param name="readScalar">The coercer's own scalar rules.</param>
    /// <param name="onEmptyArray">What <c>[]</c> means for this coercer — a value, or a refusal.</param>
    /// <param name="multipleElementsMessage">The refusal when the array holds more than one element.</param>
    /// <exception cref="JsonException">The JSON ended inside the array.</exception>
    /// <exception cref="UserErrorException">The array holds more than one element.</exception>
    internal static T Read<T>(
        ref Utf8JsonReader reader,
        ScalarReader<T> readScalar,
        Func<T> onEmptyArray,
        string multipleElementsMessage)
    {
        if (!reader.Read()) throw new JsonException("Unexpected end of JSON while reading array.");

        if (reader.TokenType == JsonTokenType.EndArray) return onEmptyArray();

        T value = readScalar(ref reader, insideArray: true);

        if (!reader.Read()) throw new JsonException("Unexpected end of JSON while reading array.");

        if (reader.TokenType != JsonTokenType.EndArray) throw new UserErrorException(multipleElementsMessage);

        return value;
    }
}
