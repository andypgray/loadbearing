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
///         instance. An early VS 18 preview (observed 2026-07) shipped MSBuild assemblies that
///         crashed with a <c>TypeInitializationException</c> for
///         <c>Microsoft.Build.Shared.XMakeElements</c> when loading legacy projects (those using
///         the old MSBuild XML namespace <c>http://schemas.microsoft.com/developer/msbuild/2003</c>).
///         That crash no longer reproduces — VS 18.6 loads such a project cleanly — so the 16/17
///         preference in <see cref="SelectBestInstance" /> is caution about a moving target rather
///         than a workaround for a known break, and <see cref="LastSelection" /> exists so a machine
///         that ends up somewhere else says so instead of failing as a bare exit code.
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
///         <b>Why vswhere instead of MSBuildLocator.QueryVisualStudioInstances:</b> see
///         <see cref="VsWhereLocator" />. The asymmetry that matters here is that the BuildHost
///         subprocess <em>does</em> see VS Setup instances through that API, because it runs on
///         .NET Framework 4.7.2 — the parent process cannot.
///     </para>
/// </remarks>
public static class MsBuildBootstrap
{
    private const string DevConsoleVersion = "99.0";

    // The layout every Visual Studio install puts MSBuild under, as the two sentences that quote it spell it:
    // Windows separators, because it is prose about a VS install root. The probe itself lives in
    // TryRegisterFromVsRoot, and a reader sent to a directory the code never probed is the drift this exists
    // to stop.
    private const string MsBuildBinLayout = @"MSBuild\Current\Bin";

    // The tail an instance taken outside the preferred VS 16/17 set carries in its description. The
    // preferred arm needs no counterpart: an ordinary pick is the unremarkable case, and a note on every
    // line would stop the exceptional one from reading as exceptional.
    private const string OutsideEnvelopeNote = " — outside the tested VS 2019/2022 envelope";

    /// <summary>
    ///     The <see cref="MsBuildSelection.Source" /> of the most recent registration: which MSBuild this
    ///     process is running on and why that one, on one line. <see langword="null" /> until something
    ///     registers. Exists so "which MSBuild did you pick" is answerable on a machine nobody can attach
    ///     a debugger to.
    /// </summary>
    /// <remarks>
    ///     A <see langword="string" /> rather than the <see cref="MsBuildSelection" /> itself, deliberately:
    ///     this namespace is quarantined behind <see cref="MsBuildBootstrap" />, so a caller outside it may
    ///     name this type and nothing else here.
    /// </remarks>
    internal static string? LastSelection { get; private set; }

    /// <summary>
    ///     The MSBuild-selection line that rides along with workspace diagnostics. A project that fails to
    ///     load is nearly always a question about which MSBuild opened it, so the line that answers lives
    ///     beside the state it reads. A null selection means nothing registered MSBuild at all, which for a
    ///     caller that just opened a workspace is itself worth saying.
    /// </summary>
    internal static string SelectionNote()
    {
        string selection = LastSelection ?? "not registered by this process";
        return $"MSBuild for this run: {selection}. Set {LoadBearingEnvVars.VsInstallPath} to a Visual Studio "
               + $"install root (the parent of {MsBuildBinLayout}) to select a different MSBuild.";
    }

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
    ///     and modern SDK-style ones. A newer major is taken only when no 16/17 is installed — the
    ///     shape of a runner image that ships VS 2026 alone — and the selection then says so rather
    ///     than reading as an ordinary pick (see <see cref="DescribeSelection" /> and
    ///     <see cref="LastSelection" />). The one crash that motivated the preference was an early
    ///     VS 18 preview and does not reproduce on VS 18.6, so what is kept here is caution, not a
    ///     workaround. Users who actually want a newer MSBuild can opt in with
    ///     <see cref="LoadBearingEnvVars.VsInstallPath" />.
    /// </remarks>
    internal static VsInstance? SelectBestInstance(IReadOnlyList<VsInstance> instances)
    {
        if (instances.Count == 0) return null;

        VsInstance? stable = instances
            .Where(IsPreferredMajor)
            .OrderByDescending(i => i.Version)
            .FirstOrDefault();

        return stable ?? instances.OrderByDescending(i => i.Version).First();
    }

    /// <summary>
    ///     The one-line description of a chosen instance, naming the version and — when the instance came
    ///     from outside the preferred set — that it did. Pure: this is the reporting half of
    ///     <see cref="RegisterFromVsInstance" />, split out so it can be pinned on a machine with no
    ///     Visual Studio installed.
    /// </summary>
    internal static string DescribeSelection(VsInstance instance)
    {
        string envelope = IsPreferredMajor(instance) ? string.Empty : OutsideEnvelopeNote;
        return $"{instance.Name} ({instance.Version.Major}.{instance.Version.Minor}, via vswhere){envelope}";
    }

    // The tested envelope: VS 2019 and VS 2022. Shared by the selection and its description so the two
    // cannot drift into describing an instance as preferred that the selection took as a fallback.
    private static bool IsPreferredMajor(VsInstance instance)
    {
        return instance.Version.Major is 16 or 17;
    }

    // Registration, plus the publication of what it chose. Every arm below returns a selection, so
    // recording it once here is what keeps LastSelection total rather than a thing each arm remembers.
    private static MsBuildSelection SelectAndRegister()
    {
        MsBuildSelection selection = SelectAndRegisterCore();
        LastSelection = selection.Source;
        return selection;
    }

    private static MsBuildSelection SelectAndRegisterCore()
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

        (string msBuildBin, string msBuildExe, bool registered) = TryRegisterFromVsRoot(vsRoot);
        if (!registered)
            throw new InvalidOperationException(
                $"{LoadBearingEnvVars.VsInstallPath}='{overridePath}': MSBuild.exe not found at '{msBuildExe}'. " +
                $"Set the env var to the VS install root (the parent of '{MsBuildBinLayout}').");

        return new MsBuildSelection(msBuildBin, null, $"{LoadBearingEnvVars.VsInstallPath} override ('{vsRoot}')");
    }

    private static MsBuildSelection RegisterFromVsInstance(VsInstance instance)
    {
        (string msBuildBin, _, bool registered) = TryRegisterFromVsRoot(instance.InstallationPath);
        if (!registered)
        {
            // VS install missing MSBuild — extremely unusual but degrade gracefully.
            MSBuildLocator.RegisterDefaults();
            return new MsBuildSelection(
                null, null, $"MSBuildLocator default (selected '{instance.Name}' had no MSBuild at '{msBuildBin}')");
        }

        return new MsBuildSelection(msBuildBin, instance.Version.ToString(), DescribeSelection(instance));
    }

    // The registration sequence both arms run: find the bin directory under the VS root, prove MSBuild.exe is
    // in it, and only then point this process and the BuildHost subprocess at it. A missed probe registers
    // nothing and hands both probed paths back, because that is the whole of what the two arms disagree
    // about — an operator who named this root has to be told it was wrong, while a VS install we picked
    // ourselves degrades to MSBuildLocator's defaults rather than failing a run over it.
    private static (string MsBuildBin, string MsBuildExe, bool Registered) TryRegisterFromVsRoot(string vsRoot)
    {
        string msBuildBin = Path.Combine(vsRoot, "MSBuild", "Current", "Bin");
        string msBuildExe = Path.Combine(msBuildBin, "MSBuild.exe");
        if (!File.Exists(msBuildExe)) return (msBuildBin, msBuildExe, false);

        ApplyDevConsoleEnv(vsRoot);
        MSBuildLocator.RegisterMSBuildPath(msBuildBin);

        return (msBuildBin, msBuildExe, true);
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
