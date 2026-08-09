using Shouldly;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The committed golden renders under <c>Cli/Golden/</c>, copied to the test output beside the assembly,
///     and the comparison that names the file to update when one reds.
/// </summary>
internal static class GoldenFile
{
    /// <summary>The golden's text, as committed.</summary>
    internal static string Read(string fileName)
    {
        return File.ReadAllText(GoldenPath(fileName));
    }

    /// <summary>
    ///     Asserts <paramref name="actual" /> matches the golden, line endings and surrounding whitespace
    ///     aside. The failure names the golden's absolute path, which is the first thing a reader needs.
    /// </summary>
    internal static void ShouldMatchGolden(this string actual, string fileName)
    {
        actual.NormalizedTrimmed().ShouldBe(
            Read(fileName).NormalizedTrimmed(),
            $"The render no longer matches the golden. Update it deliberately: {GoldenPath(fileName)}");
    }

    private static string GoldenPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Cli", "Golden", fileName);
    }
}
