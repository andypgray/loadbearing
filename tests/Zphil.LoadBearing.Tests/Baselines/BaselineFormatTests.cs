using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Baselines;

/// <summary>
///     Pins the canonical baseline file format and its seals: full-text canonical bytes with a literal
///     seal, ordinal sorting of rules and entries, the one-line empty-array form, LF/no-BOM/
///     trailing-newline invariants, the JSON escaper, the seal-input grammar (including the optional
///     <c>siteCount</c> measure and <c>because</c> attribution lines) and the rule ID it binds in, the
///     frozen legacy whole-file grammar a stored v1 file is still verified against, the inventory of
///     accepted schema versions and the shape each one declares, and a self-verifying seal computed
///     independently inline. Moving one of these is a deliberate act.
/// </summary>
public sealed class BaselineFormatTests
{
    [Fact]
    public void SupportedSchemaVersions_AreExactlyTheOnesWhoseShapeIsDeclaredHere()
    {
        // One version, one shape. The two halves of that claim are written differently: IsSupported
        // enumerates the versions a reader accepts, while CarriesSiteCount and CarriesSeal answer for
        // every version but the legacy one. They agree today only because a reader asks IsSupported
        // first, and nothing makes it — so widening the accepted set without saying what the new
        // number's file looks like leaves it silently wearing the current shape's envelope and keys.
        // That is one version naming two shapes, which is the fault this row exists to make loud.
        (int Version, bool SiteCount, bool Seal)[] declared =
        [
            (BaselineFormat.LegacySchemaVersion, SiteCount: false, Seal: false),
            (BaselineFormat.SchemaVersion, SiteCount: true, Seal: true)
        ];

        int[] versions = declared.Select(shape => shape.Version).ToArray();
        versions.ShouldBeUnique();

        foreach ((int version, bool siteCount, bool seal) in declared)
        {
            BaselineFormat.IsSupported(version)
                .ShouldBeTrue($"schemaVersion {version} has a shape declared here");
            BaselineFormat.CarriesSiteCount(version).ShouldBe(siteCount, $"schemaVersion {version}");
            BaselineFormat.CarriesSeal(version).ShouldBe(seal, $"schemaVersion {version}");
        }

        // The other half, scanned rather than asserted about one number: a version with no row above is
        // not a version this reader accepts. The range reaches either side of the real ones, because the
        // values a malformed file actually carries are the neighbours and the obvious placeholders.
        for (int version = -1; version <= 20; version++)
        {
            if (versions.Contains(version)) continue;

            BaselineFormat.IsSupported(version)
                .ShouldBeFalse($"schemaVersion {version} is accepted but no shape is declared for it");
        }
    }

    [Fact]
    public void ComposeFile_SingleEdgeEntry_MatchesPinnedCanonicalText()
    {
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules((
            "data-access/no-inline-sql",
            [BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")])));

        // No whole-file digest in the envelope: an entry's integrity rides on the entry's own line. That is
        // what lets two branches reduce two rules of one file and merge with nothing in common to conflict
        // on, and what leaves a conflict over one rule resolvable by keeping whole lines.
        composed.ShouldBe(
            "{\n" +
            "  \"schemaVersion\": 2,\n" +
            "  \"rules\": {\n" +
            "    \"data-access/no-inline-sql\": {\n" +
            "      \"entries\": [\n" +
            "        { \"source\": \"T:MyApp.Web.InvoiceController\", \"target\": \"T:System.Data.DataTable\", " +
            "\"seal\": \"d1146eb919c14754\" }\n" +
            "      ]\n" +
            "    }\n" +
            "  }\n" +
            "}\n");
    }

    [Fact]
    public void ComposeFile_UnsortedInput_SortsRulesAndEntriesOrdinal()
    {
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules(
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
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules(("data-access/no-inline-sql", [])));

        composed.ShouldContain("      \"entries\": []\n");
    }

    [Fact]
    public void ComposeFile_Always_LfNoBomTrailingNewline()
    {
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules((
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
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules(("r/x", [BaselineEntry.ForSubject(subject)])));

        composed.ShouldContain("{ \"subject\": \"a\\\"b\\\\c\\td\\u0001e\"");
    }

    [Fact]
    public void ComposeFile_SubjectWithNamedControlChars_EscapesBackspaceFormfeedNewlineReturnTabAndUnicode()
    {
        // The subject field reaches Quote unfiltered (ForSubject validates nothing; WithBecause would reject the
        // newline). \b \f \n \r \t are the named JSON escapes (BaselineFormat.Quote); U+001F (<0x20) falls
        // through to \uXXXX. Built from explicit code points to keep the source free of invisible control chars.
        string subject = "a" + (char)0x08 + (char)0x0C + (char)0x0A + (char)0x0D + (char)0x09 + (char)0x1F + "z";
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules(("r/x", [BaselineEntry.ForSubject(subject)])));

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
    public void SealInput_EdgeAndSubjectEntries_MatchPinnedGrammar()
    {
        // A seal covers one entry, so the rule ID leads each rendering rather than heading a section of
        // them. That is what binds an entry to the section it sits in.
        BaselineFormat.SealInput("r/x", BaselineEntry.ForEdge("T:A", "T:B"))
            .ShouldBe(
                "loadbearing-baseline-seal-v2\n" +
                "rule r/x\n" +
                "edge T:A -> T:B\n");

        BaselineFormat.SealInput("r/x", BaselineEntry.ForSubject("T:C"))
            .ShouldBe(
                "loadbearing-baseline-seal-v2\n" +
                "rule r/x\n" +
                "subject T:C\n");
    }

    [Fact]
    public void SealInput_CountedAndAttributedEdge_EmitsSiteCountLineBetweenTheEdgeAndItsBecause()
    {
        // Both riders, in the order they are read: the measure sits on its own line immediately after the
        // entry it measures and before that entry's attribution.
        BaselineEntry entry = BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
            .WithSiteCount(2)
            .WithBecause("INC-1234");

        BaselineFormat.SealInput("data-access/no-inline-sql", entry)
            .ShouldBe(
                "loadbearing-baseline-seal-v2\n" +
                "rule data-access/no-inline-sql\n" +
                "edge T:MyApp.Web.InvoiceController -> T:System.Data.DataTable\n" +
                "siteCount 2\n" +
                "because INC-1234\n");
    }

    [Fact]
    public void SealInput_AttributedEntry_EmitsBecauseLineAfterItsOwn()
    {
        BaselineEntry entry = BaselineEntry.ForSubject("T:C")
            .WithBecause("INC-1234");

        BaselineFormat.SealInput("r/x", entry)
            .ShouldBe(
                "loadbearing-baseline-seal-v2\n" +
                "rule r/x\n" +
                "subject T:C\n" +
                "because INC-1234\n");
    }

    [Fact]
    public void LegacyDigestInput_Always_IsTheFrozenGrammarWithNoSiteCountLine()
    {
        // The v1 grammar is frozen verbatim — its own preamble, a rule line heading a whole section, and no
        // measure line even for an entry that carries one. That is what lets a file written before the
        // measure existed still verify against the digest it stored, which is the whole of the transparent
        // legacy read. It is also the only whole-file digest that will ever exist, which is what the verb's
        // name says.
        string input = BaselineFormat.LegacyDigestInput(BaselineComposer.Rules((
            "r/x", [
                BaselineEntry.ForEdge("T:A", "T:B")
                    .WithSiteCount(2)
                    .WithBecause("INC-1234"),
                BaselineEntry.ForSubject("T:C")
            ])));

        input.ShouldBe(
            "loadbearing-baseline-digest-v1\n" +
            "rule r/x\n" +
            "edge T:A -> T:B\n" +
            "because INC-1234\n" +
            "subject T:C\n");
    }

    [Fact]
    public void ComputeSeal_KnownEntry_IsTheIndependentSha256TruncatedToSixteen()
    {
        BaselineEntry entry = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithSiteCount(2);

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(BaselineFormat.SealInput("a/one", entry)));
        string independent = string.Concat(hash.Select(b => b.ToString("x2")));

        string seal = BaselineFormat.ComputeSeal("a/one", entry);
        // Length pinned literally and the value against the untruncated hash: together those say "the first
        // sixteen characters of the SHA-256" without restating the truncation the product does.
        seal.Length.ShouldBe(16);
        independent.ShouldStartWith(seal);
    }

    [Fact]
    public void ComputeSeal_TheSameEntryUnderTwoRules_Differs()
    {
        // The rule ID is the one whole-file property the seal keeps: an entry lifted out of one section and
        // dropped into another fails there rather than riding in as blessed. The composer binds it the same
        // way, which is why the two identical lines below come out with different seals.
        BaselineEntry entry = BaselineEntry.ForEdge("T:A", "T:B");

        BaselineFormat.ComputeSeal("r/x", entry)
            .ShouldNotBe(BaselineFormat.ComputeSeal("r/y", entry));

        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules(
            ("r/x", [BaselineEntry.ForEdge("T:A", "T:B")]),
            ("r/y", [BaselineEntry.ForEdge("T:A", "T:B")])));

        composed.ShouldContain($"\"seal\": \"{BaselineFormat.ComputeSeal("r/x", entry)}\"");
        composed.ShouldContain($"\"seal\": \"{BaselineFormat.ComputeSeal("r/y", entry)}\"");
    }

    [Fact]
    public void ComposeFile_AttributedEdgeAndSubject_RenderBecauseAfterTheIdentityAndSealLast()
    {
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules(("r/x",
        [
            BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
                .WithBecause("INC-1234"),
            BaselineEntry.ForSubject("T:N.Sub")
                .WithBecause("keep until migration")
        ])));

        composed.ShouldContain(
            "        { \"source\": \"T:N.Src\", \"target\": \"T:N.Tgt\", \"because\": \"INC-1234\", \"seal\": ");
        composed.ShouldContain(
            "        { \"subject\": \"T:N.Sub\", \"because\": \"keep until migration\", \"seal\": ");
    }

    [Fact]
    public void ComposeFile_CountedEdgeEntry_RendersSiteCountAfterTargetAndBeforeBecause()
    {
        // Every shape on one line, in the one order the format allows: the measure closes the identity
        // slots, the attribution closes the prose, the seal closes the entry. An uncounted entry omits the
        // key rather than writing a null, so a burndown diff of a partially upgraded file still moves one
        // line per entry.
        string composed = BaselineFormat.ComposeFile(BaselineComposer.Rules(("r/x",
        [
            BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
                .WithSiteCount(2),
            BaselineEntry.ForEdge("T:N.Src2", "T:N.Tgt")
                .WithSiteCount(3)
                .WithBecause("INC-1234"),
            BaselineEntry.ForEdge("T:N.Src3", "T:N.Tgt")
        ])));

        composed.ShouldContain("        { \"source\": \"T:N.Src\", \"target\": \"T:N.Tgt\", \"siteCount\": 2, \"seal\": ");
        composed.ShouldContain(
            "        { \"source\": \"T:N.Src2\", \"target\": \"T:N.Tgt\", \"siteCount\": 3, \"because\": \"INC-1234\", \"seal\": ");
        composed.ShouldContain("        { \"source\": \"T:N.Src3\", \"target\": \"T:N.Tgt\", \"seal\": ");
    }

    [Fact]
    public void ComputeSeal_AttributedVsUnattributed_Differ()
    {
        string plain = BaselineFormat.ComputeSeal("r/x", BaselineEntry.ForEdge("T:A", "T:B"));
        string attributed = BaselineFormat.ComputeSeal(
            "r/x",
            BaselineEntry.ForEdge("T:A", "T:B")
                .WithBecause("INC-1234"));
        string otherText = BaselineFormat.ComputeSeal(
            "r/x",
            BaselineEntry.ForEdge("T:A", "T:B")
                .WithBecause("INC-9999"));

        attributed.ShouldNotBe(plain);
        attributed.ShouldNotBe(otherText);
    }

    [Fact]
    public void ComputeSeal_CountedVsUncounted_Differ()
    {
        // The measure is folded into the seal, which is what makes a hand-raised count tamper rather than a
        // silently widened allowance — the one property that stops the ratchet being edited open.
        string uncounted = BaselineFormat.ComputeSeal("r/x", BaselineEntry.ForEdge("T:A", "T:B"));
        string two = BaselineFormat.ComputeSeal(
            "r/x",
            BaselineEntry.ForEdge("T:A", "T:B")
                .WithSiteCount(2));
        string three = BaselineFormat.ComputeSeal(
            "r/x",
            BaselineEntry.ForEdge("T:A", "T:B")
                .WithSiteCount(3));

        two.ShouldNotBe(uncounted);
        two.ShouldNotBe(three);
    }

    [Fact]
    public void ComposeFile_FixtureBaselines_ReproduceCheckedInFiles()
    {
        // The checked-in fixture baselines are authored FROM the composer, never by hand — this keeps
        // them honest. Compared after CRLF normalization (core.autocrlf may check them out as CRLF).
        // Each controller declares the DataTable twice, as a return type and as a construction, on two
        // lines — so every edge entry here grandfathers two sites, and the count is part of its seal.
        string violated = BaselineFormat.ComposeFile(BaselineComposer.Rules((
            "data-access/no-inline-sql",
            [
                BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
                    .WithSiteCount(2)
            ])));
        ReadFixture("arch", "baselines", "data-access", "no-inline-sql.json")
            .NormalizedLines()
            .ShouldBe(violated);

        string clean = BaselineFormat.ComposeFile(BaselineComposer.Rules((
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

        // The third v2 fixture, read by the Quarantine containment rows rather than composed by them.
        string quarantined = BaselineFormat.ComposeFile(BaselineComposer.Rules((
            "legacy/billing/containment",
            [
                BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.BillingCalculator")
                    .WithSiteCount(2),
                BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.RoundingMode")
                    .WithSiteCount(1)
            ])));
        ReadFixture("arch", "baselines", "legacy", "billing", "containment.json")
            .NormalizedLines()
            .ShouldBe(quarantined);
    }

    private static string ReadFixture(params string[] relativeParts)
    {
        var segments = new List<string> { AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp" };
        segments.AddRange(relativeParts);
        return File.ReadAllText(Path.Combine(segments.ToArray()));
    }
}
