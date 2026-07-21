using System.Reflection;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     When spec discovery's <c>GetTypes()</c> raises a <see cref="ReflectionTypeLoadException" />
///     (a missing or version-mismatched dependency of the spec assembly), the pipeline surfaces the
///     <em>distinct</em> loader messages — deduped and ordinal-sorted so the output is deterministic —
///     under a naming/fix frame, not a raw reflection failure. Pinned with a fabricated exception.
/// </summary>
public sealed class ModelPipelineLoaderFailureTests
{
    [Fact]
    public void LoaderFailureMessage_DistinctLoaderMessages_AreDedupedAndOrdinalSorted()
    {
        var exception = new ReflectionTypeLoadException(
            new Type?[] { null, null, null },
            new Exception?[]
            {
                new TypeLoadException("Could not load type 'Zebra'."),
                new FileNotFoundException("Could not load file or assembly 'Acme'."),
                new FileNotFoundException("Could not load file or assembly 'Acme'.") // duplicate — deduped
            });

        // Build the spec path with the native separator so Path.GetFileName strips the directory on
        // every OS. A literal "C:\out\..." only strips on Windows — on Linux/macOS '\' is not a
        // separator, so the whole string survives and the pinned bare-filename message fails.
        string specDllPath = Path.Combine("out", "Meridian.ArchSpec.dll");

        string message = ModelPipeline.LoaderFailureMessage(exception, specDllPath);

        message.ShouldBe(
            "Could not load spec assembly 'Meridian.ArchSpec.dll'; one or more types failed to load:\n"
            + "  Could not load file or assembly 'Acme'.\n"
            + "  Could not load type 'Zebra'.\n"
            + "Build the spec project and restore its dependencies, then retry.");
    }

    [Fact]
    public void LoaderFailureMessage_NoLoaderDetail_StillNamesTheAssemblyAndFix()
    {
        var exception = new ReflectionTypeLoadException(new Type?[] { null }, new Exception?[] { null });

        string message = ModelPipeline.LoaderFailureMessage(exception, "Spec.dll");

        message.ShouldBe(
            "Could not load spec assembly 'Spec.dll'; one or more types failed to load:\n"
            + "  (the runtime reported no loader detail)\n"
            + "Build the spec project and restore its dependencies, then retry.");
    }
}