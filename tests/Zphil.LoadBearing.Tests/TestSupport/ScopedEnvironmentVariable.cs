namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     One process-wide environment variable, set for the life of a <c>using</c> and restored — not cleared —
///     after it. For the tests whose subject is the real <see cref="Environment" /> read rather than the
///     suite's <c>IEnvironment</c> fake.
/// </summary>
/// <remarks>
///     The mutation is process-wide, so it is only safe inside the serial collection: a parallel test reading
///     the same variable would see this one's value. Restoring rather than clearing is what keeps a run-wide
///     default — one a module initializer set, say — alive for the next test in that collection.
/// </remarks>
internal sealed class ScopedEnvironmentVariable : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    /// <summary>Captures the current value of <paramref name="name" /> and sets it to <paramref name="value" />.</summary>
    internal ScopedEnvironmentVariable(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>Puts the captured value back — clearing the variable only if it was absent to begin with.</summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _previous);
    }
}
