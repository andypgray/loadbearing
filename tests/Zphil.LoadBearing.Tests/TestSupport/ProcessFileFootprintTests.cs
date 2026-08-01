using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Proves the instrument on this host before anything relies on it. A handle detector that silently sees
///     nothing reports every process as clean, so both scans are pointed at this process — whose footprint is
///     known by construction — and each is required to find what must be there.
/// </summary>
public sealed class ProcessFileFootprintTests
{
    [Fact]
    public void PathsUnder_TheTestAssemblyDirectory_ReportsMappedImages()
    {
        Assert.SkipUnless(ProcessFileFootprint.IsSupported, "The footprint scan is Windows-x64 only.");

        using var self = Process.GetCurrentProcess();

        var retained =
            ProcessFileFootprint.PathsUnder(self, AppContext.BaseDirectory);

        // This assembly is running: its image is mapped from the directory it was loaded out of, and no file
        // handle on it exists to find — which is exactly why the mapped-view scan has to exist.
        retained.ShouldContain(
            path => path.Scan == FootprintScan.MappedView
                    && path.ResolvedPath.EndsWith("Zphil.LoadBearing.Tests.dll", StringComparison.OrdinalIgnoreCase),
            $"the mapped-view scan did not find this test assembly's own image under "
            + $"'{AppContext.BaseDirectory}'. It found: {Format(retained)}");
    }

    [Fact]
    public void PathsUnder_AnOpenFileStream_ReportsItAndStopsOnceClosed()
    {
        Assert.SkipUnless(ProcessFileFootprint.IsSupported, "The footprint scan is Windows-x64 only.");

        string probeRoot = TestTempRoot.For("footprint-probe");
        string probeFile = Path.Combine(probeRoot, "held.txt");
        File.WriteAllText(probeFile, "held open");

        using var self = Process.GetCurrentProcess();

        using (new FileStream(probeFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var held = ProcessFileFootprint.PathsUnder(self, probeRoot);

            held.ShouldContain(
                path => path.Scan == FootprintScan.Handle
                        && string.Equals(path.ResolvedPath, probeFile, StringComparison.OrdinalIgnoreCase),
                $"the handle scan did not find the open FileStream on '{probeFile}'. It found: {Format(held)}");
        }

        // The other direction, so a scan that reported everything under the sun would fail here: once the
        // stream is closed the same root must come back empty.
        var released = ProcessFileFootprint.PathsUnder(self, probeRoot);
        released.ShouldBeEmpty($"nothing should be held under '{probeRoot}' once the stream is disposed.");
    }

    private static string Format(IReadOnlyList<RetainedPath> retained)
    {
        return retained.Count == 0
            ? "(nothing)"
            : string.Join(", ", retained.Select(path => $"[{path.Scan}] {path.ResolvedPath}"));
    }
}