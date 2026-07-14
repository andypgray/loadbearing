namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Where a selection sits in a constraint, which fixes its candidate universe (GRAMMAR §4.1):
///     a <see cref="Subject" /> ranges over solution-declared types only; a <see cref="Target" />
///     (or source operand) ranges over all types, including external metadata nodes.
/// </summary>
internal enum SelectionPosition
{
    /// <summary>Subject position — solution-declared types only.</summary>
    Subject,

    /// <summary>Target/source position — all types, externals included.</summary>
    Target
}