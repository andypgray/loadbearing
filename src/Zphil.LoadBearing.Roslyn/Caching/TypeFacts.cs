namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The scalar shape facts of one type, extracted from a Roslyn symbol but holding no Roslyn types —
///     the pure-data payload shared by a fragment's declared types and its externals, and the first ten
///     constructor arguments of a <see cref="Zphil.LoadBearing.Codebase.TypeNode" />. These are read once
///     per input from the symbol's <c>OriginalDefinition</c> so a persisted fragment (Phase 11 WP6) can
///     rebuild the node without re-binding.
/// </summary>
/// <remarks>
///     <see cref="Kind" /> and <see cref="Accessibility" /> are the Core enums (never Roslyn's), so this
///     record — like the rest of the fragment DTO — carries no dependency on <c>Microsoft.CodeAnalysis</c>
///     and round-trips through System.Text.Json unchanged.
/// </remarks>
internal sealed record TypeFacts(
    string FullName,
    string SymbolId,
    string Name,
    string Namespace,
    TypeKind Kind,
    Accessibility Accessibility,
    bool IsSealed,
    bool IsStatic,
    bool IsAbstract,
    bool IsRecord);