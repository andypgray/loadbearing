namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     Thrown by <see cref="LazyCaptureReplaySource" /> when a capture that
///     <see cref="Roslyn.Replay.BinlogCaptureStore" />
///     validated as usable nonetheless fails to replay at runtime — a torn copy, a reference that vanished
///     between validation and materialisation. It is caught only at the gate, which prints its
///     <see cref="System.Exception.Message" /> (already the complete notice) behind a <c>warning: </c> prefix,
///     registers MSBuild, and re-runs the command cold. Never reaches <see cref="CliErrorMapper" />: this is a
///     recoverable fallback signal, not a user error, and acquisition precedes all rendering so the re-run
///     produces the run's only output.
/// </summary>
internal sealed class CaptureReplayFailedException(string notice, Exception inner) : Exception(notice, inner);