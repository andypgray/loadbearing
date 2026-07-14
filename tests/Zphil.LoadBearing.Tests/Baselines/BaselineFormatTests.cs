using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Tests.Baselines;

/// <summary>
///     Pins the canonical baseline file format and its digest (Spec 1): full-text canonical bytes with
///     a literal digest, ordinal sorting of rules and entries, the one-line empty-array form, LF/no-BOM/
///     trailing-newline invariants, the JSON escaper, the digest-input grammar (including the optional
///     <c>because</c> attribution line), and a self-verifying digest computed independently inline.
///     Moving one of these is a deliberate act.
/// </summary>
public sealed class BaselineFormatTests
{
    private static Dictionary<string, IReadOnlyCollection<BaselineEntry>> Rules(
        params (string Id, BaselineEntry[] Entries)[] rules)
    {
        return rules.ToDictionary(r => r.Id, r => (IReadOnlyCollection<BaselineEntry>)r.Entries, StringComparer.Ordinal);
    }

    [Fact]
    public void ComposeFile_SingleEdgeEntry_MatchesPinnedCanonicalText()
    {
        string composed = BaselineFormat.ComposeFile(Rules((
            "data-access/no-inline-sql",
            [BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")])));

        composed.ShouldBe(
            "{\n" +
            "  \"schemaVersion\": 1,\n" +
            "  \"digest\": \"5a2d44770632c921c27c338f9178d1e2d4d76b775056f17a54f39e33b8a40eaf\",\n" +
            "  \"rules\": {\n" +
            "    \"data-access/no-inline-sql\": {\n" +
            "      \"entries\": [\n" +
            "        { \"source\": \"T:MyApp.Web.InvoiceController\", \"target\": \"T:System.Data.DataTable\" }\n" +
            "      ]\n" +
            "    }\n" +
            "  }\n" +
            "}\n");
    }

    [Fact]
    public void ComposeFile_UnsortedInput_SortsRulesAndEntriesOrdinal()
    {
        string composed = BaselineFormat.ComposeFile(Rules(
            ("z/rule", [BaselineEntry.ForSubject("T:N.Beta"), BaselineEntry.ForSubject("T:N.Alpha")]),
            ("a/rule", [BaselineEntry.ForEdge("T:N.Src2", "T:N.Tgt"), BaselineEntry.ForEdge("T:N.Src1", "T:N.Tgt")])));

        // Rules ascend (a/rule before z/rule); within each, entries ascend by ((source|subject), target).
        int aRule = composed.IndexOf("a/rule", StringComparison.Ordinal);
        int zRule = composed.IndexOf("z/rule", StringComparison.Ordinal);
        aRule.ShouldBeLessThan(zRule);
        composed.IndexOf("T:N.Src1", StringComparison.Ordinal)
            .ShouldBeLessThan(composed.IndexOf("T:N.Src2", StringComparison.Ordinal));
        composed.IndexOf("T:N.Alpha", StringComparison.Ordinal)
            .ShouldBeLessThan(composed.IndexOf("T:N.Beta", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposeFile_EmptyEntries_RendersEmptyArrayOnOneLine()
    {
        string composed = BaselineFormat.ComposeFile(Rules(("data-access/no-inline-sql", [])));

        composed.ShouldContain("      \"entries\": []\n");
    }

    [Fact]
    public void ComposeFile_Always_LfNoBomTrailingNewline()
    {
        string composed = BaselineFormat.ComposeFile(Rules((
            "r/x", [BaselineEntry.ForEdge("T:A", "T:B")])));
        byte[] bytes = Encoding.UTF8.GetBytes(composed);

        bytes.Take(3).ShouldNotBe([0xEF, 0xBB, 0xBF]); // no UTF-8 BOM
        composed.ShouldNotContain("\r"); // LF only
        composed.EndsWith("\n", StringComparison.Ordinal).ShouldBeTrue(); // trailing newline
    }

    [Fact]
    public void ComposeFile_EntryNeedingJsonEscape_EscapesQuoteBackslashAndControls()
    {
        // Real symbol IDs never carry these, but the escaper must be honest. Built from explicit code
        // points to keep the source free of invisible control chars: quote, backslash, TAB (0x09, a
        // named escape) and U+0001 (0x01, which falls through to the \u00XX form).
        string subject = "a\"b\\c" + (char)0x09 + "d" + (char)0x01 + "e";
        string composed = BaselineFormat.ComposeFile(Rules(("r/x", [BaselineEntry.ForSubject(subject)])));

        composed.ShouldContain("{ \"subject\": \"a\\\"b\\\\c\\td\\u0001e\" }");
    }

    [Fact]
    public void DigestInput_EdgeAndSubjectEntries_MatchesPinnedGrammar()
    {
        string input = BaselineFormat.DigestInput(Rules((
            "r/x", [BaselineEntry.ForEdge("T:A", "T:B"), BaselineEntry.ForSubject("T:C")])));

        input.ShouldBe(
            "loadbearing-baseline-digest-v1\n" +
            "rule r/x\n" +
            "edge T:A -> T:B\n" +
            "subject T:C\n");
    }

    [Fact]
    public void ComputeDigest_KnownInput_MatchesIndependentSha256()
    {
        var rules = Rules(
            ("b/two", [BaselineEntry.ForSubject("T:N.Two")]),
            ("a/one", [BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")]));

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(BaselineFormat.DigestInput(rules)));
        string independent = string.Concat(hash.Select(b => b.ToString("x2")));

        BaselineFormat.ComputeDigest(rules).ShouldBe(independent);
    }

    [Fact]
    public void ComposeFile_AttributedEdgeAndSubject_RenderBecauseLastOnOneLine()
    {
        string composed = BaselineFormat.ComposeFile(Rules(("r/x",
        [
            BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt").WithBecause("INC-1234"),
            BaselineEntry.ForSubject("T:N.Sub").WithBecause("keep until migration")
        ])));

        composed.ShouldContain("        { \"source\": \"T:N.Src\", \"target\": \"T:N.Tgt\", \"because\": \"INC-1234\" }");
        composed.ShouldContain("        { \"subject\": \"T:N.Sub\", \"because\": \"keep until migration\" }");
    }

    [Fact]
    public void DigestInput_AttributedEntries_EmitBecauseLineAfterOwnLine()
    {
        string input = BaselineFormat.DigestInput(Rules((
            "r/x", [BaselineEntry.ForEdge("T:A", "T:B").WithBecause("INC-1234"), BaselineEntry.ForSubject("T:C")])));

        input.ShouldBe(
            "loadbearing-baseline-digest-v1\n" +
            "rule r/x\n" +
            "edge T:A -> T:B\n" +
            "because INC-1234\n" +
            "subject T:C\n");
    }

    [Fact]
    public void ComputeDigest_AttributedVsUnattributed_Differ()
    {
        string plain = BaselineFormat.ComputeDigest(Rules(("r/x", [BaselineEntry.ForEdge("T:A", "T:B")])));
        string attributed = BaselineFormat.ComputeDigest(Rules((
            "r/x", [BaselineEntry.ForEdge("T:A", "T:B").WithBecause("INC-1234")])));
        string otherText = BaselineFormat.ComputeDigest(Rules((
            "r/x", [BaselineEntry.ForEdge("T:A", "T:B").WithBecause("INC-9999")])));

        attributed.ShouldNotBe(plain);
        attributed.ShouldNotBe(otherText);
    }

    [Fact]
    public void ComposeFile_FixtureBaselines_ReproduceCheckedInFiles()
    {
        // The checked-in fixture baselines are authored FROM the composer, never by hand — this keeps
        // them honest. Compared after CRLF normalization (core.autocrlf may check them out as CRLF).
        string violated = BaselineFormat.ComposeFile(Rules((
            "data-access/no-inline-sql",
            [BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")])));
        Normalize(ReadFixture("arch", "baselines", "data-access", "no-inline-sql.json")).ShouldBe(violated);

        string clean = BaselineFormat.ComposeFile(Rules((
            "data-access/no-inline-sql",
            [
                BaselineEntry.ForEdge("T:MyApp.Web.HomeController", "T:System.Data.DataTable"),
                BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
            ])));
        Normalize(ReadFixture("arch", "clean-baseline.json")).ShouldBe(clean);
    }

    private static string ReadFixture(params string[] relativeParts)
    {
        var segments = new List<string> { AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp" };
        segments.AddRange(relativeParts);
        return File.ReadAllText(Path.Combine(segments.ToArray()));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n");
    }
}