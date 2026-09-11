using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Except(projects)</c> → ", except {reference}", canonicalized to sentence-final and after any
///     <c>Where</c> of the same subject (GRAMMAR §4.10, §6). The payload is another project selection and
///     renders in reference position via the renderer.
/// </summary>
/// <remarks>
///     Opens a parenthetical the composer closes at the junction that follows, and renders last so that
///     junction is adjacent — see <see cref="ExceptAdjective" />.
/// </remarks>
internal sealed class ProjectExceptAdjective(ProjectSelection payload) : ProjectAdjective
{
    /// <summary>The excluded project selection.</summary>
    internal ProjectSelection Payload { get; } = payload;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override bool OpensParenthetical => true;

    internal override string Fragment => ", except " + SentenceRenderer.ProjectReference(Payload);
}
