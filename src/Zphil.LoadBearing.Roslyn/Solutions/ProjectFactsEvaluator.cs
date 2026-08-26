using System.Runtime.CompilerServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Roslyn.Solutions;

/// <summary>
///     One project file to evaluate: its path, and the framework to evaluate it for where the load reported
///     one. A null framework is the single-target case, where the project's own
///     <c>&lt;TargetFramework&gt;</c> already decides every condition.
/// </summary>
internal readonly record struct ProjectEvaluationRequest(string? ProjectFile, string? TargetFramework);

/// <summary>
///     Reads a project's artifact facts by <em>evaluating</em> it — the packages it declares, whether it
///     packs, whether its restore locks, and the frameworks it targets — rather than by parsing its XML.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why evaluation and not the csproj's own text.</b> Every fact here is regularly decided
///         somewhere the project file does not mention: a <c>Directory.Build.props</c> above it, a package's
///         imported targets, or an SDK default nobody wrote (<c>IsPackable</c> is true unless something says
///         otherwise). A reader of the XML sees the declarations and misses the value; an evaluation sees
///         the value and can still say which declaration won.
///     </para>
///     <para>
///         <b>Lifetime and threading.</b> A <see cref="ProjectCollection" /> is created for one batch and
///         disposed with it, and the batch evaluates serially — the collection is not safe for concurrent
///         evaluation, and it caches both loaded projects and parsed imports by path, so one shared across
///         calls would answer a later call from an earlier call's read of a file that has since changed.
///         That is the exact staleness the warm session's per-call reconcile exists to prevent, so nothing
///         here outlives the call. The collection is what makes a batch affordable in the first place: the
///         projects of one solution share nearly all of their imports, so the first evaluation pays for them
///         and the rest do not. Each project is unloaded as soon as its four facts have been copied out of
///         it — the parsed imports it shares are held by the collection rather than by it, so a batch keeps
///         them while holding one evaluated project at a time rather than all of them.
///     </para>
///     <para>
///         <b>Every failure is an absence.</b> MSBuild may be unavailable in this process, an import may not
///         resolve, a project may not parse. Each of those records nothing for the project rather than a
///         default, because a default here is indistinguishable from a fact and would be checked as one. The
///         filter on the catch is what keeps a cancellation travelling.
///     </para>
/// </remarks>
internal static class ProjectFactsEvaluator
{
    // The line a fact falls back to when it has no declaration of its own to point at, or when the winning
    // declaration is not one this repository owns. The project file is always somewhere a reader can go.
    private const int ProjectFileLine = 1;

    /// <summary>
    ///     Evaluates <paramref name="requests" />, returning one result per request by index —
    ///     <see langword="null" /> where that project could not be evaluated at all.
    /// </summary>
    /// <param name="requests">The project files and frameworks to evaluate, in the caller's order.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static IReadOnlyList<ProjectArtifactFacts?> EvaluateAll(
        IReadOnlyList<ProjectEvaluationRequest> requests, CancellationToken ct)
    {
        var facts = new ProjectArtifactFacts?[requests.Count];
        if (requests.Count == 0) return facts;

        try
        {
            EvaluateInto(facts, requests, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // MSBuild itself did not load, or the collection could not be built. Every project stays absent,
            // which is the same answer a hand-built extraction gives and is read the same way downstream.
            Array.Clear(facts);
        }

        return facts;
    }

    // Split out and never inlined so that a runtime unable to load the MSBuild engine assemblies fails at
    // the call above — inside its catch — rather than while jitting the caller. Same discipline the
    // extraction entry points keep around Roslyn's own types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EvaluateInto(
        ProjectArtifactFacts?[] facts, IReadOnlyList<ProjectEvaluationRequest> requests, CancellationToken ct)
    {
        using var collection = new ProjectCollection();
        var evaluated = new Dictionary<(string File, string? Framework), ProjectArtifactFacts?>();

        for (var i = 0; i < requests.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            ProjectEvaluationRequest request = requests[i];
            if (request.ProjectFile is not { Length: > 0 } projectFile) continue;

            (string, string?) key = (PathComparison.Fold(projectFile), request.TargetFramework);
            if (!evaluated.TryGetValue(key, out ProjectArtifactFacts? result))
            {
                result = TryEvaluate(collection, projectFile, request.TargetFramework);
                evaluated[key] = result;
            }

            facts[i] = result;
        }
    }

    private static ProjectArtifactFacts? TryEvaluate(
        ProjectCollection collection, string projectFile, string? targetFramework)
    {
        try
        {
            return Evaluate(collection, projectFile, targetFramework);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static ProjectArtifactFacts Evaluate(
        ProjectCollection collection, string projectFile, string? targetFramework)
    {
        Dictionary<string, string>? globalProperties = targetFramework is { Length: > 0 }
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["TargetFramework"] = targetFramework }
            : null;
        var options = new ProjectOptions
        {
            ProjectCollection = collection,
            GlobalProperties = globalProperties,
            LoadSettings = ProjectLoadSettings.IgnoreMissingImports
                           | ProjectLoadSettings.IgnoreInvalidImports
                           | ProjectLoadSettings.IgnoreEmptyImports
        };
        Project project = Project.FromFile(projectFile, options);

        try
        {
            var projectSite = new FragmentSite(projectFile, ProjectFileLine);
            string projectDirectory = Path.GetDirectoryName(projectFile) ?? projectFile;
            HashSet<string> projectCone = ProjectCone(projectDirectory);

            (IReadOnlyList<string> frameworks, FragmentSite frameworksSite) =
                ReadTargetFrameworks(project, projectCone, projectSite);
            (bool? packable, FragmentSite packableSite) = ReadPackable(project, projectCone, projectSite);
            (bool locks, FragmentSite locksSite) = ReadLockPolicy(project, projectCone, projectSite);
            IReadOnlyList<FragmentPackageReference> packages =
                ReadPackageReferences(project, projectCone, projectSite);

            return new ProjectArtifactFacts(
                frameworks, frameworksSite, packages, packable, packableSite, locks, locksSite);
        }
        finally
        {
            collection.UnloadProject(project);
        }
    }

    // The SDK spelling first, because an SDK project defines both properties and the plural is authoritative
    // for a project that targets several. The classic spelling is the fallback and the only one that needs
    // normalizing: a project predating the SDK states a framework identifier and a version rather than a
    // moniker, and nothing downstream should have to know that two spellings of net48 exist.
    private static (IReadOnlyList<string> Frameworks, FragmentSite Site) ReadTargetFrameworks(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        foreach (string property in new[] { "TargetFrameworks", "TargetFramework" })
        {
            ProjectProperty? declared = project.GetProperty(property);
            if (declared is null || declared.EvaluatedValue.Length == 0) continue;

            List<string> frameworks = declared.EvaluatedValue
                .Split([';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(framework => framework.Trim())
                .Where(framework => framework.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(framework => framework, StringComparer.Ordinal)
                .ToList();
            if (frameworks.Count > 0)
                return (frameworks, SiteOf(declared, projectCone, projectSite));
        }

        ProjectProperty? version = project.GetProperty("TargetFrameworkVersion");
        string? classic = ClassicMoniker(project.GetPropertyValue("TargetFrameworkIdentifier"), version?.EvaluatedValue);

        return classic is null
            ? ([], projectSite)
            : ([classic], SiteOf(version, projectCone, projectSite));
    }

    // ".NETFramework" + "v4.8" -> "net48". Only the .NET Framework identifier is spelled out, because it is
    // the only one that reaches here: every other identifier belongs to a project the SDK gives a
    // TargetFramework property to, which the caller above has already read. An unrecognised pair records
    // nothing rather than a guess — a wrong moniker would be checked as if it were the project's own word.
    private static string? ClassicMoniker(string identifier, string? version)
    {
        if (version is not { Length: > 1 } || version[0] != 'v') return null;
        if (identifier.Length > 0 && !string.Equals(identifier, ".NETFramework", StringComparison.Ordinal)) return null;

        string digits = version.Substring(1).Replace(".", "");
        return digits.Length > 0 && digits.All(char.IsDigit) ? $"net{digits}" : null;
    }

    // Undefined is null, not false: the property is defined by the SDK's pack targets, so a project that
    // does not import them (every classic project) has no answer here at all. A value that is neither
    // true nor false is the same nothing — MSBuild would treat it as false, but a rule reading it as a
    // deliberate opt-out would be reasoning from a typo.
    private static (bool? Packable, FragmentSite Site) ReadPackable(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        ProjectProperty? declared = project.GetProperty("IsPackable");
        if (declared is null || !bool.TryParse(declared.EvaluatedValue.Trim(), out bool packable))
            return (null, projectSite);

        return (packable, SiteOf(declared, projectCone, projectSite));
    }

    // Undefined is false here, and that asymmetry with IsPackable is the fact rather than an inconsistency:
    // NuGet writes a lock file only when told to, so a project that says nothing has said no. Its site is
    // then the project file, which is where the missing declaration would go.
    private static (bool Locks, FragmentSite Site) ReadLockPolicy(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        ProjectProperty? declared = project.GetProperty("RestorePackagesWithLockFile");
        if (declared is null) return (false, projectSite);

        bool locks = bool.TryParse(declared.EvaluatedValue.Trim(), out bool parsed) && parsed;
        return (locks, SiteOf(declared, projectCone, projectSite));
    }

    // Declared references only. The SDK adds some of its own — NETStandard.Library to a netstandard project,
    // for one — and marks each of them implicit; they are nobody's declaration and nobody can remove them,
    // so a rule about what a project depends on would only be able to fail on them. Which of two declarations
    // of one name survives, and in what order the survivors come out, is the shared fold's business rather
    // than this reader's: the merge meets the same duplicates a framework at a time and must answer alike.
    private static IReadOnlyList<FragmentPackageReference> ReadPackageReferences(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        var declarations = new List<FragmentPackageReference>();
        foreach (ProjectItem item in project.GetItems("PackageReference"))
        {
            if (string.Equals(item.GetMetadataValue("IsImplicitlyDefined"), "true", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = item.EvaluatedInclude.Trim();
            if (name.Length == 0) continue;

            FragmentSite site = SiteOf(item.Xml?.Location, projectCone, projectSite);
            declarations.Add(new FragmentPackageReference(name, site));
        }

        return FragmentSiteSets.OrderedPackages(
            declarations, (name, site) => new FragmentPackageReference(name, site));
    }

    private static FragmentSite SiteOf(
        ProjectProperty? declared, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        return SiteOf(declared?.Xml?.Location, projectCone, projectSite);
    }

    // A declaration is only worth citing where somebody reading this repository can go and change it. The
    // SDK's own defaults have real locations, but they name a directory on whichever machine ran the build,
    // so they degrade to the project file — which is where the override would be written anyway.
    private static FragmentSite SiteOf(
        ElementLocation? location, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        if (location is null || location.File.Length == 0) return projectSite;

        return IsUnderProjectCone(location.File, projectCone)
            ? new FragmentSite(location.File, Math.Max(location.Line, ProjectFileLine))
            : projectSite;
    }

    // The project's own directory and every directory above it, each folded — precisely the cone MSBuild
    // discovers Directory.Build.props in, so a solution-wide policy file is cited and an SDK or package-cache
    // import is not. Built once per project because every site this reads is tested against it, and the
    // common case — a declaration outside the cone — is the one that walks the chain to the drive root.
    private static HashSet<string> ProjectCone(string projectDirectory)
    {
        var cone = new HashSet<string>(StringComparer.Ordinal);
        for (string? directory = projectDirectory;
             !string.IsNullOrEmpty(directory);
             directory = Path.GetDirectoryName(directory))
            cone.Add(PathComparison.Fold(Normalize(directory)));

        return cone;
    }

    private static bool IsUnderProjectCone(string file, IReadOnlySet<string> projectCone)
    {
        string? declaringDirectory = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(declaringDirectory)) return false;

        return projectCone.Contains(PathComparison.Fold(Normalize(declaringDirectory)));
    }

    private static string Normalize(string directory)
    {
        return directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
