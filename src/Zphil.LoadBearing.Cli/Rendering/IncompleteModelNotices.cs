using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The one place the CLI refuses a run on an incomplete model: whether the gate fires, and the block
///     that says so when it does.
/// </summary>
/// <remarks>
///     <para>
///         Four verbs wrote the same three steps — ask <see cref="WorkspaceDiagnostics.Gates" /> whether the
///         model is incomplete and the operator did not opt out, write the verb's refusal to stderr as a line
///         block, exit 2 — and only the wording that followed differed. The wording stays in
///         <see cref="IncompleteModelGate" />, because the xUnit adapter and the MCP surface state the same
///         condition and have no writer to hand; what lives here is the CLI's half, which is the part that
///         had four copies.
///     </para>
///     <para>
///         The exit code stays with the caller, as it does for <see cref="NarrowingNotices.Refusal" />: this
///         reports whether it refused and the verb returns the 2. Where in a verb the refusal fires is a
///         per-verb decision — <c>check</c> and <c>status</c> render their answer first, so the verdict rides
///         the document, while <c>baseline</c> and <c>render</c> refuse before a byte is written.
///     </para>
/// </remarks>
internal static class IncompleteModelNotices
{
    /// <summary>
    ///     Writes the verb's incomplete-model refusal and reports that it fired, or writes nothing and
    ///     reports that the run may go on.
    /// </summary>
    /// <param name="error">The verb's stderr writer — a refusal never reaches stdout.</param>
    /// <param name="diagnostics">The load's own verdict, which decides whether the gate fires.</param>
    /// <param name="allowWorkspaceDiagnostics">Whether the operator opted into the partial model.</param>
    /// <param name="factory">The verb's refusal wording, from <see cref="IncompleteModelGate" />.</param>
    internal static bool Refused(
        TextWriter error,
        WorkspaceDiagnostics diagnostics,
        bool allowWorkspaceDiagnostics,
        Func<WorkspaceDiagnostics, string> factory)
    {
        if (!diagnostics.Gates(allowWorkspaceDiagnostics)) return false;

        LineBlocks.Write(error, factory(diagnostics));
        return true;
    }
}
