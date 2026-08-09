using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps Dependabot's NuGet coverage and the committed lock files describing the
///     same set of projects, in both directions.
/// </summary>
/// <remarks>
///     <para>
///         <b>What goes wrong without it.</b> A committed <c>packages.lock.json</c> that no update
///         entry names is a project whose pinned versions stop being reviewed the day it is added,
///         and nothing about it is visible: the build stays green, the lock file stays valid, and the
///         only symptom is an advisory nobody is told about. The other direction is as quiet, since a
///         listed directory with no lock file raises update PRs against a project that restores
///         unlocked, where the pin it is bumping does not exist.
///     </para>
///     <para>
///         <b>Read by scan, not by a YAML parser.</b> The configuration is a two-entry update list,
///         so the shapes worth reading are one key and one bullet list; a YAML dependency to reach
///         them would cost more than it proves. What a scan cannot do is follow an anchor or a flow
///         sequence, so the list is pinned non-empty rather than trusted: a rewrite into a form this
///         cannot read fails here instead of passing over nothing.
///     </para>
///     <para>
///         Git decides which lock files count, through <see cref="TrackedFiles" />, so this gate and
///         every other hygiene gate agree on what the repository actually publishes.
///     </para>
/// </remarks>
public sealed class DependabotCoverageTests
{
    private const string DependabotConfig = ".github/dependabot.yml";

    private const string LockFileName = "packages.lock.json";

    // The number of projects carrying a committed lock file. Pinned as well as cross-checked,
    // because a coordinated change — a seventh project, listed correctly — still has prose to move.
    private const int LockFileProjectCount = 6;

    private static readonly Regex EcosystemEntry = new(@"^\s*-\s*package-ecosystem:\s*(?<name>\S+)\s*$");

    private static readonly Regex DirectoriesKey = new(@"^\s*directories:\s*$");

    private static readonly Regex DirectoryItem = new(@"^\s*-\s*(?<path>/\S*)\s*$");

    [Fact]
    public void DependabotNuGetList_CoversEveryCommittedLockFile()
    {
        // Arrange
        var listed = ReadNuGetDirectories().ToHashSet(StringComparer.Ordinal);
        var committed = CommittedLockFileDirectories().ToHashSet(StringComparer.Ordinal);

        // Act
        var unlisted = committed.Except(listed).Order(StringComparer.Ordinal).ToList();
        var unlocked = listed.Except(committed).Order(StringComparer.Ordinal).ToList();

        // Assert
        unlisted.ShouldBeEmpty(
            $"Project(s) carry a committed {LockFileName} that {DependabotConfig} does not update:\n{string.Join("\n", unlisted)}");
        unlocked.ShouldBeEmpty(
            $"{DependabotConfig} updates director(ies) with no committed {LockFileName}:\n{string.Join("\n", unlocked)}");
    }

    [Fact]
    public void DependabotNuGetList_IsReadable()
    {
        // Act
        var listed = ReadNuGetDirectories();

        // Assert: the arm above compares two sets, and an empty parse would make it pass by reading
        // nothing rather than by finding agreement.
        listed.ShouldNotBeEmpty($"No directories were parsed out of the nuget update entry in {DependabotConfig}.");
    }

    [Fact]
    public void CommittedLockFiles_StillNumberSix()
    {
        // Act
        var committed = CommittedLockFileDirectories();

        // Assert: the count is written out in prose as well as enforced. It is stated in the comment
        // above the update list in .github/dependabot.yml and in the restore comment in
        // .github/workflows/ci.yml, and SECURITY.md's supply-chain promise covers whatever the set
        // holds, so all of them move together with this number.
        committed.Count.ShouldBe(
            LockFileProjectCount,
            $"The committed {LockFileName} count moved:\n{string.Join("\n", committed)}");
    }

    // Every tracked lock file's directory, in the leading-slash repository-relative form the update
    // entry lists, so the two sets are comparable as they stand.
    private static IReadOnlyList<string> CommittedLockFileDirectories()
    {
        return TrackedFiles.All
            .Where(static path => path == LockFileName || path.EndsWith($"/{LockFileName}", StringComparison.Ordinal))
            .Select(DirectoryOf)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // A lock file at the repository root maps to "/", which is the form a root update entry takes.
    private static string DirectoryOf(string lockFilePath)
    {
        int lastSlash = lockFilePath.LastIndexOf('/');

        return lastSlash < 0 ? "/" : $"/{lockFilePath[..lastSlash]}";
    }

    // The directories listed under the nuget update entry: the bullet list under its `directories:`
    // key, which ends at the first line that is not one of its items.
    private static IReadOnlyList<string> ReadNuGetDirectories()
    {
        string[] lines = File.ReadAllLines(RepoRoot.Absolute(DependabotConfig));
        string[] entry = ReadNuGetEntry(lines);
        int keyIndex = Array.FindIndex(entry, DirectoriesKey.IsMatch);
        if (keyIndex < 0) return [];

        List<string> directories = new();
        for (int index = keyIndex + 1; index < entry.Length; index++)
        {
            Match item = DirectoryItem.Match(entry[index]);
            if (!item.Success) break;

            directories.Add(item.Groups["path"].Value);
        }

        return directories;
    }

    // The lines of the nuget update entry, from its own `- package-ecosystem:` line to the line
    // before the next entry. Missing entirely is a failure rather than an empty result: nothing
    // would be keeping the pinned versions current at all.
    private static string[] ReadNuGetEntry(string[] lines)
    {
        int start = Array.FindIndex(lines, static line => IsEcosystem(line, "nuget"));
        if (start < 0)
            throw new InvalidOperationException(
                $"{DependabotConfig} carries no nuget update entry, so nothing keeps the pinned package versions current.");

        int next = Array.FindIndex(lines, start + 1, EcosystemEntry.IsMatch);
        int end = next < 0 ? lines.Length : next;

        return lines[start..end];
    }

    private static bool IsEcosystem(string line, string name)
    {
        Match entry = EcosystemEntry.Match(line);

        return entry.Success && entry.Groups["name"].Value == name;
    }
}
