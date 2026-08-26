using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotBePackable()</c> → "must not be packable" (GRAMMAR §4.10) — the law that keeps an
///     internal project off the feed. Its polarity is the one that matters: the SDK defaults
///     <c>IsPackable</c> on, so the projects that ship and the projects that merely compile look alike
///     until one opts out, and the ban is what makes the opt-out checkable.
/// </summary>
internal sealed class MustNotBePackableConstraint(ProjectSelection subject) : ProjectConstraint(subject)
{
    internal override string VerbPhrase => "must not be packable";
}
