namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The wire names of the <c>arch_*</c> tools, in one place so the tool declarations and the pipeline
///     stages that key on a tool name cannot drift apart.
/// </summary>
/// <remarks>
///     They live here rather than on the tool class because the pipeline must be able to name a tool without
///     referencing it: the tool type reaches Roslyn workspace types through its runners and is JITted only at
///     the first tool call, after MSBuildLocator has registered. A filter or a truncator that touched it to
///     read a string would pull that quarantined type into a path that runs at startup.
/// </remarks>
internal static class ArchToolNames
{
    /// <summary>The whole-spec check tool.</summary>
    public const string Check = "arch_check";

    /// <summary>The ratchet burndown tool.</summary>
    public const string Status = "arch_status";

    /// <summary>The single-rule explanation tool.</summary>
    public const string Explain = "arch_explain";

    /// <summary>The path → scope-card tool.</summary>
    public const string Context = "arch_context";

    /// <summary>The pre-spec codebase survey tool.</summary>
    public const string Graph = "arch_graph";
}
