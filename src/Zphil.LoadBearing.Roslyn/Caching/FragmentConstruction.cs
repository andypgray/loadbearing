namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A construction fact by FQN: the open-or-non-generic definition's fully-qualified name (which the
///     merge resolves to the shared definition node) plus the <em>constructed</em> display name. The
///     pure-data counterpart of <see cref="Zphil.LoadBearing.Codebase.TypeConstruction" /> — for a
///     non-generic type the two names coincide; for a closed generic they diverge
///     (<see cref="DefinitionFullName" /> <c>N.IHandler&lt;T&gt;</c> /
///     <see cref="ConstructedName" /> <c>N.IHandler&lt;N.Order&gt;</c>).
/// </summary>
internal sealed record FragmentConstruction(string DefinitionFullName, string ConstructedName);