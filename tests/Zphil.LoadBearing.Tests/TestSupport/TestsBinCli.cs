namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The CLI build output beside the test assembly. The tests project references the CLI project, so
///     <c>loadbearing.dll</c> (plus its runtimeconfig and deps) is copied there — this is the in-repo tool,
///     and a child launched from it maps its images out of the repository's build tree.
/// </summary>
internal static class TestsBinCli
{
    /// <summary>
    ///     The absolute path to <c>loadbearing.dll</c> beside the test assembly, throwing when it is absent
    ///     so no test can pass by running against a stale or missing binary.
    /// </summary>
    internal static string Dll()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "loadbearing.dll");
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"The CLI build output 'loadbearing.dll' was not found beside the test assembly at '{path}'. "
                + "The tests project references Zphil.LoadBearing.Cli, so its output should be copied here.");

        return path;
    }
}
