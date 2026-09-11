using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     Posture stage for a scope anchor: <c>Quarantine</c> and <c>Caution</c> each record the scoped
///     selection and the posture it was scoped under (GRAMMAR §3.2).
/// </summary>
internal sealed class ScopeBuilder(ScopeRegistration registration) : IScopeBuilder
{
    public IQuarantinedScope Quarantine(Selection selection)
    {
        Record(selection, Posture.Quarantine);
        return new QuarantinedScopeBuilder(registration);
    }

    public ICautionedScope Caution(Selection selection)
    {
        Record(selection, Posture.Caution);
        return new CautionedScopeBuilder(registration);
    }

    // One selection field and one posture field, whichever verb was called: a scope has exactly one
    // posture, so a stored builder's second call overwrites both and the count is what reports it
    // (§8 item 17).
    private void Record(Selection selection, Posture posture)
    {
        registration.PostureCount++;
        registration.Posture = posture;
        registration.Scoped = Guard.NotNull(selection, nameof(selection));
    }
}
