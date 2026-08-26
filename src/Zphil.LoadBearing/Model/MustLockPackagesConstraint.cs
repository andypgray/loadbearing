using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustLockPackages()</c> → "must lock package restore" (GRAMMAR §4.10) — the
///     <c>RestorePackagesWithLockFile</c> supply-chain law, stated over the evaluated property rather than
///     over the presence of a <c>packages.lock.json</c> on disk.
/// </summary>
internal sealed class MustLockPackagesConstraint(ProjectSelection subject) : ProjectConstraint(subject)
{
    internal override string VerbPhrase => "must lock package restore";
}
