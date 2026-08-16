using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Every P/Invoke in this assembly is one of the imports below, and each names a Windows system DLL that
// can resolve nowhere but System32 — so the search path is stated once for the assembly rather than
// twelve times over one file. An import that ever names a DLL the system does not own has to override
// this per method — which is exactly the conversation stating it here is meant to force.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>Which of the two scans observed a retained path.</summary>
internal enum FootprintScan
{
    /// <summary>A mapped view of a section — how a loaded assembly image shows up.</summary>
    MappedView,

    /// <summary>An open kernel handle onto a disk file or a directory.</summary>
    Handle
}

/// <summary>
///     One path a process was observed retaining, with the raw form the OS reported alongside the
///     translated one, so a failure message can be read even when the translation is what broke.
/// </summary>
internal sealed record RetainedPath(FootprintScan Scan, string RawPath, string ResolvedPath);

/// <summary>
///     Reads what files a live process is holding onto, and answers the one question the architecture
///     rules structurally cannot: does this long-lived host retain anything under a given directory?
/// </summary>
/// <remarks>
///     <para>
///         <b>Why two scans.</b> A retained build output shows up in two different places, and neither
///         subsumes the other. A loaded assembly image is a <em>mapped view</em> of a section — there is no
///         file handle in the process's table to find — which is how the spec-DLL lock manifested: a path
///         load pinned the file for the host's whole lifetime and every build of the spec project then
///         failed. An ordinary open file, a log, or the process's own working directory is a
///         <em>handle</em> instead. So this walks the address space for file-backed regions and walks the
///         handle table for disk handles, and reports the union.
///     </para>
///     <para>
///         <b>Failure semantics are deliberately split.</b> Structural failures throw — a process it cannot
///         open, a 32-bit target whose address space it would misread, a buffer that outgrew its cap, or an
///         address-space walk that resolved no image at all ("blind"): each of those would otherwise return
///         an empty list, which reads as "holds nothing" and is the one wrong answer this instrument must
///         never give. Per-item races skip — a handle that closed between the snapshot and the duplicate is
///         by definition not retained. Nothing is retried here; the polling assertion is the retry.
///     </para>
///     <para>
///         Windows x64 only (see <see cref="IsSupported" />): the NT calls and the struct layouts below are
///         both platform-specific, and the property they check is about this repo's own Windows dev loop.
///     </para>
/// </remarks>
internal static class ProcessFileFootprint
{
    private const int ProcessQueryInformation = 0x0400;
    private const int ProcessDupHandle = 0x0040;

    private const int MemCommit = 0x1000;
    private const int MemMapped = 0x40000;
    private const int MemImage = 0x1000000;

    private const int MemorySectionName = 2;
    private const int ProcessHandleInformation = 51;

    private const int StatusSuccess = 0;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    private const int StatusBufferOverflow = unchecked((int)0x80000005);

    private const int DuplicateSameAccess = 0x00000002;
    private const int FileTypeDisk = 0x0001;

    private const int FileNameNormalized = 0x0;
    private const int VolumeNameDos = 0x0;
    private const int VolumeNameNt = 0x2;

    private const int OpenExisting = 3;
    private const int FileFlagBackupSemantics = 0x02000000;
    private const int FileShareAll = 0x1 | 0x2 | 0x4;

    /// <summary>The device the redirector fronts every UNC path with.</summary>
    private const string MultipleUncProviderDevice = @"\Device\Mup";

    private const string ExtendedPathPrefix = @"\\?\";
    private const string ExtendedUncPathPrefix = @"\\?\UNC\";

    /// <summary>Enough for the longest path Windows can produce, doubling from a page.</summary>
    private const int MaxSectionNameBytes = 64 * 1024;

    /// <summary>A ceiling on the handle-table snapshot: a wedged host with millions of handles fails loudly.</summary>
    private const int MaxHandleTableBytes = 16 * 1024 * 1024;

    /// <summary>A backstop on the address-space walk; a real process has a few thousand regions.</summary>
    private const int MaxRegions = 200_000;

    private static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>
    ///     Whether this host can run a scan at all. Off-Windows and under WOW64 the calls below either do not
    ///     exist or would read the wrong address space; callers skip rather than pretend.
    /// </summary>
    internal static bool IsSupported => OperatingSystem.IsWindows() && Environment.Is64BitProcess;

    /// <summary>
    ///     Every distinct path under <paramref name="root" /> that <paramref name="process" /> is holding —
    ///     mapped views first, then open disk handles. An empty result is a real "holds nothing": anything
    ///     that could have blinded the scan throws instead.
    /// </summary>
    internal static IReadOnlyList<RetainedPath> PathsUnder(Process process, string root)
    {
        return Scan(process, root, true);
    }

    /// <summary>
    ///     The handle half of the scan alone — which paths under <paramref name="root" /> this process holds
    ///     open handles on, and therefore what a child it launches may be handed.
    /// </summary>
    /// <remarks>
    ///     A process started with redirected streams is created with handle inheritance on, so it receives a
    ///     copy of every inheritable handle its launcher holds — including the launcher's own
    ///     current-directory handle, which is inside this repository whenever the suite is run from it. Taken
    ///     before the child starts, this is the set that was handed over rather than acquired; see
    ///     <see cref="ExceptInherited" />.
    /// </remarks>
    internal static IReadOnlyList<RetainedPath> HandlePathsUnder(Process process, string root)
    {
        return Scan(process, root, false);
    }

    /// <summary>
    ///     <paramref name="retained" /> minus the handles in <paramref name="inheritedHandles" /> — what the
    ///     process actually acquired.
    /// </summary>
    /// <remarks>
    ///     Only the handle scan is filtered, and only by exact path. A mapped view cannot be inherited: each
    ///     process maps its own, so an image under the repository is always one this process loaded — which
    ///     matters, because a mapped image is exactly how the spec-DLL retention this guards against
    ///     manifested.
    /// </remarks>
    internal static IReadOnlyList<RetainedPath> ExceptInherited(
        IReadOnlyList<RetainedPath> retained, IReadOnlyCollection<RetainedPath> inheritedHandles)
    {
        if (inheritedHandles.Count == 0) return retained;

        var handed = new HashSet<string>(
            inheritedHandles.Select(path => path.ResolvedPath), StringComparer.OrdinalIgnoreCase);

        return retained
            .Where(path => path.Scan != FootprintScan.Handle || !handed.Contains(path.ResolvedPath))
            .ToList();
    }

    private static IReadOnlyList<RetainedPath> Scan(Process process, string root, bool includeMappedViews)
    {
        if (!IsSupported)
            throw new InvalidOperationException(
                "ProcessFileFootprint needs a 64-bit Windows host; guard callers with IsSupported.");

        if (process.HasExited)
            throw new InvalidOperationException(
                $"Process {process.Id} has already exited, so it can hold nothing — a scan of it would report "
                + "an empty footprint for the wrong reason. Assert the process is alive first.");

        string canonicalRoot = Canonicalize(root);
        Dictionary<string, string> deviceMap = BuildDeviceMap();

        IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessDupHandle, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"OpenProcess failed for PID {process.Id}: the footprint scan cannot see the process at all.");

        try
        {
            RequireNativeBitness(handle, process.Id);

            var found = new List<RetainedPath>();
            if (includeMappedViews) ScanMappedViews(handle, deviceMap, canonicalRoot, found);
            ScanHandleTable(handle, deviceMap, canonicalRoot, found);
            return found;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    ///     The final, fully-resolved spelling of <paramref name="path" /> — 8.3 short names expanded,
    ///     junctions and symlinks followed, the volume named by its drive letter — so a comparison against
    ///     it cannot be dodged by an alternative spelling of the same directory.
    /// </summary>
    internal static string Canonicalize(string path)
    {
        IntPtr file = CreateFile(
            path, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (file == InvalidHandleValue)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), $"Could not open '{path}' to resolve its canonical spelling.");

        try
        {
            string resolved = FinalPath(file, VolumeNameDos)
                              ?? throw new Win32Exception(
                                  Marshal.GetLastWin32Error(),
                                  $"GetFinalPathNameByHandle failed for '{path}'.");
            return Path.TrimEndingDirectorySeparator(StripExtendedPrefix(resolved));
        }
        finally
        {
            CloseHandle(file);
        }
    }

    // Walks the target's committed address space region by region, asking the memory manager for the name of
    // the section behind each file-backed one. Regions with no name (pagefile-backed) are skipped: the
    // absence of a name is the absence of a retained path, not a failed read.
    private static void ScanMappedViews(
        IntPtr process, Dictionary<string, string> deviceMap, string canonicalRoot, List<RetainedPath> found)
    {
        var infoSize = (IntPtr)Marshal.SizeOf<MemoryBasicInformation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedImages = 0;
        IntPtr address = IntPtr.Zero;

        for (var region = 0; region < MaxRegions; region++)
        {
            if (VirtualQueryEx(process, address, out MemoryBasicInformation info, infoSize) == IntPtr.Zero) break;

            var regionSize = info.RegionSize.ToInt64();
            if (regionSize <= 0) break;

            bool fileBacked = (info.Type & (MemMapped | MemImage)) != 0 && info.State == MemCommit;
            if (fileBacked && SectionName(process, info.BaseAddress) is { } raw && seen.Add(raw))
            {
                string? resolved = TranslateDevicePath(raw, deviceMap);
                if (resolved is not null)
                {
                    if ((info.Type & MemImage) != 0) resolvedImages++;
                    if (IsUnder(resolved, canonicalRoot))
                        found.Add(new RetainedPath(FootprintScan.MappedView, raw, resolved));
                }
            }

            long next = info.BaseAddress.ToInt64() + regionSize;
            if (next <= address.ToInt64()) break; // no forward progress — stop rather than spin
            address = new IntPtr(next);
        }

        // Every Windows process maps ntdll, so a walk that resolved no image at all did not see this
        // process's memory: an empty result would be a false all-clear. Fail instead.
        if (resolvedImages == 0)
            throw new InvalidOperationException(
                "The mapped-view scan resolved no image path at all, so it was blind to this process's "
                + "address space (every Windows process maps ntdll). Treat the result as unknown, not clean.");
    }

    // Snapshots the target's handle table, then duplicates each entry to ask what it points at. GetFileType
    // is the gate: it is documented safe on any handle type, where an object-name query on a synchronous
    // pipe can block forever — and a pipe is exactly what a stdio server's own handles are.
    private static void ScanHandleTable(
        IntPtr process, Dictionary<string, string> deviceMap, string canonicalRoot, List<RetainedPath> found)
    {
        int size = 64 * 1024;
        while (true)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryInformationProcess(
                    process, ProcessHandleInformation, buffer, size, out int _);

                if (status is StatusInfoLengthMismatch or StatusBufferTooSmall or StatusBufferOverflow)
                {
                    if (size >= MaxHandleTableBytes)
                        throw new InvalidOperationException(
                            $"The handle table did not fit in {MaxHandleTableBytes / (1024 * 1024)} MB; the scan "
                            + "cannot report a footprint it never read.");

                    size = Math.Min(size * 2, MaxHandleTableBytes);
                    continue;
                }

                if (status != StatusSuccess)
                    throw new InvalidOperationException(
                        $"NtQueryInformationProcess(ProcessHandleInformation) failed with 0x{status:X8}, so the "
                        + "handle table was not read.");

                CollectDiskHandles(process, buffer, deviceMap, canonicalRoot, found);
                return;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static void CollectDiskHandles(
        IntPtr process,
        IntPtr snapshot,
        Dictionary<string, string> deviceMap,
        string canonicalRoot,
        List<RetainedPath> found)
    {
        var count = Marshal.PtrToStructure<ProcessHandleSnapshotInformation>(snapshot)
            .NumberOfHandles.ToInt64();
        int entrySize = Marshal.SizeOf<ProcessHandleTableEntryInfo>();
        int headerSize = Marshal.SizeOf<ProcessHandleSnapshotInformation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (long index = 0; index < count; index++)
        {
            var entryAddress = new IntPtr(snapshot.ToInt64() + headerSize + index * entrySize);
            var entry = Marshal.PtrToStructure<ProcessHandleTableEntryInfo>(entryAddress);

            if (DiskHandlePath(process, entry.HandleValue, deviceMap) is not { } resolved) continue;
            if (!seen.Add(resolved.ResolvedPath)) continue;
            if (IsUnder(resolved.ResolvedPath, canonicalRoot)) found.Add(resolved);
        }
    }

    private static RetainedPath? DiskHandlePath(
        IntPtr process, IntPtr handleValue, Dictionary<string, string> deviceMap)
    {
        if (!DuplicateHandle(
                process, handleValue, GetCurrentProcess(), out IntPtr duplicate, 0, false, DuplicateSameAccess))
            // The handle closed between the snapshot and now, or it is not duplicable: either way it is not a
            // handle this process is holding onto, which is the only thing being counted.
            return null;

        try
        {
            if (GetFileType(duplicate) != FileTypeDisk) return null;

            if (FinalPath(duplicate, VolumeNameDos) is { } dos)
                return new RetainedPath(FootprintScan.Handle, dos, StripExtendedPrefix(dos));

            // A handle whose volume has no drive letter (a mounted volume, a UNC share) still resolves in
            // device form, which the device map translates.
            if (FinalPath(duplicate, VolumeNameNt) is { } nt
                && TranslateDevicePath(nt, deviceMap) is { } translated)
                return new RetainedPath(FootprintScan.Handle, nt, translated);

            return null;
        }
        finally
        {
            CloseHandle(duplicate);
        }
    }

    private static string? SectionName(IntPtr process, IntPtr baseAddress)
    {
        for (var size = 1024; size <= MaxSectionNameBytes; size *= 2)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryVirtualMemory(
                    process, baseAddress, MemorySectionName, buffer, size, out IntPtr _);

                if (status is StatusBufferOverflow or StatusInfoLengthMismatch or StatusBufferTooSmall) continue;
                if (status != StatusSuccess) return null; // no name behind this region (pagefile-backed)

                var name = Marshal.PtrToStructure<UnicodeString>(buffer);
                return name.Length == 0 ? null : Marshal.PtrToStringUni(name.Buffer, name.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException(
            $"A section name did not fit in {MaxSectionNameBytes} bytes; the mapped-view scan cannot report a "
            + "region it failed to name.");
    }

    private static string? FinalPath(IntPtr file, int volumeNameFlag)
    {
        var buffer = new StringBuilder(1024);
        int length = GetFinalPathNameByHandle(
            file, buffer, buffer.Capacity, FileNameNormalized | volumeNameFlag);

        if (length > buffer.Capacity)
        {
            buffer = new StringBuilder(length + 1);
            length = GetFinalPathNameByHandle(
                file, buffer, buffer.Capacity, FileNameNormalized | volumeNameFlag);
        }

        return length == 0 ? null : buffer.ToString();
    }

    // Drive letters resolved once per scan: every mapped view names its volume in device form, and there can
    // be thousands of them.
    private static Dictionary<string, string> BuildDeviceMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // A UNC path arrives as \Device\Mup\server\share; prefixing the remainder with one more
            // separator restores the \\server\share spelling.
            [MultipleUncProviderDevice] = @"\"
        };

        var target = new StringBuilder(1024);
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var drive = $"{letter}:";
            if (QueryDosDevice(drive, target, target.Capacity) == 0) continue;

            map[target.ToString()] = drive;
        }

        return map;
    }

    private static string? TranslateDevicePath(string devicePath, Dictionary<string, string> deviceMap)
    {
        foreach ((string device, string dosRoot) in deviceMap)
        {
            if (!devicePath.StartsWith(device, StringComparison.OrdinalIgnoreCase)) continue;
            if (devicePath.Length <= device.Length || devicePath[device.Length] != '\\') continue;

            return dosRoot + devicePath[device.Length..];
        }

        return null;
    }

    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(ExtendedUncPathPrefix, StringComparison.Ordinal))
            return @"\\" + path[ExtendedUncPathPrefix.Length..];

        return path.StartsWith(ExtendedPathPrefix, StringComparison.Ordinal)
            ? path[ExtendedPathPrefix.Length..]
            : path;
    }

    // Separator-guarded so a sibling directory sharing a name prefix ("...\build-cache") is not read as
    // being inside the root ("...\build").
    private static bool IsUnder(string path, string canonicalRoot)
    {
        if (!path.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Length == canonicalRoot.Length) return true;

        char next = path[canonicalRoot.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }

    private static void RequireNativeBitness(IntPtr process, int processId)
    {
        if (!IsWow64Process(process, out bool isWow64))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), $"IsWow64Process failed for PID {processId}.");

        if (isWow64)
            throw new InvalidOperationException(
                $"PID {processId} is a 32-bit (WOW64) process; this scan reads a 64-bit address space and "
                + "would misreport it.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr process, out bool isWow64);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualQueryEx(
        IntPtr process, IntPtr address, out MemoryBasicInformation buffer, IntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess,
        IntPtr sourceHandle,
        IntPtr targetProcess,
        out IntPtr targetHandle,
        int desiredAccess,
        bool inheritHandle,
        int options);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetFileType(IntPtr file);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetFinalPathNameByHandle(IntPtr file, StringBuilder path, int pathLength, int flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int QueryDosDevice(string deviceName, StringBuilder targetPath, int maxCharacters);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string fileName,
        int desiredAccess,
        int shareMode,
        IntPtr securityAttributes,
        int creationDisposition,
        int flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryVirtualMemory(
        IntPtr process,
        IntPtr baseAddress,
        int memoryInformationClass,
        IntPtr buffer,
        IntPtr bufferLength,
        out IntPtr returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public int AllocationProtect;
        public int Alignment1;
        public IntPtr RegionSize;
        public int State;
        public int Protect;
        public int Type;
        public int Alignment2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessHandleSnapshotInformation
    {
        public IntPtr NumberOfHandles;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessHandleTableEntryInfo
    {
        public IntPtr HandleValue;
        public IntPtr HandleCount;
        public IntPtr PointerCount;
        public int GrantedAccess;
        public int ObjectTypeIndex;
        public int HandleAttributes;
        public int Reserved;
    }
}
