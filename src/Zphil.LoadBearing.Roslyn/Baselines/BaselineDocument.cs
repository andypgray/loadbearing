using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Roslyn.Baselines;

/// <summary>
///     A parsed baseline file: every rule section keyed by rule ID — including sections for rules not
///     in the current model (e.g. a rule removed from the spec but still recorded on a shared file).
///     Unknown sections are preserved so a mutation command (<c>baseline</c>) recomposes them untouched.
///     Only <see cref="BaselineStore" /> constructs one, after the file's digest has verified.
/// </summary>
internal sealed record BaselineDocument(IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> Sections);