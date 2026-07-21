namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A container-registration fact inside one fragment (GRAMMAR §4.7): the <see cref="Lifetime" /> the
///     registration was made with, the service type's FQN, the implementation type's FQN (null for a
///     factory/instance form or the <c>AddHostedService</c> unresolvable-service fallback), and the distinct
///     <c>file:line</c> sites of the recognized registration calls (in <see cref="FragmentSite" /> order).
///     The merge unions site-sets across fragments per <c>(lifetime, service, implementation)</c>.
/// </summary>
/// <remarks>
///     Deliberately string-side — the service/implementation are FQNs, never resolved to a
///     <see cref="Zphil.LoadBearing.Codebase.TypeNode" /> (registration is many-to-many, so membership is a
///     model-side union at evaluation, not a denormalized node fact). The <see cref="Lifetime" /> is the Core
///     enum, so this record — like the rest of the fragment DTO — carries no dependency on
///     <c>Microsoft.CodeAnalysis</c> and round-trips through System.Text.Json unchanged (the enum serializes
///     as its name). The pure-data counterpart of <see cref="Zphil.LoadBearing.Codebase.ServiceRegistration" />.
/// </remarks>
internal sealed record FragmentServiceRegistration(
    Lifetime Lifetime,
    string ServiceFullName,
    string? ImplementationFullName,
    IReadOnlyList<FragmentSite> Sites);