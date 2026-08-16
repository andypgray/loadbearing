using Zphil.LoadBearing.Cli.Pipeline;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>
///     What the replay-aware source-selection gate reads off a <c>check</c>, <c>status</c> or <c>graph</c>
///     request: the four inputs the decision turns on, and nothing else.
/// </summary>
/// <remarks>
///     <see cref="CheckRequest" />, <see cref="StatusRequest" /> and <see cref="GraphRequest" /> already
///     carried all four under the same names with the same meanings; this is what types that agreement, so
///     <see cref="MsBuildGate" /> decides over one request rather than the same spread of four arguments
///     repeated per verb. Roslyn-free, like the records themselves, so it crosses the MSBuild gate.
/// </remarks>
internal interface IReplayableRequest
{
    /// <summary>The positional solution argument (a file, a directory, or null for cwd walk-up).</summary>
    string? Solution { get; }

    /// <summary>The directory solution discovery walks up from.</summary>
    string WorkingDirectory { get; }

    /// <summary>The explicit <c>--binlog</c> to replay instead of a design-time build, or null to auto-select.</summary>
    string? Binlog { get; }

    /// <summary>Whether to bypass the persisted extraction cache entirely (no read, no write).</summary>
    bool NoCache { get; }
}
