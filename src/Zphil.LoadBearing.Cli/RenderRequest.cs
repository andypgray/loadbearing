namespace Zphil.LoadBearing.Cli;

/// <summary>The parsed inputs to a <c>render</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
internal sealed record RenderRequest(string? Solution, string? Spec, string WorkingDirectory);