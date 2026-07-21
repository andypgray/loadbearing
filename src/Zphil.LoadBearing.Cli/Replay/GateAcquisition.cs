namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     Which source-selection branch <see cref="MsBuildGate" /> took for the last <c>check</c>/<c>status</c>/
///     <c>graph</c> run. Internal test observable — the enforcement path's output is
///     byte-identical whichever branch runs, so this only ever tells a test which path was chosen, paired
///     with <see cref="Roslyn.WorkspaceLoader.LoadCount" /> for the "no design-time build" pin. Never printed.
/// </summary>
internal enum GateAcquisition
{
    /// <summary>No capture and no explicit binlog (or <c>--no-cache</c>): the plain cold path, MSBuild registered.</summary>
    Cold,

    /// <summary>A stale/unreadable capture: MSBuild registered, its notice printed on workspace acquisition, then cold.</summary>
    NoticeCold,

    /// <summary>An explicit <c>--binlog</c>: replay-first, eagerly (and ingest unless <c>--no-cache</c>); no MSBuild.</summary>
    ExplicitReplay,

    /// <summary>A structurally-valid capture and no explicit binlog: lazy replay on first acquire; no MSBuild.</summary>
    CaptureReplay,

    /// <summary>A valid capture whose copy failed to replay at runtime: its notice printed, then a cold re-run.</summary>
    CaptureReplayFellBackToCold
}