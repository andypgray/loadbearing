namespace Zphil.LoadBearing.Cli.Mcp;

/// <summary>
///     The solution + spec this MCP server is bound to for its lifetime, captured once at
///     <c>loadbearing mcp</c> startup. Resolution (solution discovery, spec load, workspace open)
///     happens per tool call, not here, so any resolution error text matches the CLI exactly. A singleton
///     in the host's DI graph, injected into <c>ArchTools</c>.
/// </summary>
/// <remarks>
///     Startup runs discovery once ahead of all that, and what it does with a failure depends on whether
///     <see cref="Solution" /> was given. An argument that does not resolve exits 2 — the operator named
///     something wrong, and a server that answers every call with that error is worse than not starting.
///     With no argument the walk-up is optional by documentation, so its failure only records a reason: the
///     server starts unbound and announces it (<c>McpServerCommand.ResolveBoundSolution</c>). Per-call
///     resolution is what makes that coherent — the tools repeat the same discovery and the same message.
/// </remarks>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from (the server's launch cwd).</param>
internal sealed record McpServerBinding(string? Solution, string? Spec, string WorkingDirectory);
