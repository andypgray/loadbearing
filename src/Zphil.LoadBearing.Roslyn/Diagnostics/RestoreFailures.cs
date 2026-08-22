using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     Which projects' NuGet packages are missing from the model — because their restore ran and failed, or
///     because it never ran at all — answered from the <c>project.assets.json</c> each project points at
///     rather than from the text of any MSBuild message. The second cause the fail-closed gate keys on,
///     beside <see cref="ProjectLoadFailures" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>The measurement this exists for.</b> One tree, one spec, the NuGet feed the only variable and no
///         rebuild between the runs: with the feed reachable <c>check</c> exited 1 on a rule forbidding a
///         package namespace; with the feed unreachable it exited <b>0</b>, and the rule reported itself inert
///         because its target selection matched no types. <c>graph --json</c> settled which half was wrong —
///         the external edge the violation rests on was in the restored run's survey and absent from the
///         broken one. The violation was not missed; the edge it rests on was never extracted.
///     </para>
///     <para>
///         <b>Why <see cref="ProjectLoadFailures" /> cannot see it.</b> A project whose restore failed still
///         <em>loads completely</em> — full document set, both output paths — so it presents none of the shapes
///         that predicate reads, nothing is blamed, and the model is silently missing every package edge. That
///         is the design working as intended rather than a regression: the gate was deliberately moved off
///         diagnostic text onto loaded structure, because message matching refused solutions whose rules all
///         passed and refused in German what it let through in English.
///     </para>
///     <para>
///         <b>Why the assets file, and why it is not a diagnostic parse.</b> A failed restore <em>does</em>
///         write <c>project.assets.json</c> — measured, and contrary to what was expected — with an empty
///         <c>libraries</c> section and the failure recorded in its <c>logs</c> array as a
///         <c>{ "code": …, "level": … }</c> pair. Both fields are invariant: <c>NU1301</c> is <c>NU1301</c> and
///         <c>Error</c> is <c>Error</c> whatever language the UI renders in. So a project whose assets file
///         carries a <c>level: Error</c> entry is a project whose restore failed, in any locale — a file read,
///         not a message match. The assets file hands back exactly the two facts Roslyn's
///         <c>MSBuildDiagnosticLogger</c> throws away, since it records <c>BuildEventArgs.Message</c> and never
///         <c>.Code</c>.
///     </para>
///     <para>
///         <b>Where the file is, spelled nowhere.</b> The candidate paths come from
///         <see cref="IntermediateOutputTree.AssetsPathsOf" />, which already derives the default layout,
///         <c>UseArtifactsOutput</c>, and a redirected <c>BaseIntermediateOutputPath</c> from the project's own
///         evaluated paths — the same primitive the cache stamps, so the file this reads and the file that
///         invalidates the cache are the same file by construction. The first candidate that exists wins: the
///         list is ordered default-first and then deepest-first up the intermediate tree, so a shallower shared
///         intermediate root cannot shadow a project's own file.
///     </para>
///     <para>
///         <b>A bad read is not a failure.</b> Every degradation — unreadable file, malformed JSON, an
///         unexpected node kind, an unknown layout — answers "not failed", the same posture the repo's one
///         other NuGet-manifest parse takes. Silence is the only safe answer for a file that could not be read:
///         the alternative is refusing a healthy solution, which is the disease the structural gate was written
///         to cure. A false negative is the safe direction, and it is where every degradation lands.
///     </para>
///     <para>
///         <b>No assets file at all, and why that took a second read.</b> Absence asserts nothing on its own:
///         a non-SDK-style .NET Framework project never writes an assets file — measured on this repo's own
///         <c>Fixtures/LegacySolutions/ClassicApp</c> bed — and those are exactly the codebases this product is
///         built for, so "absent ⇒ failed" would refuse them wholesale. It asserts a great deal for an
///         SDK-style project, which writes one on every restore, and which was measured to <em>load completely</em>
///         without one — full document and reference sets, both output paths, zero workspace
///         diagnostics — while missing every package edge exactly as a failed restore leaves it. So the
///         absence is read together with <see cref="SdkStyleProject.IsSdkStyle" />, whose corpus is the
///         measurement that licensed this arm: every project in this repo and its fixture trees, classified,
///         and paired with whether an assets file is really on disk. The two agreed everywhere except the
///         three beds deliberately left unrestored.
///     </para>
///     <para>
///         <b>The residual limit, which is narrower and still real.</b> A <em>non</em>-SDK-style project that
///         uses <c>PackageReference</c> and was never restored stays invisible. It would write an assets file,
///         so its absent one is a fact — but the absence is indistinguishable from a <c>packages.config</c>
///         project's, and telling them apart means reading item groups rather than the project's shape. The
///         same posture applies as before: what is unmeasured stays unmeasured rather than being guessed at,
///         and the direction the guess would fail in is refusing a healthy legacy solution.
///     </para>
///     <para>
///         <b>Only C# projects are asked about.</b> A solution's projects in other languages reach the loaded
///         solution — F# does, measured — and this predicate used to blame them like any other, which is a
///         false refusal twice over: their packages are not in the model because the projects are not in the
///         model, and the remedy it names cannot help. Measured on a two-project bed with the C# half fully
///         restored, that was exit 2 naming the <c>.fsproj</c> alone. Such a project is stated under
///         <c>WorkspaceDiagnostics.UnsupportedProjects</c> instead, which says what the run covers rather
///         than refusing it. The narrowing is safe in the direction that matters: this arm can now only blame
///         projects the model actually holds.
///     </para>
///     <para>
///         <b>The one carve-out, and why no other.</b> The NuGet audit family (NU19xx) is excluded by
///         <em>code</em>, because an advisory's publication date and an audit fetch's network reachability are
///         external, time-varying inputs that say nothing about how this codebase is built while resolution
///         itself succeeded and the model is complete — refusing on one is literally issue #19. The family is
///         <c>level: Warning</c> by default so it does not fire unpromoted, but <c>TreatWarningsAsErrors</c>
///         promotes it, and the code match that was impossible in Roslyn's diagnostic stream is available here.
///         Any <em>other</em> warning promoted to an error (an <c>NU1605</c> downgrade being the common one)
///         gates, deliberately: restore exited non-zero by the operator's own configuration of their own
///         dependency graph rather than on an external input, so their build is broken — and the refusal is
///         loud, names the projects, and has an opt-out.
///     </para>
/// </remarks>
internal static class RestoreFailures
{
    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects whose packages are missing from the model — an
    ///     assets file recording a restore error, or an SDK-style project with no assets file at all —
    ///     ordinal-sorted, deduplicated by this OS's path rule, and empty for a solution that restored cleanly
    ///     (or whose projects were never going to write an assets file).
    /// </summary>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="alreadyFailed">
    ///     The projects <see cref="ProjectLoadFailures" /> has already blamed, subtracted from the answer so
    ///     one project never appears in two lists. It matters because the two are not disjoint by
    ///     construction: a project caught by the loaded-but-empty arm carries neither output path, so
    ///     <see cref="IntermediateOutputTree.AssetsPathsOf" /> degrades to the default location — which may
    ///     well exist and carry an error from a restore that ran before the project stopped evaluating.
    /// </param>
    internal static IReadOnlyList<string> Detect(Solution solution, IReadOnlyList<string> alreadyFailed)
    {
        var blamed = new HashSet<string>(PathComparison.Comparer);

        // Both caches are keyed on the file the verdict is read from rather than on the project, because a
        // multi-target-framework project is several Projects behind one csproj and one assets file — so each
        // file is read once, while a leg that genuinely resolves elsewhere is still read.
        var verdictByAssetsPath = new Dictionary<string, bool>(PathComparison.Comparer);
        var sdkStyleByProjectPath = new Dictionary<string, bool>(PathComparison.Comparer);

        foreach (Project project in solution.Projects)
        {
            if (project.FilePath is not { } filePath) continue;

            // A project in another language reaches the loaded solution — measured on a two-project bed
            // whose .fsproj arrived here with its output paths — and its packages cannot affect a model that
            // never included it. Blaming one refused a solution whose C# half was restored perfectly:
            // measured on that bed, exit 2 naming the .fsproj alone. Skipped before any file is read, so
            // this also spares the assets probe for every project the model does not contain.
            if (!ProjectLanguages.IsCSharp(project)) continue;

            string fullPath = Path.GetFullPath(filePath);
            if (Path.GetDirectoryName(fullPath) is not { } projectDirectory) continue;

            string? assetsPath = FirstExistingAssetsPath(
                projectDirectory, project.OutputFilePath, project.CompilationOutputInfo.AssemblyPath);

            // No assets file at all: the restore never ran, and whether that is a fact about this project
            // depends entirely on whether it is one that would have written one.
            if (assetsPath is null)
            {
                if (IsSdkStyle(sdkStyleByProjectPath, fullPath)) blamed.Add(fullPath);
                continue;
            }

            if (!verdictByAssetsPath.TryGetValue(assetsPath, out bool failed))
            {
                failed = RecordsARestoreError(assetsPath);
                verdictByAssetsPath[assetsPath] = failed;
            }

            // The set collapses a multi-target-framework project to the one csproj a reader would go and fix,
            // exactly as ProjectLoadFailures does.
            if (failed) blamed.Add(fullPath);
        }

        blamed.ExceptWith(alreadyFailed);

        return blamed
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsSdkStyle(Dictionary<string, bool> cache, string projectFilePath)
    {
        if (cache.TryGetValue(projectFilePath, out bool sdkStyle)) return sdkStyle;

        sdkStyle = SdkStyleProject.IsSdkStyle(projectFilePath);
        cache[projectFilePath] = sdkStyle;
        return sdkStyle;
    }

    private static string? FirstExistingAssetsPath(
        string projectDirectory, string? evaluatedOutputPath, string? intermediateAssemblyPath)
    {
        return IntermediateOutputTree
            .AssetsPathsOf(projectDirectory, evaluatedOutputPath, intermediateAssemblyPath)
            .FirstOrDefault(File.Exists);
    }

    // Whether the assets file records a restore error: any logs entry whose level is Error and whose code is
    // not an audit one. Every fault degrades to false — see the class remarks on why silence is the safe
    // answer for a file that could not be read.
    //
    // Read rather than parsed, because of the file this asks about: a real project.assets.json is one to five
    // megabytes of resolved dependency graph, there is one per project, and a cold load reads every one of
    // them — to answer a question about a single top-level property. A JsonDocument would index every token
    // in the file first. The reader still walks the whole document, so the degradation is unchanged.
    private static bool RecordsARestoreError(string assetsPath)
    {
        try
        {
            ReadOnlySpan<byte> preamble = Encoding.UTF8.Preamble;
            ReadOnlySpan<byte> json = File.ReadAllBytes(assetsPath);

            // Every assets file a real restore writes carries a UTF-8 BOM. The stream parse this replaces
            // skipped it; a reader over bytes does not, and would refuse the exact shape production meets.
            if (json.StartsWith(preamble)) json = json[preamble.Length..];

            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

            var failed = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                bool isLogs = reader.ValueTextEquals("logs"u8);
                reader.Read();
                if (isLogs)
                {
                    // The first 'logs' decides and nothing after it can revise the verdict — the same
                    // first-wins rule TryGetProperty applied to a duplicated property name.
                    failed = reader.TokenType == JsonTokenType.StartArray && AnyRestoreError(ref reader);
                    break;
                }

                reader.Skip();
            }

            // The remainder is still read, so a file malformed past the point the verdict was reached lands
            // on "not failed" rather than letting half a document decide — what parsing gave for free.
            while (reader.Read())
            {
            }

            return failed;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException
                                       or SecurityException
                                       or ArgumentException
                                       or JsonException)
        {
            return false;
        }
    }

    // Whether any entry of the logs array the reader is positioned at records a restore error. Anything that
    // is not an object is skipped rather than refused, exactly as the element-kind test it replaces did.
    private static bool AnyRestoreError(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            if (IsRestoreError(ref reader)) return true;
        }

        return false;
    }

    // The per-entry predicate over the object the reader is positioned at: level is the string Error, and the
    // code — where there is one — is not an audit code. Each name is taken first-wins, which is how
    // TryGetProperty resolved a duplicate; a value of any other kind reads as absent.
    private static bool IsRestoreError(ref Utf8JsonReader reader)
    {
        var levelSeen = false;
        var codeSeen = false;
        var levelIsError = false;
        string? code = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            bool isLevel = !levelSeen && reader.ValueTextEquals("level"u8);
            bool isCode = !codeSeen && reader.ValueTextEquals("code"u8);
            reader.Read();

            if (isLevel)
            {
                levelSeen = true;
                levelIsError = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("Error"u8);
            }
            else if (isCode)
            {
                codeSeen = true;
                code = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }

            reader.Skip();
        }

        return levelIsError && (code is null || !NuGetAuditDiagnostics.IsAuditCode(code));
    }
}
