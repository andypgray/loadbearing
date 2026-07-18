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
///     file-naming <see cref="UserErrorException" />; and a file that is not valid UTF-8 — invalid bytes
///     or a UTF-16 BOM — is refused with a file-naming <see cref="UserErrorException" /> and left byte-for-byte
///     untouched (L1), never decoded lossily and rewritten as replacement characters.
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

    [Fact]
    public void Splice_InvalidUtf8File_RefusesAndLeavesFileUntouched()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "AGENTS.md");
            // A lone 0x80 continuation byte with no lead byte — invalid UTF-8 that a lenient decoder would
            // silently turn into U+FFFD and then write back, destroying the file (the L1 defect).
            byte[] invalid = [0x41, 0x80, 0x42];
            File.WriteAllBytes(path, invalid);

            var error = Should.Throw<UserErrorException>(() => ManagedBlockFile.Splice(path, Body));

            error.Message.ShouldBe(
                $"{path}: not valid UTF-8. LoadBearing manages a UTF-8 AGENTS.md block and refuses a file it "
                + "cannot decode rather than rewriting it with replacement characters; convert it to UTF-8 and re-render.");
            File.ReadAllBytes(path).ShouldBe(invalid); // refused, not rewritten
        });
    }

    [Fact]
    public void Splice_Utf16LeBomFile_RefusesFlaggingUtf16()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "AGENTS.md");
            // A UTF-16 LE file: the FF FE BOM then "# Title" as UTF-16. A generic "invalid UTF-8" message
            // would be true but unhelpful; the adapter names the encoding so the fix is obvious.
            byte[] utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("# Title")];
            File.WriteAllBytes(path, utf16);

            var error = Should.Throw<UserErrorException>(() => ManagedBlockFile.Splice(path, Body));

            error.Message.ShouldBe(
                $"{path}: looks like UTF-16 (a leading byte-order mark). LoadBearing manages UTF-8 files only; "
                + "convert it to UTF-8 and re-render.");
            File.ReadAllBytes(path).ShouldBe(utf16); // refused, not rewritten
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