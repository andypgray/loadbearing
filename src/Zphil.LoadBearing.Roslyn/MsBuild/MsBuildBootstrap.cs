using Microsoft.Build.Locator;

namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Selects an MSBuild instance and registers it both for the current process and (via inherited
///     environment variables) for the Roslyn out-of-process BuildHost.
/// </summary>
/// <remarks>
///     <para>
///         Roslyn's <c>MSBuildWorkspace</c> spawns a separate <c>BuildHost</c> process to load
///         project files. The BuildHost's <c>FindMSBuild</c> calls
///         <see cref="MSBuildLocator.QueryVisualStudioInstances()" /> and picks the highest-version
///         instance. Preview Visual Studio installs (e.g. VS 18+ at time of writing) ship MSBuild
///         assemblies that crash with a <c>TypeInitializationException</c> for
///         <c>Microsoft.Build.Shared.XMakeElements</c> when loading legacy projects (those using
///         the old MSBuild XML namespace <c>http://schemas.microsoft.com/developer/msbuild/2003</c>).
///     </para>
///     <para>
///         Workaround: select a stable VS instance ourselves (via <see cref="VsWhereLocator" />)
///         and propagate the choice to the BuildHost subprocess via <c>VSINSTALLDIR</c> +
///         <c>VSCMD_VER</c> env vars. MSBuildLocator honours these as a synthetic "developer
///         console" instance (see <c>MSBuildLocator.GetDevConsoleInstance</c>). Setting
///         <c>VSCMD_VER=99.0</c> ensures the dev console wins the descending-version sort in the
///         BuildHost. We avoid <c>VisualStudioVersion</c> because that env var is also read by
///         MSBuild itself during project evaluation.
///     </para>
///     <para>
///         <b>Why vswhere instead of MSBuildLocator.QueryVisualStudioInstances:</b> on .NET 5+
///         (which the parent process runs on) MSBuildLocator's query returns only DotNetSdk and
///         DevConsole entries, never VS Setup instances. The BuildHost subprocess sees them
///         because it runs on .NET Framework 4.7.2 — but the parent can't enumerate them through
///         the same API. <c>vswhere.exe</c> works regardless of host runtime.
///     </para>
/// </remarks>
public static class MsBuildBootstrap
{
    private const string DevConsoleVersion = "99.0";

    /// <summary>
    ///     Registers an MSBuild instance and propagates the choice to subprocesses.
    /// </summary>
    /// <remarks>
    ///     Must be called before any Roslyn workspace type loads, otherwise the runtime resolves
    ///     MSBuild assemblies before <see cref="MSBuildLocator" /> has had a chance to point at them.
    ///     Callers guard on <see cref="MSBuildLocator.IsRegistered" /> for idempotency.
    /// </remarks>
    public static MsBuildSelection Initialize()
    {
        return SelectAndRegister();
    }

    /// <summary>
    ///     Registers MSBuild once, idempotently: a no-op returning <see langword="null" /> when
    ///     <see cref="MSBuildLocator.IsRegistered" /> is already true (e.g. tests registered in a
    ///     <c>[ModuleInitializer]</c>). Lets a host gate registration without referencing
    ///     <see cref="MSBuildLocator" /> itself, keeping that dependency out of the CLI's clean path.
    /// </summary>
    public static MsBuildSelection? EnsureInitialized()
    {
        return MSBuildLocator.IsRegistered ? null : SelectAndRegister();
    }

    /// <summary>
    ///     Picks the best Visual Studio instance to use for MSBuild registration: prefer stable
    ///     major versions (16 = VS 2019, 17 = VS 2022) over newer ones, then pick highest within
    ///     the preferred set.
    /// </summary>
    /// <param name="instances">Candidate VS instances, typically the output of <see cref="VsWhereLocator.Query" />.</param>
    /// <returns>The chosen instance, or <see langword="null" /> when <paramref name="instances" /> is empty.</returns>
    /// <remarks>
    ///     The preference for 16/17 is conservative: those are the widely-tested LTS-era versions
    ///     that load both legacy <c>http://schemas.microsoft.com/developer/msbuild/2003</c> projects
    ///     and modern SDK-style ones. Higher major versions (preview/upcoming) have been observed to
    ///     break legacy projects. Users who actually want a newer MSBuild can opt in with
    ///     <see cref="LoadBearingEnvVars.VsInstallPath" />.
    /// </remarks>
    internal static VsInstance? SelectBestInstance(IReadOnlyList<VsInstance> instances)
    {
        if (instances.Count == 0) return null;

        VsInstance? stable = instances
            .Where(i => i.Version.Major is 16 or 17)
            .OrderByDescending(i => i.Version)
            .FirstOrDefault();

        return stable ?? instances.OrderByDescending(i => i.Version).First();
    }

    private static MsBuildSelection SelectAndRegister()
    {
        string? overridePath = Environment.GetEnvironmentVariable(LoadBearingEnvVars.VsInstallPath);
        if (!string.IsNullOrWhiteSpace(overridePath)) return RegisterFromOverride(overridePath);

        if (OperatingSystem.IsWindows())
        {
            VsInstance? best = SelectBestInstance(VsWhereLocator.Query());
            if (best is not null) return RegisterFromVsInstance(best);
        }

        MSBuildLocator.RegisterDefaults();
        return new MsBuildSelection(null, null, "MSBuildLocator default (no Visual Studio install detected via vswhere)");
    }

    private static MsBuildSelection RegisterFromOverride(string overridePath)
    {
        string vsRoot = overridePath.TrimEnd('\\', '/');
        if (!Directory.Exists(vsRoot))
            throw new InvalidOperationException(
                $"{LoadBearingEnvVars.VsInstallPath}='{overridePath}' is not an existing directory. " +
                "Set it to the VS install root (e.g. " +
                "'C:\\Program Files\\Microsoft Visual Studio\\2022\\Community').");

        string msBuildBin = Path.Combine(vsRoot, "MSBuild", "Current", "Bin");
        string msBuildExe = Path.Combine(msBuildBin, "MSBuild.exe");
        if (!File.Exists(msBuildExe))
            throw new InvalidOperationException(
                $"{LoadBearingEnvVars.VsInstallPath}='{overridePath}': MSBuild.exe not found at '{msBuildExe}'. " +
                "Set the env var to the VS install root (the parent of 'MSBuild\\Current\\Bin').");

        ApplyDevConsoleEnv(vsRoot);
        MSBuildLocator.RegisterMSBuildPath(msBuildBin);

        return new MsBuildSelection(msBuildBin, null, $"{LoadBearingEnvVars.VsInstallPath} override");
    }

    private static MsBuildSelection RegisterFromVsInstance(VsInstance instance)
    {
        string msBuildBin = Path.Combine(instance.InstallationPath, "MSBuild", "Current", "Bin");
        if (!File.Exists(Path.Combine(msBuildBin, "MSBuild.exe")))
        {
            // VS install missing MSBuild — extremely unusual but degrade gracefully.
            MSBuildLocator.RegisterDefaults();
            return new MsBuildSelection(null, null, $"MSBuildLocator default (selected '{instance.Name}' had no MSBuild)");
        }

        ApplyDevConsoleEnv(instance.InstallationPath);
        MSBuildLocator.RegisterMSBuildPath(msBuildBin);

        return new MsBuildSelection(
            msBuildBin,
            instance.Version.ToString(),
            $"{instance.Name} ({instance.Version.Major}.{instance.Version.Minor}, via vswhere)");
    }

    /// <summary>
    ///     Sets the env vars that the Roslyn BuildHost subprocess inherits, telling its
    ///     MSBuildLocator to use a synthetic "developer console" instance pointing at our selected
    ///     VS — and giving it a version high enough to win the BuildHost's descending-version sort
    ///     against any other installed VS.
    /// </summary>
    private static void ApplyDevConsoleEnv(string vsRoot)
    {
        Environment.SetEnvironmentVariable("VSINSTALLDIR", vsRoot);
        Environment.SetEnvironmentVariable("VSCMD_VER", DevConsoleVersion);
    }
}