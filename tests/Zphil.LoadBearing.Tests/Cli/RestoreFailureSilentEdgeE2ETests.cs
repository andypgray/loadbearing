using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The silent edge, made a permanent fact: a rule that reds on the restored tree does not pass on the
///     same tree with the restore broken, and the cure it prints while inert names the partial model rather
///     than the shape of its own selection.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why the defect needs a bed of its own.</b> With the restore broken, the external edge a
///         violation rests on is never extracted, so the rule reports itself inert and the run passes —
///         the violation is not missed; the survey it rests on is. Nothing shorter than a real restore
///         can hold that: every cheaper surface pin downstream of the gate takes the project list as
///         given, and the whole question here is whether the list is produced at all.
///     </para>
///     <para>
///         <b>Hermetic, with no committed binary.</b> The package is manufactured into a folder feed at test
///         time, <c>&lt;clear/&gt;</c> in the bed's <c>NuGet.config</c> drops every other source, and
///         <c>NUGET_PACKAGES</c> points at a directory under this copy — emptied before each arm, so both
///         restore from cold. That last part is what removes the confound the field measurement had to design
///         around: an operator's warm global package folder satisfies a restore whose feed is unreachable, and
///         the bed would then prove nothing.
///     </para>
///     <para>
///         <b>Three arms, and the third is nearly free.</b> Restored, broken, and never restored at all. The
///         third needs no feed, no restore and no network — the copy simply arrives without an <c>obj/</c>,
///         which is the state a fresh clone and a cold CI runner are both in — and it is the arm where the
///         model is missing exactly the same edges with nothing written down anywhere to say so. It shares
///         this bed rather than getting one of its own because the comparison is the point: one tree, one
///         spec, and the only difference between the arms is what a restore did or did not leave behind.
///     </para>
///     <para>
///         <b>Nothing is built.</b> The bed needs a restore, not a compile: <c>MSBuildWorkspace</c>'s
///         design-time build resolves package assets out of <c>project.assets.json</c>, which is exactly the
///         file the arms differ in. The spec is compiled in-process by
///         <see cref="SpecAssemblyCompiler" /> and passed as an explicit DLL, so no project in the bed is ever
///         built either.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class RestoreFailureSilentEdgeE2ETests
{
    private const string PackageId = "Contoso.Json";

    private const string PackageVersion = "1.0.0";

    private const string RuleId = "fieldmini/core-no-json";

    // The stub package's whole content: one public static method, on a type in a namespace the spec bans.
    private const string PackageSource =
        """
        namespace Contoso.Json
        {
            public static class JsonWriter
            {
                public static string Write(object value) { return value == null ? "null" : value.ToString(); }
            }
        }
        """;

    private const string SpecSource =
        $$"""
          using Zphil.LoadBearing;

          namespace FieldMiniSpecAssembly
          {
              public sealed class FieldMiniSpec : IArchitectureSpec
              {
                  public void Define(Arch arch)
                  {
                      arch.Rule("{{RuleId}}")
                          .Enforce(arch.Project("FieldMini.Core").MustNotReference(arch.Namespace("Contoso.*")))
                          .Because("The core must not serialize through a third-party writer.");
                  }
              }
          }
          """;

    // The unreachable source the broken arm restores against. A DNS name under .invalid can never resolve
    // (RFC 2606), so the failure is deterministic and offline rather than dependent on what a network does.
    private const string BrokenNuGetConfig =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
            <packageSources>
                <clear />
                <add key="field-mini-broken" value="https://nuget.fieldtest.invalid/v3/index.json" />
            </packageSources>
        </configuration>
        """;

    // The spec assembly, compiled once for the whole class: every arm passes the same --spec, and the
    // compilation drags in the full platform-assembly closure. Each arm still writes the bytes into its own
    // workspace, because --spec takes a path.
    private static readonly Lazy<byte[]> SpecImage = new(() => SpecAssemblyCompiler.EmitImage(SpecSource, "FieldMiniSpec"));

    // The stub package, assembled once for the same reason — one nuspec and one compiled assembly, neither of
    // which varies by arm.
    private static readonly Lazy<byte[]> PackageImage = new(BuildStubPackage);

    [Fact]
    public void Check_SameTreeRestoredThenBroken_RedsThenRefusesAndNeverPasses()
    {
        // One test, both arms, deliberately: the fact is a comparison, and split across two tests either half
        // could drift into passing for its own reasons while the pair stopped meaning anything.
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(
            "RestoreFailureSolutions/FieldMini", "FieldMini.sln", false);

        string packagesRoot = workspace.PathOf("packages");
        using var scopedPackages = new ScopedEnvironmentVariable("NUGET_PACKAGES", packagesRoot);

        WriteStubPackage(workspace.PathOf("feed"));
        string specDll = WriteSpec(workspace);

        // ── the good arm: the feed is reachable, so the package edge is in the model and the rule reds ──
        RestoreFromCold(workspace, packagesRoot)
            .ExitCode.ShouldBe(0);

        CliResult restored = Check(workspace, specDll);

        restored.ShouldReportViolations($"FAIL {RuleId}", "JsonUser.cs");

        // ── the broken arm: the same tree, the same spec, no rebuild — only the feed moves ──
        File.WriteAllText(workspace.PathOf("NuGet.config"), BrokenNuGetConfig);

        RestoreFromCold(workspace, packagesRoot)
            .ExitCode.ShouldNotBe(0);

        CliResult broken = Check(workspace, specDll);

        // The assertion that carries the whole bed. Exit 0 here is the measured defect: the rule reported
        // itself inert — its target selection matched no types — because the edge it rests on was never
        // extracted, and a green run said so to CI.
        broken.Exit.ShouldNotBe(0, Transcript(broken));
        broken.ShouldRefuseWith("NuGet packages did not resolve for 1 project", "dotnet restore");
    }

    [Fact]
    public void Check_SameTreeNeverRestoredAtAll_RefusesWithoutAFeedOrARestore()
    {
        // The third arm, and the cheapest: no feed, no restore, no network. A restore that never ran leaves
        // the model in exactly the state a failed one does — every package edge missing — but leaves no
        // assets file to read it off, so this arm used to exit 0 with the rule reporting itself inert. What
        // decides it is the csproj: an SDK-style project writes an assets file on every restore, so not
        // having one is a fact about this project rather than a shrug.
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(
            "RestoreFailureSolutions/FieldMini", "FieldMini.sln", false);

        // The bed's whole premise, asserted rather than assumed: TempFixtureWorkspace skips bin/ and obj/ when
        // it copies, and a fixture that arrived with an obj/ would make this pass for the wrong reason.
        File.Exists(workspace.PathOf("FieldMini.Core", "obj", "project.assets.json"))
            .ShouldBeFalse();

        CliResult never = Check(workspace, WriteSpec(workspace));

        never.ShouldRefuseWith(
            "NuGet packages did not resolve for 1 project", "FieldMini.Core.csproj", "dotnet restore");
        // No restore ran, so there are no NuGet logs for the SDK to replay and no warnings to point at. The
        // refusal has to be honest about that rather than sending the reader up the terminal.
        never.Err.ShouldNotContain("See the warnings above", customMessage: Transcript(never));
        // check renders before it gates, so the rule's own report is on stdout beside the refusal — and this
        // is the one place the inert warning's cure is read. Without the stamp reaching it, the cure here was
        // about namespace globs: advice to check the selection against what the solution declares, for a
        // selection that came up empty because a package never resolved.
        never.Out.ShouldContain(
            "hint: The model is incomplete: NuGet packages did not resolve for 1 project, so this selection "
            + "may name types or rest on references that were never extracted; restore the solution "
            + "(dotnet restore), then re-check before reading this as a spec defect.",
            customMessage: Transcript(never));
        never.Out.ShouldNotContain("never crosses a dot", customMessage: Transcript(never));
    }

    [Fact]
    public void CheckJson_NeverRestored_NamesTheProjectInTheSameSlotAsAFailedRestore()
    {
        // One slot for both causes, pinned from the machine-readable side: a client cannot tell a restore
        // that failed from one that never ran, and has nothing different to do with the answer — the remedy
        // is dotnet restore either way and the hole in the model is identical. What it must be able to tell
        // is this from a load failure, which is why failedProjects stays absent.
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(
            "RestoreFailureSolutions/FieldMini", "FieldMini.sln", false);

        CliResult never = Check(workspace, WriteSpec(workspace), "--json");

        never.ShouldRefuseWith();
        using JsonDocument document = JsonDocument.Parse(never.Out);
        document.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse(Transcript(never));
        CheckJson.Strings(document, "restoreFailedProjects")
            .ShouldBe(["FieldMini.Core/FieldMini.Core.csproj"]);
    }

    [Fact]
    public void CheckJson_RestoreBroken_NamesTheProjectInItsOwnDocumentSlot()
    {
        // The machine-readable half. An MCP client and a hook both read the document rather than an exit
        // code, so the fact has to reach them there — in its own slot, because these projects loaded and
        // calling them failed loads would be false.
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(
            "RestoreFailureSolutions/FieldMini", "FieldMini.sln", false);

        string packagesRoot = workspace.PathOf("packages");
        using var scopedPackages = new ScopedEnvironmentVariable("NUGET_PACKAGES", packagesRoot);

        WriteStubPackage(workspace.PathOf("feed"));
        string specDll = WriteSpec(workspace);
        File.WriteAllText(workspace.PathOf("NuGet.config"), BrokenNuGetConfig);

        RestoreFromCold(workspace, packagesRoot)
            .ExitCode.ShouldNotBe(0);

        CliResult broken = Check(workspace, specDll, "--json");

        broken.ShouldRefuseWith();
        using JsonDocument document = JsonDocument.Parse(broken.Out);
        document.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse(Transcript(broken));
        CheckJson.Strings(document, "restoreFailedProjects")
            .ShouldBe(["FieldMini.Core/FieldMini.Core.csproj"]);
        // The same fact the stamp above carries, reaching the rule that reported itself inert: the warning's
        // own cure names the partial model rather than the selection's shape, so a client reading one rule's
        // warning does not have to correlate it with a document-level flag to know what it means.
        document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Single(rule => rule.GetProperty("id")
                .GetString() == RuleId)
            .GetProperty("warnings")[0]
            .GetProperty("hint")
            .GetString()
            .ShouldBe(
                "The model is incomplete: NuGet packages did not resolve for 1 project, so this selection may "
                + "name types or rest on references that were never extracted; restore the solution "
                + "(dotnet restore), then re-check before reading this as a spec defect.",
                Transcript(broken));
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // Cold every time: an arm restoring out of a package folder a previous arm filled would succeed against
    // an unreachable feed, and the bed would prove nothing. Both arms therefore start from no packages at
    // all, which is also the CI shape this models.
    private static ChildProcess.ProcessResult RestoreFromCold(TempFixtureWorkspace workspace, string packagesRoot)
    {
        if (Directory.Exists(packagesRoot)) ReadOnlyTolerant.DeleteTree(packagesRoot);

        ProcessStartInfo startInfo = new("dotnet", "restore FieldMini.sln")
        {
            WorkingDirectory = Path.GetDirectoryName(workspace.SolutionPath)!
        };
        DotnetCli.ApplyCleanSdkEnvironment(startInfo);
        startInfo.Environment["NUGET_PACKAGES"] = packagesRoot;

        return ChildProcess.Run(startInfo);
    }

    // Cold, not warm: the arms differ only in what a restore wrote or never wrote, and a pooled session that
    // served one arm another's snapshot would hide exactly the thing under test.
    private static CliResult Check(TempFixtureWorkspace workspace, string specDll, params string[] extra)
    {
        string[] arguments =
            ["check", workspace.SolutionPath, "--spec", specDll, "--no-cache", .. extra];
        return CliRunner.InvokeColdAsync(arguments)
            .GetAwaiter()
            .GetResult();
    }

    // The spec this arm passes as --spec, written into its own workspace out of the one compilation.
    private static string WriteSpec(TempFixtureWorkspace workspace)
    {
        string specDirectory = workspace.PathOf("spec");
        Directory.CreateDirectory(specDirectory);
        string specDll = Path.Combine(specDirectory, "FieldMiniSpec.dll");
        File.WriteAllBytes(specDll, SpecImage.Value);
        return specDll;
    }

    // A folder feed with one package in it, out of the same one image every arm restores from.
    private static void WriteStubPackage(string feedDirectory)
    {
        Directory.CreateDirectory(feedDirectory);
        File.WriteAllBytes(Path.Combine(feedDirectory, $"{PackageId}.{PackageVersion}.nupkg"), PackageImage.Value);
    }

    // Assembled here rather than through `dotnet pack`: a pack would need its own project, its own restore
    // and its own feed to restore from, all to produce a zip holding one nuspec and one assembly.
    private static byte[] BuildStubPackage()
    {
        byte[] assembly = SpecAssemblyCompiler.EmitImage(
            PackageSource, PackageId, SpecAssemblyCompiler.PlatformReferences);

        using var buffer = new MemoryStream();
        using (var package = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            Write(package, $"{PackageId}.nuspec", Encoding.UTF8.GetBytes(Nuspec));
            Write(package, $"lib/net10.0/{PackageId}.dll", assembly);
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive package, string entryName, byte[] content)
    {
        using Stream entry = package.CreateEntry(entryName)
            .Open();
        entry.Write(content, 0, content.Length);
    }

    private static string Nuspec =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
             <metadata>
                 <id>{PackageId}</id>
                 <version>{PackageVersion}</version>
                 <authors>LoadBearing tests</authors>
                 <description>A stub package manufactured by RestoreFailureSilentEdgeE2ETests.</description>
                 <dependencies>
                     <group targetFramework="net10.0" />
                 </dependencies>
             </metadata>
         </package>
         """;

    // Both channels, because a run that answered the wrong exit code answered it for a reason that is on one
    // of them, and which one is not knowable in advance.
    private static string Transcript(CliResult result)
    {
        return $"exit {result.Exit}\n--- stdout ---\n{result.Out}\n--- stderr ---\n{result.Err}";
    }
}
