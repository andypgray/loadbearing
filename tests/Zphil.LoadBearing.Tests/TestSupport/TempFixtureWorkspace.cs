using System.Runtime.CompilerServices;
using Xunit;
using Zphil.LoadBearing.Internal;

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
///         once — <see cref="RequireSerialCollection(string?,string?)">enforced</see> at construction rather
///         than merely stated here, because a class can drift out of the collection with nothing to notice.
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
///     <para>
///         <b>The reset and the warm pool.</b> A <see cref="WarmWorkspacePool" /> session for this tree
///         outlives the test that loaded it, which is the point — but it must never make a reset fail or go
///         unseen. Unseen it cannot: a restored file carries the pristine source's write time, and the
///         session's reconcile sweep treats any mtime difference as a change. Fail it must not, so a reset
///         blocked by a held file drops the pooled session and tries once more before giving the lease up.
///     </para>
/// </remarks>
internal sealed class TempFixtureWorkspace : IDisposable
{
    private const string DefaultFixtureDirectory = "TestSolutions/MyApp";

    private const string DefaultSolutionFileName = "MyApp.sln";

    // Spelled out rather than left to a search pattern: a three-character pattern is documented to match
    // longer extensions too, so ".sln" alone is not a reliable way to include — or exclude — its siblings.
    // Every project kind a bed may hold, not just C#: a bed with a project in another language restores it
    // like any other, and leaving that extension out would let an edit to it reuse a stale restore.
    private static readonly string[] RestoreRelevantExtensions =
        [".csproj", ".fsproj", ".vbproj", ".sln", ".slnx", ".slnf", ".props", ".targets"];

    // The collection every caller has to be in, named by its definition type rather than by the string
    // "Serial": the attribute and the definition move together under a rename, and the definition type is
    // what the runner reports back for a member.
    private static readonly string SerialCollectionName = typeof(SerialCollection).FullName!;

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
        RequireSerialCollection();

        string source = SourceDirectory(fixtureDirectory);
        var key = $"{Path.GetFileNameWithoutExtension(callerFilePath)}-{fixtureDirectory.Replace('/', '-')}";

        if (TryAcquire(key))
        {
            string leased = LeasedDirectory(key);
            if (TrySyncTree(source, leased, out bool projectsChanged)
                || RetryAfterDroppingWarmWorkspaces(source, leased, out projectsChanged))
            {
                _leaseKey = key;
                _root = leased;
                SolutionPath = Path.Combine(_root, solutionFileName);
                if (restore && projectsChanged) FixtureRestorer.Restore(SolutionPath);
                return;
            }

            // The reset could not complete even with the warm sessions dropped — a fixture file is held by
            // something else. Give the lease up and fall through to a private copy: slower, never a spurious
            // failure.
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
        RequireSerialCollection();

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

        // A private copy is deleted outright, so any warm workspace still holding it has to go first.
        WarmWorkspacePool.DropUnder(_root);
        try
        {
            ReadOnlyTolerant.DeleteTree(_root);
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

    /// <summary>
    ///     Refuses a construction from a test outside the <see cref="SerialCollection">"Serial"</see>
    ///     collection, reading which collection that is from the ambient runner context.
    /// </summary>
    /// <remarks>
    ///     Sited at the constructor because every path in reaches it — <see cref="TempGitRepo" /> and
    ///     <see cref="BinlogFixtureWorkspace" /> both wrap one, so a wrapper added later is covered without
    ///     being listed anywhere. A test that forgets the attribute fails on the line that builds the copy,
    ///     under a filtered run of that test alone, rather than as a distant gate over the whole suite.
    /// </remarks>
    private static void RequireSerialCollection()
    {
        ITestContext current = TestContext.Current;
        RequireSerialCollection(
            current.TestCollection?.TestCollectionClassName, current.TestClass?.TestClassSimpleName);
    }

    /// <summary>
    ///     The refusal itself, over the two facts read above. Split from the ambient read so both halves
    ///     pin: what the runner reports for a member of the collection, and what this makes of it.
    /// </summary>
    /// <param name="collectionClassName">
    ///     The definition type of the collection the calling test belongs to; <c>null</c> for the implicit
    ///     per-class collection a test class gets when it carries no <c>[Collection]</c> attribute, which is
    ///     the shape of the defect this exists to catch.
    /// </param>
    /// <param name="testClassName">The calling test's class, named in the refusal so it needs no lookup.</param>
    internal static void RequireSerialCollection(string? collectionClassName, string? testClassName)
    {
        if (string.Equals(collectionClassName, SerialCollectionName, StringComparison.Ordinal)) return;

        throw new InvalidOperationException(
            $"{testClassName ?? "A test"} builds a fixture copy from outside the \"Serial\" collection. "
            + "Add [Collection(\"Serial\")] to it: the copy shells dotnet restore with redirected output, "
            + "and that child deadlocks against the Roslyn BuildHost a workspace-loading test spawns at the "
            + "same moment.");
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
    ///     The second attempt at a reset, after releasing any warm workspace still holding this tree. A
    ///     pooled <see cref="WarmWorkspacePool">session</see> deliberately outlives the test that loaded it,
    ///     so it — rather than a leaked handle — is the likeliest thing keeping a fixture file open. Dropping
    ///     it costs the next test one load; falling through to a private copy would cost a load <em>and</em> a
    ///     restore, every remaining test in the class.
    /// </summary>
    private static bool RetryAfterDroppingWarmWorkspaces(string source, string leased, out bool projectsChanged)
    {
        WarmWorkspacePool.DropUnder(leased);
        return TrySyncTree(source, leased, out projectsChanged);
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
        string root = Path.Combine(TestTempRoot.For("fixtures"), Guid.NewGuid()
            .ToString("N"));
        DirectoryTree.Copy(source, root, relative => !IsBuildArtifact(relative));
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

            // Read-only-tolerant because a TempGitRepo consumer leaves a .git/ here and git writes its loose
            // objects read-only: a plain File.Delete threw partway through the prune, failed the whole reset,
            // and cost the caller a private copy and a restore.
            ReadOnlyTolerant.Delete(file);
            projectsChanged |= IsProjectFile(file);
        }

        return projectsChanged;
    }

    /// <summary>
    ///     Whether a changed file obliges the copy to re-restore: the project itself, any of the three
    ///     solution formats, and the MSBuild imports that can add a package reference. Internal so the
    ///     accepted set pins without leasing a tree — a format missing here fails silently, leaving the copy
    ///     on a stale <c>project.assets.json</c> rather than reporting anything.
    /// </summary>
    internal static bool IsProjectFile(string path)
    {
        string extension = Path.GetExtension(path);
        return RestoreRelevantExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameContent(string source, string target)
    {
        var targetInfo = new FileInfo(target);
        if (!targetInfo.Exists) return false;
        if (targetInfo.Length != new FileInfo(source).Length) return false;

        // Fixture files are small (the largest MyApp source is a few KB), so a full compare beats hashing.
        return File.ReadAllBytes(source)
            .AsSpan()
            .SequenceEqual(File.ReadAllBytes(target));
    }

    /// <summary>
    ///     Whether <paramref name="path" /> lies under a <c>bin/</c> or <c>obj/</c> directory. Takes an
    ///     absolute path or one relative to any root, with or without a leading separator: the leading one is
    ///     supplied here, so a path whose own first segment is <c>bin/</c> is caught like every other.
    /// </summary>
    internal static bool IsBuildArtifact(string path)
    {
        string normalized = "/" + path.Replace('\\', '/')
            .TrimStart('/');
        return normalized.Contains("/bin/") || normalized.Contains("/obj/");
    }

    private static bool IsBuildArtifact(string path, string source)
    {
        return IsBuildArtifact(path.Substring(source.Length));
    }
}
