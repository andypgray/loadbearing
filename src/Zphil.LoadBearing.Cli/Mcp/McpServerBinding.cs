namespace Zphil.LoadBearing.Cli.Mcp;

/// <summary>
///     The solution + spec this MCP server is bound to for its lifetime, captured once at
///     <c>loadbearing mcp</c> startup. Resolution (solution discovery, spec load, workspace open)
///     happens per tool call, not here — so startup never fails and any resolution error text matches
///     the CLI exactly. A singleton in the host's DI graph, injected into <c>ArchTools</c>.
/// </summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from (the server's launch cwd).</param>
internal sealed record McpServerBinding(string? Solution, string? Spec, string WorkingDirectory);