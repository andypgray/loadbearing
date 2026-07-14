using System.Runtime.CompilerServices;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The MSBuildLocator quarantine. Neither <c>Program</c> nor the command wiring references a
///     Roslyn/MSBuild type; the check action calls in here. <see cref="MethodImplOptions.NoInlining" />
///     keeps the JIT from resolving <see cref="CheckRunner" /> (and through it MSBuildWorkspace) until
///     after registration has run. In tests MSBuild is already registered
///     by a <c>[ModuleInitializer]</c>, so <see cref="MsBuildBootstrap.EnsureInitialized" /> is a no-op.
/// </summary>
internal static class MsBuildGate
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunCheckAsync(CheckRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeCheckAsync(request, output, error, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunExplainAsync(ExplainRequest request, TextWriter output, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeExplainAsync(request, output, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunRenderAsync(RenderRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeRenderAsync(request, output, error, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunBaselineAsync(BaselineRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeBaselineAsync(request, output, error, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunStatusAsync(StatusRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeStatusAsync(request, output, error, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunGraphAsync(GraphRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeGraphAsync(request, output, error, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuildRegistered()
    {
        MsBuildBootstrap.EnsureInitialized();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeCheckAsync(CheckRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return new CheckRunner(output, error).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeExplainAsync(ExplainRequest request, TextWriter output, CancellationToken ct)
    {
        return new ExplainRunner(output).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeRenderAsync(RenderRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return new RenderRunner(output, error).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeBaselineAsync(BaselineRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return new BaselineRunner(output, error).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeStatusAsync(StatusRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return new StatusRunner(output, error).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeGraphAsync(GraphRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return new GraphRunner(output, error).RunAsync(request, ct);
    }
}