namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A type referenced by a compilation but not declared by it — a BCL/NuGet (or, from this input's
///     narrow view, another project's) type, captured with the <see cref="TypeFacts">facts</see> read
///     from this compilation's metadata symbol plus the declaring <see cref="AssemblyName" />. The
///     assembly name becomes the external node's <c>ProjectName</c>.
/// </summary>
/// <remarks>
///     A fragment records an external for every FQN it references-but-does-not-declare, and the merge reads
///     <see cref="AssemblyName" /> twice over. It decides declared-beats-external: an external whose assembly
///     is one the declaring fragments produce is a view <em>of</em> that declaration, so it is discarded at
///     merge in favour of the real node — which is the ordinary cross-project case. An external whose assembly
///     is one <em>no</em> fragment produces is a different type that happens to share the full name, so both
///     survive, and it is this record that supplies the shallow node standing in for the assembly's half.
/// </remarks>
internal sealed record FragmentExternal(TypeFacts Facts, string AssemblyName);
