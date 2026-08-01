using System.Runtime.CompilerServices;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A private copy of a fixture solution — the substrate for the mutation tests
///     (<c>baseline --init</c>/<c>--accept-reductions</c>, tamper, file move) and for any test that needs to
///     build a fixture rather than only read it. The shared <see cref="WorkspaceFixture" /> output tree is
///     read-only and shared across the assembly, so a test that edits fixture files or baselines must run
///     against its own copy.
/// </summary>
/// <remarks>
///     <para>
///         <b>One copy per test class, reset between tests — not one per test.</b> The copy is leased, keyed
///         on the calling source file (one type per file, so that is the test class). The first test in a
///         class pays the copy and the <c>dotnet restore</c>; each later test in that class gets the same
///         directory with its tree reset to pristine. Every caller is in the
///         <see cref="SerialCollection">"Serial"</see> collection, so two tests can never hold one lease at
///         once.
///     </para>
///     <para>
///         <b>Why the path must be stable, not merely private.</b> A fresh directory per test cost far more
///         than its restore: <c>CacheLocations</c> keys the extraction cache on a hash of the canonical
///         solution path, so a new path is a guaranteed cold miss. Measured on the MyApp fixture, a
///         <c>check</c> costs ~7.0s cold against ~0.65s warm, on top of a ~2.1s restore. Holding the path
///         steady across a class turns every test after the first into the warm case.
///     </para>
///     <para>
///         <b>What the reset guarantees.</b> Every source file is returned to its fixture content and
///         anything a previous test created (baselines, probe sources) is deleted, so a test sees the same
///         tree whether it ran first or last. <c>bin</c>/<c>obj</c> are left alone — that is what keeps the
///         restore valid — and a re-restore happens only if a project or solution file actually changed.
///     </para>
/// </remarks>
internal sealed class TempFixtureWorkspace : IDisposable
{
    private const string DefaultFixtureDirectory = "TestSolutions/MyApp";

    private const string DefaultSolutionFileName = "MyApp.sln";

    private static readonly Lock LeaseGate = new();

    // Lease key -> the directory that key owns. Held for the process; reset, never deleted, between tests.
    private static readonly Dictionary<string, string> Leases = new(StringComparer.Ordinal);

    private static readonly HashSet<string> HeldLeases = new(StringComparer.Ordinal);

    private readonly string? _leaseKey;

    private readonly string _root;

    /// <param name="fixtureDirectory">
    ///     The fixture's directory, '/'-separated and relative to the test output's <c>Fixtures/</c> root.
    /// </param>
    /// <param name="solutionFileName">The solution file's name inside that directory.</param>
    /// <param name="restore">Whether to <c>dotnet restore</c> the copy before handing it back.</param>
    /// <param name="callerFilePath">
    ///     Compiler-supplied; never passed explicitly. The calling source file is the lease key, which by the
    ///     repository's one-type-per-file rule is exactly "the test class".
    /// </param>
    public TempFixtureWorkspace(
        string fixtureDirectory = DefaultFixtureDirectory,
        string solutionFileName = DefaultSolutionFileName,
        bool restore = true,
        [CallerFilePath] string callerFilePath = "")
    {
        string source = SourceDirectory(fixtureDirectory);
        var key = $"{Path.GetFileNameWithoutExtension(callerFilePath)}-{fixtureDirectory.Replace('/', '-')}";

        if (TryAcquire(key))
        {
            string leased = LeasedDirectory(key);
            if (TrySyncTree(source, leased, out bool projectsChanged))
            {
                _leaseKey = key;
                _root = leased;
                SolutionPath = Path.Combine(_root, solutionFileName);
                if (restore && projectsChanged) FixtureRestorer.Restore(SolutionPath);
                return;
            }

            // The reset could not complete — a fixture file still held by a previous test's workspace. Give
            // the lease up and fall through to a private copy: slower, but never a spurious failure.
            Release(key);
        }

        // Either the lease is already held (one test holding two copies at once) or its tree could not be
        // reset. Correctness first: hand back a private directory of its own. It costs a restore, which the
        // suite's timing would expose.
        _root = PrivateCopy(source);
        SolutionPath = Path.Combine(_root, solutionFileName);
        if (restore) FixtureRestorer.Restore(SolutionPath);
    }

    // The dedicated path. Takes the resolved source as a DirectoryInfo purely so it cannot collide with the
    // leased constructor's (string, string, bool) call shape, which real call sites already use.
    private TempFixtureWorkspace(DirectoryInfo source, string solutionFileName, bool restore)
    {
        _root = PrivateCopy(source.FullName);
        SolutionPath = Path.Combine(_root, solutionFileName);
        if (restore) FixtureRestorer.Restore(SolutionPath);
    }

    /// <summary>Absolute path to the copied solution file.</summary>
    public string SolutionPath { get; }

    public void Dispose()
    {
        if (_leaseKey is not null)
        {
            // Leased: the tree stays for the next test in this class, which resets it on acquire.
            Release(_leaseKey);
            return;
        }

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // best-effort: a temp tree with a locked MSBuild handle survives to TestTempRoot's sweep.
        }
    }

    /// <summary>
    ///     A copy no other test shares, for a caller that must own its tree for the whole run rather than hand
    ///     it back between tests (see <see cref="BinlogFixtureWorkspace" />, whose binlog bakes in these
    ///     absolute paths, and which mutates the tree deliberately between tests).
    /// </summary>
    internal static TempFixtureWorkspace Dedicated(
        string fixtureDirectory = DefaultFixtureDirectory,
        string solutionFileName = DefaultSolutionFileName,
        bool restore = true)
    {
        return new TempFixtureWorkspace(
            new DirectoryInfo(SourceDirectory(fixtureDirectory)), solutionFileName, restore);
    }

    /// <summary>Absolute path to a file or directory inside the copy, from solution-relative segments.</summary>
    public string PathOf(params string[] relativeSegments)
    {
        var segments = new List<string> { _root };
        segments.AddRange(relativeSegments);
        return Path.Combine(segments.ToArray());
    }

    private static string SourceDirectory(string fixtureDirectory)
    {
        var segments = new List<string> { AppContext.BaseDirectory, "Fixtures" };
        segments.AddRange(fixtureDirectory.Split('/'));
        return Path.Combine(segments.ToArray());
    }

    private static bool TryAcquire(string key)
    {
        lock (LeaseGate)
        {
            return HeldLeases.Add(key);
        }
    }

    private static void Release(string key)
    {
        lock (LeaseGate)
        {
            HeldLeases.Remove(key);
        }
    }

    /// <summary>
    ///     <see cref="SyncTree" />, reporting failure rather than throwing. A fixture file can still be held
    ///     when the reset runs (a workspace the previous test left undisposed), and a locked file must cost
    ///     the caller a private copy, never a spurious test failure.
    /// </summary>
    private static bool TrySyncTree(string source, string destination, out bool projectsChanged)
    {
        try
        {
            projectsChanged = SyncTree(source, destination);
            return true;
        }
        catch (IOException)
        {
            projectsChanged = false;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            projectsChanged = false;
            return false;
        }
    }

    private static string LeasedDirectory(string key)
    {
        lock (LeaseGate)
        {
            if (Leases.TryGetValue(key, out string? existing)) return existing;

            string created = Path.Combine(TestTempRoot.For("fixtures"), key);
            Directory.CreateDirectory(created);
            Leases[key] = created;
            return created;
        }
    }

    private static string PrivateCopy(string source)
    {
        string root = Path.Combine(TestTempRoot.For("fixtures"), Guid.NewGuid().ToString("N"));
        CopyTree(source, root);
        return root;
    }

    /// <summary>
    ///     Brings <paramref name="destination" /> back to <paramref name="source" />'s content: restores every
    ///     changed file, deletes everything a previous test added, and leaves <c>bin</c>/<c>obj</c> untouched.
    ///     Returns whether a project or solution file changed, which is the only thing a restore depends on.
    /// </summary>
    private static bool SyncTree(string source, string destination)
    {
        var expected = new HashSet<string>(PathComparison.Comparer);
        var projectsChanged = false;

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file, source)) continue;

            string target = destination + file.Substring(source.Length);
            expected.Add(target);
            if (SameContent(file, target)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
            projectsChanged |= IsProjectFile(target);
        }

        foreach (string file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file, destination) || expected.Contains(file)) continue;

            File.Delete(file);
            projectsChanged |= IsProjectFile(file);
        }

        return projectsChanged;
    }

    private static bool IsProjectFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameContent(string source, string target)
    {
        var targetInfo = new FileInfo(target);
        if (!targetInfo.Exists) return false;
        if (targetInfo.Length != new FileInfo(source).Length) return false;

        // Fixture files are small (the largest MyApp source is a few KB), so a full compare beats hashing.
        return File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target));
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file, source)) continue;

            string target = destination + file.Substring(source.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static bool IsBuildArtifact(string path, string source)
    {
        string relative = path.Substring(source.Length).Replace('\\', '/');
        return relative.Contains("/bin/") || relative.Contains("/obj/");
    }
}