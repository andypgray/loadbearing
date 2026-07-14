namespace Zphil.LoadBearing.Internal;

/// <summary>
///     Minimal argument guards for the public API boundary. netstandard2.0 lacks the
///     <c>ArgumentNullException.ThrowIfNull</c> / <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>
///     throw helpers, so these stand in for programmer-error checks only. Spec-author mistakes
///     are not guarded here — they are collected and reported by the validation catalog (§8).
/// </summary>
internal static class Guard
{
    /// <summary>Throws <see cref="ArgumentNullException" /> when <paramref name="value" /> is null.</summary>
    internal static T NotNull<T>(T? value, string paramName)
        where T : class
    {
        return value ?? throw new ArgumentNullException(paramName);
    }

    /// <summary>Throws when <paramref name="value" /> is null, empty, or all whitespace.</summary>
    internal static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (value is null) throw new ArgumentNullException(paramName);

        if (value.Trim().Length == 0) throw new ArgumentException("Value must not be blank.", paramName);

        return value;
    }
}