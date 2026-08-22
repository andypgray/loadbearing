using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Roslyn.Solutions;

/// <summary>
///     A solution file's declared membership: the <c>.csproj</c> half split into the two sets a solution
///     filter makes different — what the load is <see cref="Required">entitled to demand</see> and what the
///     underlying solution <see cref="Declared">contains at all</see> — beside the
///     <see cref="Unsupported">projects in another language</see> that no load can reach. For an unfiltered
///     <c>.sln</c>/<c>.slnx</c> the first two are the same list. Which solution a filter points at is
///     <see cref="SolutionProjectFileParser.TryReadReferencedSolution">its own question</see>, asked where it
///     arises: both callers ask before the load, where no membership object exists to read.
/// </summary>
/// <param name="Required">
///     The members the load must produce — a filter's selected projects, or every member when there is no
///     filter (and when a filter's <c>projects</c> array is empty, which Roslyn reads as "the whole
///     solution"). A member of this set that did not load is a genuine load failure.
/// </param>
/// <param name="Declared">
///     Every <c>.csproj</c> the underlying solution declares, filter or no filter. Subtracting what actually
///     loaded from this is what makes a narrowed universe nameable.
/// </param>
/// <param name="Unsupported">
///     Every other declared project — an <c>.fsproj</c>, <c>.vbproj</c>, <c>.sqlproj</c>, <c>.vcxproj</c>,
///     <c>.shproj</c>, anything whose extension ends in <c>proj</c> and is not <c>.csproj</c>. Deliberately
///     <em>not</em> merged into <see cref="Declared" />: the three consumers of that set all mean "a project
///     this run was obliged to check", and adding a project no extractor can read would turn every polyglot
///     solution into a load failure. What this set is for is saying so out loud, which is the one thing the
///     survey could not do while these entries were simply dropped.
/// </param>
internal sealed record SolutionMembership(
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Declared,
    IReadOnlyList<string> Unsupported);

/// <summary>
///     One solution file's declared projects, partitioned by whether this product can read them:
///     <paramref name="Csproj" /> is what the extractor loads, <paramref name="Unsupported" /> is every other
///     declared <c>*proj</c>. Solution folders are in neither — they are not projects.
/// </summary>
/// <param name="Csproj">The declared <c>.csproj</c> paths, absolute and deduplicated, in declaration order.</param>
/// <param name="Unsupported">
///     The declared non-<c>.csproj</c> project paths, absolute and deduplicated, in declaration order.
/// </param>
internal sealed record DeclaredProjects(
    IReadOnlyList<string> Csproj,
    IReadOnlyList<string> Unsupported);

/// <summary>
///     Reads a solution file's <em>declared</em> project membership textually, with no MSBuild. Handles the
///     classic <c>.sln</c>, the XML <c>.slnx</c>, and the JSON solution-filter <c>.slnf</c> formats; entries
///     are partitioned into the <c>.csproj</c> members a load can produce and the projects in other languages
///     it cannot, solution folders are neither, and both slash spellings resolve.
/// </summary>
/// <remarks>
///     <para>
///         Three consumers, one question — <em>what does the solution declare?</em>
///         <see cref="Replay.BinlogCaptureStore" />'s coverage check compares a replayed binlog against this
///         set so it can refuse a binlog that does not build exactly the solution's projects;
///         <see cref="SpecExclusion" /> subtracts it from a spec project's <c>ProjectReference</c> closure to
///         tell the spec's private plumbing from the codebase under law; and
///         <see cref="ProjectLoadFailures" /> subtracts what loaded from it to name both the projects that
///         failed and the projects a filter left out. All three need declared membership rather than "every
///         project MSBuild loaded", which is why this lives beside them rather than inside <c>Replay</c>.
///     </para>
///     <para>
///         <b>A filter is a seed set, not the universe.</b> Measured against a three-project bed whose
///         references chain <c>Domain → Web → Billing</c>: a filter naming <c>Domain</c> alone loads all
///         three, one naming <c>Web</c> loads two, one naming the leaf loads one. Roslyn loads a filter's
///         projects <em>plus their transitive <c>ProjectReference</c> closure</em>, and
///         <c>DisableTransitiveProjectReferences</c> does not suppress it — that property governs what
///         MSBuild hands the compiler, not what the workspace loader walks. So
///         <see cref="SolutionMembership.Required" /> is what a load must produce, never what it will:
///         subtracting it from what loaded would name projects that were checked. Only the loaded solution
///         knows the checked set, which is why the narrowing is computed there and not here.
///     </para>
///     <para>
///         <b>
///             A fourth question, asked of the same text: what does the solution declare that this product
///             cannot read?
///         </b>
///         <see cref="SolutionMembership.Unsupported" /> is that answer, and it is derived
///         here rather than from <c>solution.Projects</c> because only the solution file sees all of it. F#
///         is the one non-C# kind that reaches a loaded solution at all — it plugs into Roslyn's
///         <em>workspace</em> model while having no Roslyn compiler behind it, so it loads and then supports
///         no compilation. A <c>.vbproj</c> never loads, because this repo references only the C# workspace
///         package and so has no VB language service in its MEF host; <c>.sqlproj</c>, <c>.vcxproj</c> and
///         <c>.shproj</c> have no Roslyn loader either. Reading the declaration is also the same answer on a
///         cold run, a cache hit and a warm session by construction, and it needs no workspace. The residual
///         limit is the mirror of the existing passenger case: a non-C# project dragged in by a
///         <c>ProjectReference</c> and declared in no solution file stays invisible.
///     </para>
///     <para>
///         This is a membership oracle, not a solution loader: it only needs the project <em>paths</em>, so it
///         does not evaluate configurations, conditions, or nested-project ownership. Paths are made absolute
///         against the solution file's directory but not symlink-canonicalized — callers canonicalize both
///         sides at comparison time (matching <c>SpecResolver.IsProjectFile</c>), so this stays pure and
///         disk-independent for its <see cref="ParseDeclaredProjects" /> core.
///     </para>
/// </remarks>
internal static class SolutionProjectFileParser
{
    // A classic-.sln project line: Project("{TypeGuid}") = "Name", "Relative\Path.csproj", "{ProjectGuid}".
    // The second quoted field (named group "path") is the project path; solution folders put a folder name
    // there instead, filtered out later by the .csproj extension test.
    private static readonly Regex SlnProjectLine = new(
        "Project\\(\"\\{[^}]*\\}\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"(?<path>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Whether this parser owns <paramref name="solutionPath" />'s format — the two full-solution formats
    ///     and the solution filter that points at one. A caller that subtracts declared membership must ask
    ///     this first rather than mistake an unparsed file for "the solution declares nothing".
    /// </summary>
    internal static bool OwnsFormat(string solutionPath)
    {
        string extension = Path.GetExtension(solutionPath);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
               || IsFilterFormat(solutionPath);
    }

    /// <summary>Whether <paramref name="solutionPath" /> is a solution filter (<c>.slnf</c>).</summary>
    internal static bool IsFilterFormat(string solutionPath)
    {
        return Path.GetExtension(solutionPath).Equals(".slnf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Reads <paramref name="solutionPath" />'s membership, resolving a solution filter through to the
    ///     solution it references. Throws on an unreadable or malformed file; callers that must not fail open
    ///     catch it.
    /// </summary>
    /// <remarks>
    ///     Under a <c>.slnf</c> the unsupported entries are the <em>referenced solution's</em>, unfiltered.
    ///     Filtering them would be a claim a filter cannot support: leaving an <c>.fsproj</c> out of a
    ///     selection does not make it surveyable, so hiding it would only restore the silence.
    /// </remarks>
    internal static SolutionMembership ReadDeclaredMembership(string solutionPath)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        if (!IsFilterFormat(fullPath))
        {
            DeclaredProjects projects = ReadDeclaredProjects(fullPath);
            return new SolutionMembership(projects.Csproj, projects.Csproj, projects.Unsupported);
        }

        (string referencedSolution, IReadOnlyList<string> requested) =
            ParseFilter(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!);

        DeclaredProjects referenced = ReadDeclaredProjects(referencedSolution);
        IReadOnlyList<string> declared = referenced.Csproj;

        // Roslyn's own rule, reproduced exactly: an empty projects array is not an empty selection, it is
        // "no filtering at all". Intersecting instead would load nothing and report the whole solution
        // dropped — the degraded answer this type exists to avoid, arrived at from the other side.
        if (requested.Count == 0) return new SolutionMembership(declared, declared, referenced.Unsupported);

        var selected = new HashSet<string>(requested, PathComparison.Comparer);
        List<string> required = declared
            .Where(selected.Contains)
            .ToList();

        return new SolutionMembership(required, declared, referenced.Unsupported);
    }

    /// <summary>
    ///     The pure filter-to-paths core, testable without disk: reads a <c>.slnf</c>'s referenced solution
    ///     and its requested project paths. The two resolve against <em>different</em> bases — the solution
    ///     against <paramref name="filterDirectory" />, each project against the referenced solution's own
    ///     directory — which is the shape Visual Studio writes and Roslyn reads.
    /// </summary>
    /// <exception cref="FormatException">
    ///     The filter is not JSON, declares no solution, or points at another filter (which Roslyn does not
    ///     support).
    /// </exception>
    internal static (string ReferencedSolutionPath, IReadOnlyList<string> RequestedProjects) ParseFilter(
        string filterText, string filterDirectory)
    {
        JsonElement solution;
        try
        {
            using JsonDocument document = JsonDocument.Parse(filterText);
            if (!document.RootElement.TryGetProperty("solution", out JsonElement element))
                throw new FormatException("The solution filter declares no 'solution' object.");

            solution = element.Clone();
        }
        catch (JsonException ex)
        {
            throw new FormatException("The solution filter is not well-formed JSON.", ex);
        }

        if (!solution.TryGetProperty("path", out JsonElement pathElement)
            || pathElement.GetString() is not { Length: > 0 } declaredPath)
            throw new FormatException("The solution filter declares no solution path.");

        string referencedSolution = Path.GetFullPath(Path.Combine(filterDirectory, Normalize(declaredPath)));
        if (IsFilterFormat(referencedSolution))
            throw new FormatException("A solution filter may not reference another solution filter.");

        string solutionDirectory = Path.GetDirectoryName(referencedSolution)!;
        var requested = new List<string>();
        if (solution.TryGetProperty("projects", out JsonElement projects)
            && projects.ValueKind == JsonValueKind.Array)
            requested.AddRange(projects
                .EnumerateArray()
                .Select(entry => entry.GetString())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, Normalize(path!)))));

        return (referencedSolution, requested);
    }

    /// <summary>
    ///     The <c>.sln</c>/<c>.slnx</c> that <paramref name="solutionPath" /> points at when it is a filter,
    ///     or <see langword="null" /> for any other format and for a filter too malformed to resolve. The
    ///     format test comes first, so a plain solution costs no I/O at all.
    /// </summary>
    /// <remarks>
    ///     Best-effort by design, and deliberately not <see cref="ReadDeclaredMembership" />: both callers ask
    ///     before the workspace load, where a throw would pre-empt the load's own filter refusal — the one
    ///     that names the file and says what a filter must be — with a worse-placed one.
    /// </remarks>
    internal static string? TryReadReferencedSolution(string solutionPath)
    {
        if (!IsFilterFormat(solutionPath)) return null;

        try
        {
            string fullPath = Path.GetFullPath(solutionPath);
            (string referencedSolution, _) =
                ParseFilter(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!);

            return referencedSolution;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The directory every convention-relative path of a run over <paramref name="solutionPath" /> anchors
    ///     at: the referenced solution's directory under a <c>.slnf</c>, and the file's own directory
    ///     otherwise. Never throws — a filter that cannot be resolved, or names a solution that is not there,
    ///     anchors at its own directory and leaves the refusal to the load.
    /// </summary>
    /// <remarks>
    ///     A filter is a lens on a solution, not a codebase of its own: baselines, render targets, diff
    ///     resolution and every relativized evidence path belong to the solution it references, or a filter
    ///     kept in a directory of its own would resolve none of them and write its output beside itself.
    /// </remarks>
    internal static string AnchorDirectory(string solutionPath)
    {
        if (TryReadReferencedSolution(solutionPath) is { } referencedSolution
            && File.Exists(referencedSolution))
            return Path.GetDirectoryName(referencedSolution)!;

        return Path.GetDirectoryName(solutionPath)!;
    }

    /// <summary>
    ///     Reads <paramref name="solutionPath" /> from disk and returns the absolute paths of its
    ///     <c>.csproj</c> members. Dispatches on the file extension (<c>.slnx</c> ⇒ XML, else classic).
    /// </summary>
    internal static IReadOnlyList<string> ReadCsprojMembers(string solutionPath)
    {
        return ReadDeclaredProjects(solutionPath).Csproj;
    }

    /// <summary>
    ///     Reads <paramref name="solutionPath" /> from disk and partitions its declared projects into the
    ///     <c>.csproj</c> members and everything else. Dispatches on the file extension (<c>.slnx</c> ⇒ XML,
    ///     else classic).
    /// </summary>
    internal static DeclaredProjects ReadDeclaredProjects(string solutionPath)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        string text = File.ReadAllText(fullPath);
        string directory = Path.GetDirectoryName(fullPath)!;
        return ParseDeclaredProjects(text, Path.GetExtension(fullPath), directory);
    }

    /// <summary>
    ///     The pure text-to-paths core, testable without disk: parses <paramref name="solutionText" /> per
    ///     <paramref name="extension" /> (<c>.slnx</c> ⇒ XML, else classic <c>.sln</c>), splits the declared
    ///     entries into <c>.csproj</c> and every other project kind, and resolves each against
    ///     <paramref name="solutionDirectory" /> (normalizing both slash spellings). Duplicate paths collapse.
    /// </summary>
    /// <remarks>
    ///     <b>A declared entry is a project when its extension ends in <c>proj</c>.</b> That test buys the
    ///     whole non-C# family — <c>.fsproj</c>, <c>.vbproj</c>, <c>.sqlproj</c>, <c>.vcxproj</c>,
    ///     <c>.shproj</c>, and whatever the next tool mints — without a list this parser would have to keep
    ///     current, and it excludes solution folders for free: a classic <c>.sln</c> folder line carries a
    ///     bare name in the path field, and a <c>.slnx</c> <c>&lt;Folder&gt;</c> is not a <c>Project</c>
    ///     element at all.
    /// </remarks>
    internal static DeclaredProjects ParseDeclaredProjects(
        string solutionText, string extension, string solutionDirectory)
    {
        IEnumerable<string> relativePaths = extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(solutionText)
            : ParseSln(solutionText);

        var csproj = new List<string>();
        var unsupported = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);
        foreach (string relative in relativePaths)
        {
            if (!IsProjectEntry(relative)) continue;

            string fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, Normalize(relative)));
            if (!seen.Add(fullPath)) continue;

            if (relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) csproj.Add(fullPath);
            else unsupported.Add(fullPath);
        }

        return new DeclaredProjects(csproj, unsupported);
    }

    // Read off the declared spelling rather than Path.GetExtension: a dotted directory segment is no threat
    // here (the extension is the last one either way), but a bare solution-folder name is exactly what must
    // not match, and "ends in proj" says that in one test.
    private static bool IsProjectEntry(string relativePath)
    {
        return Path.GetExtension(relativePath)
            .EndsWith("proj", StringComparison.OrdinalIgnoreCase);
    }

    // Solution files and filters are both written with whichever slash the authoring tool prefers.
    private static string Normalize(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static IEnumerable<string> ParseSln(string solutionText)
    {
        foreach (Match match in SlnProjectLine.Matches(solutionText))
            yield return match.Groups["path"].Value;
    }

    private static IEnumerable<string> ParseSlnx(string solutionText)
    {
        // Project entries may sit at the root or nested under <Folder> elements, so walk every descendant.
        return XDocument.Parse(solutionText)
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!);
    }
}
