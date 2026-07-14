using System.Text;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The byte-level file adapter (R1): a new file is written UTF-8 with no BOM; an existing file's
///     BOM state is preserved exactly (the pin that <c>File.ReadAllText</c> would silently lose); an
///     unchanged re-splice reports Unchanged with byte-identical output; a malformed block becomes a
///     file-naming <see cref="UserErrorException" />.
/// </summary>
public sealed class ManagedBlockFileTests
{
    private const string Body = "line one\nline two";
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void Splice_NewFile_WritesUtf8WithoutBom()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "AGENTS.md");

            WriteOutcome outcome = ManagedBlockFile.Splice(path, Body);

            outcome.ShouldBe(WriteOutcome.Wrote);
            byte[] bytes = File.ReadAllBytes(path);
            StartsWithBom(bytes).ShouldBeFalse();
            Encoding.UTF8.GetString(bytes).ShouldBe("<!-- loadbearing:begin -->\nline one\nline two\n<!-- loadbearing:end -->\n");
        });
    }

    [Fact]
    public void Splice_ExistingBomFile_PreservesTheBom()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "AGENTS.md");
            byte[] seeded = [.. Utf8Bom, .. Encoding.UTF8.GetBytes("# Title\n")];
            File.WriteAllBytes(path, seeded);

            ManagedBlockFile.Splice(path, Body);

            byte[] bytes = File.ReadAllBytes(path);
            StartsWithBom(bytes).ShouldBeTrue();
            // The BOM never enters the spliced text: the '# Title' heading is preserved once, un-mangled.
            string text = Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length);
            text.ShouldStartWith("# Title\n\n<!-- loadbearing:begin -->");
        });
    }

    [Fact]
    public void Splice_ExistingNoBomFile_StaysWithoutBom()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "AGENTS.md");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("# Title\n"));

            ManagedBlockFile.Splice(path, Body);

            StartsWithBom(File.ReadAllBytes(path)).ShouldBeFalse();
        });
    }

    [Fact]
    public void Splice_UnchangedResplice_ReportsUnchangedAndKeepsBytesIdentical()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "AGENTS.md");
            ManagedBlockFile.Splice(path, Body);
            byte[] afterFirst = File.ReadAllBytes(path);

            WriteOutcome second = ManagedBlockFile.Splice(path, Body);

            second.ShouldBe(WriteOutcome.Unchanged);
            File.ReadAllBytes(path).ShouldBe(afterFirst);
        });
    }

    [Fact]
    public void Splice_MalformedMarkers_ThrowsUserErrorNamingTheFile()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "AGENTS.md");
            File.WriteAllText(path, "<!-- loadbearing:begin -->\nno end marker\n");

            var error = Should.Throw<UserErrorException>(() => ManagedBlockFile.Splice(path, Body));
            error.Message.ShouldContain(path);
        });
    }

    private static bool StartsWithBom(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
    }

    private static void WithTempDir(Action<string> body)
    {
        DirectoryInfo dir = Directory.CreateTempSubdirectory("lb-render-");
        try
        {
            body(dir.FullName);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}