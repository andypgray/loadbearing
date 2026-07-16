namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A type referenced by a compilation but not declared by it — a BCL/NuGet (or, from this input's
///     narrow view, another project's) type, captured with the <see cref="TypeFacts">facts</see> read
///     from this compilation's metadata symbol plus the declaring <see cref="AssemblyName" />. The
///     assembly name becomes the external node's <c>ProjectName</c>.
/// </summary>
/// <remarks>
///     A fragment records an external for every FQN it references-but-does-not-declare; the merge keeps
///     an external node only when <em>no</em> fragment declares that FQN (declared-beats-external), so a
///     type another project declares is captured here yet discarded at merge in favour of the real node.
/// </remarks>
internal sealed record FragmentExternal(TypeFacts Facts, string AssemblyName);