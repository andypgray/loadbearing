namespace Zphil.LoadBearing.Cli;

/// <summary>The parsed inputs to an <c>arch_context</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="Path">The query path (a file or directory, absolute or solution-relative) to find frozen-scope cards for.</param>
/// <param name="Solution">The bound solution (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
internal sealed record ContextRequest(string Path, string? Solution, string? Spec, string WorkingDirectory);