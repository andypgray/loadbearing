using Microsoft.Build.Locator;
using Zphil.LoadBearing.Roslyn.Hosting;

namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Chooses the MSBuild a run will use and registers it, both for this process and for the separate build-host
///     process Roslyn spawns to read project files. This is the first thing a host does: call
///     <see cref="EnsureInitialized" /> (or <see cref="Initialize" />) before anything touches a Roslyn workspace type,
///     then load a solution with <see cref="WorkspaceLoader" /> or a <see cref="WorkspaceSession" />. Left until later,
///     the runtime resolves MSBuild assemblies before anything has said which ones to resolve. On Windows a stable
///     Visual Studio install is preferred, because the build-host process needs one to load a non-SDK project; set
///     <c>LOADBEARING_VS_INSTALL_PATH</c> to a Visual Studio install root (the parent of <c>MSBuild\Current\Bin</c>) to
///     choose a different one.
/// </summary>
// The two processes are pointed at MSBuild separately, and not at the same one (see
// RegisterEngineForThisProcess). This one gets an engine through
// MSBuildLocator; the BuildHost subprocess inherits VSINSTALLDIR + VSCMD_VER, which its own
// MSBuildLocator honours as a synthetic "developer console" instance, and VSCMD_VER=99.0 is what wins
// that process's descending-version sort against any other installed VS. VisualStudioVersion must not
// be used for this: MSBuild itself reads that variable during project evaluation.
public static class MsBuildBootstrap
{
    private const string DevConsoleVersion = "99.0";

    // The layout every Visual Studio install puts MSBuild under — quoted by the guidance sentences and
    // probed by TryRegisterFromVsRoot, so the directory a reader is sent to is the directory the code
    // probed. Windows separators: every VS install root is a Windows path.
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
    ///     Selects an MSBuild instance, registers an engine for this process, and points the build-host process at the
    ///     Visual Studio install behind the selection. Call it before any Roslyn workspace type loads, and only where
    ///     this host owns registration outright; where something else may have registered MSBuild already — a test
    ///     project that registers in a module initializer, for instance — call <see cref="EnsureInitialized" />
    ///     instead. Returns what was chosen, whose <see cref="MsBuildSelection.Source" /> is the one line worth
    ///     logging. A <c>LOADBEARING_VS_INSTALL_PATH</c> that is not an existing directory, or that holds no
    ///     <c>MSBuild.exe</c> under <c>MSBuild\Current\Bin</c>, fails with a message naming the variable and the path
    ///     that was probed.
    /// </summary>
    public static MsBuildSelection Initialize()
    {
        return SelectAndRegister();
    }

    /// <summary>
    ///     Registers MSBuild if nothing has yet and returns what was chosen, or returns <see langword="null" /> when
    ///     MSBuild is already registered. Safe to call from every entry point of a host, which is why it is the form to
    ///     prefer over <see cref="Initialize" />. A null answer is not a failure: it says the choice was already made,
    ///     and <see cref="MsBuildSelection.Source" /> is only available from the call that made it. A
    ///     <c>LOADBEARING_VS_INSTALL_PATH</c> that is not an existing directory, or that holds no <c>MSBuild.exe</c>
    ///     under <c>MSBuild\Current\Bin</c>, fails with a message naming the variable and the path that was probed.
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
    ///     The preference for 16/17 is conservative — those are the widely-tested LTS-era versions
    ///     that load both legacy <c>http://schemas.microsoft.com/developer/msbuild/2003</c> projects
    ///     and modern SDK-style ones — and it is caution about a moving target, not a workaround for
    ///     a known break. A newer major is taken only when no 16/17 is installed — the shape of a
    ///     runner image that ships VS 2026 alone — and the selection then says so rather than
    ///     reading as an ordinary pick (see <see cref="DescribeSelection" /> and
    ///     <see cref="LastSelection" />). Users who actually want a newer MSBuild can opt in with
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
    // in it, and only then point the BuildHost subprocess at it and give this process an engine. A missed
    // probe registers nothing and hands both probed paths back, because that is the whole of what the two
    // arms disagree about — an operator who named this root has to be told it was wrong, while a VS install
    // we picked ourselves degrades to MSBuildLocator's defaults rather than failing a run over it.
    private static (string MsBuildBin, string MsBuildExe, bool Registered) TryRegisterFromVsRoot(string vsRoot)
    {
        string msBuildBin = Path.Combine(vsRoot, MsBuildBinLayout);
        string msBuildExe = Path.Combine(msBuildBin, "MSBuild.exe");
        if (!File.Exists(msBuildExe)) return (msBuildBin, msBuildExe, false);

        ApplyDevConsoleEnv(vsRoot);
        RegisterEngineForThisProcess(msBuildBin);

        return (msBuildBin, msBuildExe, true);
    }

    // Which MSBuild the BuildHost opens projects with and which one runs inside this process are two
    // choices, and only the first of them is about Visual Studio: ApplyDevConsoleEnv above has already
    // handed the subprocess the VS instance that has to read a non-SDK project. In-process the engine has
    // to be a .NET build of MSBuild or it will not run at all — Visual Studio ships a .NET Framework one,
    // whose SDK resolver cannot be loaded here, and this process evaluates project files directly. So the
    // .NET SDK's MSBuild is registered, which is what every non-Windows run already gets. A machine with no
    // SDK at all falls back to the VS path: nothing here can then evaluate, and an unevaluated project
    // records no artifact facts, which the model already knows how to be missing.
    private static void RegisterEngineForThisProcess(string msBuildBin)
    {
        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException)
        {
            MSBuildLocator.RegisterMSBuildPath(msBuildBin);
        }
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
