using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     What one run's extraction produced: the codebase to check, and the declared projects that run did
///     not check.
/// </summary>
/// <remarks>
///     The two travel together because the second is only known once the workspace is open, and the xUnit
///     adapter opens it <em>inside</em> the extract delegate — so a narrowing handed to
///     <see cref="ArchCheckSequence" /> eagerly would be read before the load that measures it, and loading
///     earlier to have it would put extraction ahead of the baselines both of that type's xmldocs commit to.
/// </remarks>
/// <param name="Codebase">The extracted model the rules are evaluated against.</param>
/// <param name="UncheckedProjects">
///     The absolute <c>.csproj</c> paths the solution declares that this run did not check
///     (<see cref="WorkspaceDiagnostics.UncheckedProjects" />) — empty on any run no filter narrowed.
/// </param>
internal sealed record ExtractedCodebase(CodebaseModel Codebase, IReadOnlyList<string> UncheckedProjects);
