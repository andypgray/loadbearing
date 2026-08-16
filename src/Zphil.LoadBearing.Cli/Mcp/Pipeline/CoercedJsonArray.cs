using System.Text.Json;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The JSON-array reading both array coercers do, written once. <c>string[]</c> and <c>TEnum[]</c>
///     face the same two shapes — a real array token, and a string whose contents are an array — and
///     differ only in what each element becomes, so the projection arrives as a delegate and the token
///     walk, the <c>[</c>-prefix fast path, and the "every element must be a string" rule have a single
///     spelling instead of a hand-maintained parity.
/// </summary>
/// <remarks>
///     What a malformed input <em>says</em> stays with the caller: the refusal names the parameter's own
///     element type, and those messages are the pinned surface a model reads to correct itself. Only the
///     mechanism is shared.
/// </remarks>
internal static class CoercedJsonArray
{
    /// <summary>
    ///     Reads tokens until the matching <see cref="JsonTokenType.EndArray" />, projecting each string
    ///     element through <paramref name="project" />. Hand-rolled rather than a nested
    ///     <c>JsonSerializer.Deserialize</c> so the read cannot recurse back through the coercer that
    ///     called it; <paramref name="onBadElement" /> supplies the caller's refusal for a non-string
    ///     element.
    /// </summary>
    internal static T[] ReadArray<T>(
        ref Utf8JsonReader reader, Func<string, T> project, Func<JsonTokenType, UserErrorException> onBadElement)
    {
        List<T> items = new();
        while (reader.Read())
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return items.ToArray();
                case JsonTokenType.String:
                    items.Add(project(reader.GetString()!));
                    break;
                default:
                    throw onBadElement(reader.TokenType);
            }

        throw new JsonException("Unexpected end of JSON while reading array.");
    }

    /// <summary>
    ///     Returns <c>true</c> only when <paramref name="value" /> parses as a JSON array whose every
    ///     element is a JSON string, each then projected through <paramref name="project" />. Anything
    ///     else — mixed types, nested arrays, malformed JSON, a scalar — returns <c>false</c> so the
    ///     caller falls back to single-element coercion. Surrounding whitespace is tolerated.
    /// </summary>
    /// <remarks>
    ///     A projection that throws is <em>not</em> caught: an unknown enum name inside a well-formed
    ///     array is a refusal that names the valid values, which serves the caller better than silently
    ///     re-reading the whole string as one element.
    /// </remarks>
    internal static bool TryParseJsonStringArray<T>(string value, Func<string, T> project, out T[] result)
    {
        result = [];

        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
        if (trimmed.Length == 0 || trimmed[0] != '[') return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(trimmed.ToString());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;

            List<T> items = new(doc.RootElement.GetArrayLength());
            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String) return false;

                items.Add(project(element.GetString()!));
            }

            result = items.ToArray();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
