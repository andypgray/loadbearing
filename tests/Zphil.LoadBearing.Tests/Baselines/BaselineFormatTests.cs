using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Baselines;

/// <summary>
///     Pins the canonical baseline file format and its digest (Spec 1): full-text canonical bytes with
///     a literal digest, ordinal sorting of rules and entries, the one-line empty-array form, LF/no-BOM/
///     trailing-newline invariants, the JSON escaper, the digest-input grammar (including the optional
///     <c>siteCount</c> measure and <c>because</c> attribution lines), the frozen legacy grammar a
///     stored file is still verified against, and a self-verifying digest computed independently inline.
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
            "  \"schemaVersion\": 2,\n" +
            "  \"digest\": \"0f0c007efe321723b9603501e583b3e67615b1ec4ddce976b8c387392429de25\",\n" +
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

        bytes.Take(3)
            .ShouldNotBe([0xEF, 0xBB, 0xBF]); // no UTF-8 BOM
        composed.ShouldNotContain("\r"); // LF only
        composed.EndsWith("\n", StringComparison.Ordinal)
            .ShouldBeTrue(); // trailing newline
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
    public void ComposeFile_SubjectWithNamedControlChars_EscapesBackspaceFormfeedNewlineReturnTabAndUnicode()
    {
        // The subject field reaches Quote unfiltered (ForSubject validates nothing; WithBecause would reject the
        // newline). \b \f \n \r \t are the named JSON escapes (BaselineFormat.cs:236-250); U+001F (<0x20) falls
        // through to \uXXXX. Built from explicit code points to keep the source free of invisible control chars.
        string subject = "a" + (char)0x08 + (char)0x0C + (char)0x0A + (char)0x0D + (char)0x09 + (char)0x1F + "z";
        string composed = BaselineFormat.ComposeFile(Rules(("r/x", [BaselineEntry.ForSubject(subject)])));

        composed.ShouldContain("\"a\\b\\f\\n\\r\\t\\u001fz\"");

        // The escapes are valid JSON that round-trips back to the original subject through a standard parser.
        using JsonDocument document = JsonDocument.Parse(composed);
        string roundTripped = document.RootElement
            .GetProperty("rules")
            .GetProperty("r/x")
            .GetProperty("entries")[0]
            .GetProperty("subject")
            .GetString()!;
        roundTripped.ShouldBe(subject);
    }

    [Fact]
    public void DigestInput_EdgeAndSubjectEntries_MatchesPinnedGrammar()
    {
        string input = BaselineFormat.DigestInput(Rules((
            "r/x", [BaselineEntry.ForEdge("T:A", "T:B"), BaselineEntry.ForSubject("T:C")])));

        input.ShouldBe(
            "loadbearing-baseline-digest-v2\n" +
            "rule r/x\n" +
            "edge T:A -> T:B\n" +
            "subject T:C\n");
    }

    [Fact]
    public void DigestInput_CountedEdgeEntry_EmitsSiteCountLineBetweenTheEdgeAndItsBecause()
    {
        // The v2 grammar's whole addition, in the order it is read: the measure sits on its own line
        // immediately after the entry it measures and before that entry's attribution. Entries still
        // arrive in canonical order, which is why the subject — sorting ordinal before the edge's source
        // — leads here whatever order the input named them in.
        string input = BaselineFormat.DigestInput(Rules((
            "data-access/no-inline-sql", [
                BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
                    .WithSiteCount(2)
                    .WithBecause("INC-1234"),
                BaselineEntry.ForSubject("T:MyApp.Domain.OrderService")
            ])));

        input.ShouldBe(
            "loadbearing-baseline-digest-v2\n" +
            "rule data-access/no-inline-sql\n" +
            "subject T:MyApp.Domain.OrderService\n" +
            "edge T:MyApp.Web.InvoiceController -> T:System.Data.DataTable\n" +
            "siteCount 2\n" +
            "because INC-1234\n");
    }

    [Fact]
    public void DigestInput_LegacyVersion_IsTheFrozenGrammarWithNoSiteCountLine()
    {
        // The v1 grammar is frozen verbatim — its own preamble, and no measure line even for an entry
        // that carries one. That is what lets a file written before the measure existed still verify
        // against the digest it stored, which is the whole of the transparent legacy read.
        string input = BaselineFormat.DigestInput(
            Rules((
                "r/x", [
                    BaselineEntry.ForEdge("T:A", "T:B")
                        .WithSiteCount(2)
                        .WithBecause("INC-1234"),
                    BaselineEntry.ForSubject("T:C")
                ])),
            BaselineFormat.LegacySchemaVersion);

        input.ShouldBe(
            "loadbearing-baseline-digest-v1\n" +
            "rule r/x\n" +
            "edge T:A -> T:B\n" +
            "because INC-1234\n" +
            "subject T:C\n");
    }

    [Fact]
    public void ComputeDigest_KnownInput_MatchesIndependentSha256()
    {
        Dictionary<string, IReadOnlyCollection<BaselineEntry>> rules = Rules(
            ("b/two", [BaselineEntry.ForSubject("T:N.Two")]),
            ("a/one", [BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")]));

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(BaselineFormat.DigestInput(rules)));
        string independent = string.Concat(hash.Select(b => b.ToString("x2")));

        BaselineFormat.ComputeDigest(rules)
            .ShouldBe(independent);
    }

    [Fact]
    public void ComposeFile_AttributedEdgeAndSubject_RenderBecauseLastOnOneLine()
    {
        string composed = BaselineFormat.ComposeFile(Rules(("r/x",
        [
            BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
                .WithBecause("INC-1234"),
            BaselineEntry.ForSubject("T:N.Sub")
                .WithBecause("keep until migration")
        ])));

        composed.ShouldContain("        { \"source\": \"T:N.Src\", \"target\": \"T:N.Tgt\", \"because\": \"INC-1234\" }");
        composed.ShouldContain("        { \"subject\": \"T:N.Sub\", \"because\": \"keep until migration\" }");
    }

    [Fact]
    public void ComposeFile_CountedEdgeEntry_RendersSiteCountAfterTargetAndBeforeBecause()
    {
        // Both shapes on one line, in the one order the format allows: the measure closes the identity
        // slots, the attribution closes the entry. An uncounted entry omits the key rather than writing a
        // null, so a burndown diff of a partially upgraded file still moves one line per entry.
        string composed = BaselineFormat.ComposeFile(Rules(("r/x",
        [
            BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
                .WithSiteCount(2),
            BaselineEntry.ForEdge("T:N.Src2", "T:N.Tgt")
                .WithSiteCount(3)
                .WithBecause("INC-1234"),
            BaselineEntry.ForEdge("T:N.Src3", "T:N.Tgt")
        ])));

        composed.ShouldContain("        { \"source\": \"T:N.Src\", \"target\": \"T:N.Tgt\", \"siteCount\": 2 },\n");
        composed.ShouldContain(
            "        { \"source\": \"T:N.Src2\", \"target\": \"T:N.Tgt\", \"siteCount\": 3, \"because\": \"INC-1234\" },\n");
        composed.ShouldContain("        { \"source\": \"T:N.Src3\", \"target\": \"T:N.Tgt\" }\n");
    }

    [Fact]
    public void DigestInput_AttributedEntries_EmitBecauseLineAfterOwnLine()
    {
        string input = BaselineFormat.DigestInput(Rules((
            "r/x", [
                BaselineEntry.ForEdge("T:A", "T:B")
                    .WithBecause("INC-1234"),
                BaselineEntry.ForSubject("T:C")
            ])));

        input.ShouldBe(
            "loadbearing-baseline-digest-v2\n" +
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
            "r/x", [
                BaselineEntry.ForEdge("T:A", "T:B")
                    .WithBecause("INC-1234")
            ])));
        string otherText = BaselineFormat.ComputeDigest(Rules((
            "r/x", [
                BaselineEntry.ForEdge("T:A", "T:B")
                    .WithBecause("INC-9999")
            ])));

        attributed.ShouldNotBe(plain);
        attributed.ShouldNotBe(otherText);
    }

    [Fact]
    public void ComputeDigest_CountedVsUncounted_Differ()
    {
        // The measure is folded into the digest, which is what makes a hand-raised count tamper rather
        // than a silently widened allowance — the one property that stops the ratchet being edited open.
        string uncounted = BaselineFormat.ComputeDigest(Rules(("r/x", [BaselineEntry.ForEdge("T:A", "T:B")])));
        string two = BaselineFormat.ComputeDigest(Rules((
            "r/x", [
                BaselineEntry.ForEdge("T:A", "T:B")
                    .WithSiteCount(2)
            ])));
        string three = BaselineFormat.ComputeDigest(Rules((
            "r/x", [
                BaselineEntry.ForEdge("T:A", "T:B")
                    .WithSiteCount(3)
            ])));

        two.ShouldNotBe(uncounted);
        two.ShouldNotBe(three);
    }

    [Fact]
    public void ComputeDigest_SameEntriesUnderEachVersion_Differ()
    {
        // The preamble carries the version, so one entry set hashes to two values. That is what forces the
        // schemaVersion bump: with the count in the digest but the version unchanged, a tool that predates
        // the measure would read a new file as *tampered* rather than as one it does not understand.
        Dictionary<string, IReadOnlyCollection<BaselineEntry>> rules =
            Rules(("r/x", [BaselineEntry.ForEdge("T:A", "T:B")]));

        BaselineFormat.ComputeDigest(rules, BaselineFormat.SchemaVersion)
            .ShouldNotBe(BaselineFormat.ComputeDigest(rules, BaselineFormat.LegacySchemaVersion));
    }

    [Fact]
    public void ComposeFile_FixtureBaselines_ReproduceCheckedInFiles()
    {
        // The checked-in fixture baselines are authored FROM the composer, never by hand — this keeps
        // them honest. Compared after CRLF normalization (core.autocrlf may check them out as CRLF).
        // Each controller declares the DataTable twice, as a return type and as a construction, on two
        // lines — so every edge entry here grandfathers two sites, and the count is part of the bytes.
        string violated = BaselineFormat.ComposeFile(Rules((
            "data-access/no-inline-sql",
            [
                BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
                    .WithSiteCount(2)
            ])));
        ReadFixture("arch", "baselines", "data-access", "no-inline-sql.json")
            .NormalizedLines()
            .ShouldBe(violated);

        string clean = BaselineFormat.ComposeFile(Rules((
            "data-access/no-inline-sql",
            [
                BaselineEntry.ForEdge("T:MyApp.Web.HomeController", "T:System.Data.DataTable")
                    .WithSiteCount(2),
                BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
                    .WithSiteCount(2)
            ])));
        ReadFixture("arch", "clean-baseline.json")
            .NormalizedLines()
            .ShouldBe(clean);
    }

    private static string ReadFixture(params string[] relativeParts)
    {
        var segments = new List<string> { AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp" };
        segments.AddRange(relativeParts);
        return File.ReadAllText(Path.Combine(segments.ToArray()));
    }
}
