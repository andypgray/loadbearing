using System.ComponentModel;
using ModelContextProtocol.Server;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;

namespace Zphil.LoadBearing.Cli.Mcp.Prompts;

/// <summary>
///     The MCP prompt surface. One prompt, <c>derive_spec</c>, whose body is the embedded
///     <c>derive-spec.md</c> recipe: an honest, tool-backed walkthrough for deriving a LoadBearing
///     architecture spec — the target (<c>Enforce</c>), the debt (<c>Migrate</c>), and the dragons
///     (<c>Freeze</c>) — from an existing codebase, validating every claim with <c>arch_graph</c> and
///     <c>arch_check</c>. The server does not infer the spec: the recipe has the executing agent derive a
///     proposal from evidence, and the human curate and baseline it.
/// </summary>
/// <remarks>
///     The class is deliberately non-static: <c>WithPrompts&lt;ArchPrompts&gt;()</c> takes it as a type
///     argument, and static classes cannot be type arguments. The prompt method is <c>static</c>, so no
///     instance is ever constructed. A <c>string</c>-returning method maps to a single <c>Role.User</c>
///     <c>PromptMessage</c> carrying the markdown body.
/// </remarks>
[McpServerPromptType]
internal sealed class ArchPrompts
{
    internal const string DeriveSpecName = "derive_spec";

    private const string DeriveSpecDescription =
        "Derive an architecture spec (Enforce/Migrate/Freeze postures) from an existing codebase. The server "
        + "does not infer the spec; this recipe guides you: survey with arch_graph, draft candidate rules, "
        + "validate with arch_check, the human curates and baselines.";

    [McpServerPrompt(Name = DeriveSpecName, Title = "Derive an architecture spec")]
    [Description(DeriveSpecDescription)]
    internal static string DeriveSpec()
    {
        return EmbeddedResourceText.Load("derive-spec.md");
    }
}