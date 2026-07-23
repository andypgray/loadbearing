namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     One <c>file:line — message</c> anchor lifted from a fenced code block in a walkthrough doc.
///     A walkthrough quotes <c>check</c> violation output, and each quoted line carries the source
///     file and line the violation was reported at, alongside the extraction message the line was cut
///     from. <see cref="Doc" /> and <see cref="DocLine" /> locate the quote so a failing gate names the
///     exact place the stale anchor lives.
/// </summary>
/// <param name="Doc">The repository-relative path of the doc the anchor was quoted in.</param>
/// <param name="DocLine">The 1-based line in the doc the anchor sits on.</param>
/// <param name="File">The anchor's source path, relative to the doc's example root (e.g. <c>src/…/Foo.cs</c>).</param>
/// <param name="Line">The 1-based source line the quoted violation was reported at.</param>
/// <param name="Message">The extraction message the anchor line carried after the <c>file:line</c>.</param>
internal sealed record SourceAnchor(string Doc, int DocLine, string File, int Line, string Message);