using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The CLI baseline I/O boundary (<see cref="BaselineStore" />), exercised over scratch temp files
///     (no workspace): a missing file or missing section is uncaptured; a valid file parses (an
///     attributed entry round-trips its <c>because</c>, a counted one its <c>siteCount</c>); a legacy
///     file parses uncounted; a digest mismatch, malformed JSON, an unsupported
///     schemaVersion, a malformed entry (including an empty, blank, multi-line, or non-string
///     <c>because</c>, a <c>siteCount</c> that is not a whole number of at least 1, one on a subject
///     entry, one in a legacy file, or an unknown property beside any of them), or an unknown property
///     are all loud
///     <see cref="UserErrorException" />s naming the path — and a hand-edited <c>because</c> or
///     <c>siteCount</c> is tamper;
///     a CRLF checkout still verifies (the digest is over recanonicalized entries); unknown rule
///     sections are preserved; and path resolution honours the solution directory while an absolute
///     path wins.
/// </summary>
/// <remarks>
///     The legacy read path has one live precondition beyond the unit rows here. The fixture baseline
///     <c>Fixtures/TestSolutions/MyApp/arch/baselines/layering/services-behind-contracts.json</c> is held
///     at the legacy schema version deliberately: it is a single subject entry, which can never need a
///     site count, and it is read by every violated-spec end-to-end row — so a dozen tests fail if the
///     legacy read regresses, rather than one unit row here. It is therefore the one fixture baseline a
///     regeneration must leave alone, which
///     <see cref="LegacyFixtureBaseline_IsHeldAtTheLegacySchemaVersion" /> is here to catch.
/// </remarks>
public sealed class BaselineStoreTests : IDisposable
{
    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-store");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void TryReadDocument_MissingFile_ReturnsNull()
    {
        BaselineStore.TryReadDocument(Path.Combine(_temp.Path, "nope.json"))
            .ShouldBeNull();
    }

    [Fact]
    public void TryReadDocument_ValidFile_ParsesEntries()
    {
        string path = WriteComposed("b.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));

        BaselineDocument? document = BaselineStore.TryReadDocument(path);

        document.ShouldNotBeNull();
        document.Sections["data/x"]
            .ShouldBe([BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]);
    }

    [Fact]
    public void TryReadDocument_AttributedEntries_RoundTripBecause()
    {
        string path = WriteComposed(
            "attributed.json",
            ("data/x", [
                BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
                    .WithBecause("INC-1234")
            ]),
            ("legacy/billing/containment", [
                BaselineEntry.ForSubject("T:App.Legacy.Thing")
                    .WithBecause("grandfathered pending rewrite")
            ]));

        BaselineDocument? document = BaselineStore.TryReadDocument(path);

        document.ShouldNotBeNull();
        // Equality ignores attribution, so pin identity and .Because separately.
        IReadOnlyList<BaselineEntry> edges = document.Sections["data/x"];
        edges.ShouldBe([BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]);
        edges[0]
            .Because.ShouldBe("INC-1234");
        IReadOnlyList<BaselineEntry> subjects = document.Sections["legacy/billing/containment"];
        subjects.ShouldBe([BaselineEntry.ForSubject("T:App.Legacy.Thing")]);
        subjects[0]
            .Because.ShouldBe("grandfathered pending rewrite");
    }

    [Fact]
    public void TryReadDocument_DigestMismatch_ThrowsWithRestoreHint()
    {
        string path = WriteComposed("b.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));
        // Hand-edit an entry without updating the digest — the tamper the ratchet must refuse.
        File.WriteAllText(path, File.ReadAllText(path)
            .Replace("T:App.Web.Old", "T:App.Web.Hacked"));

        var ex = Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path));
        ex.Message.ShouldContain("failed its integrity check");
        ex.Message.ShouldContain("restore it from version control");
        ex.Message.ShouldContain(path);
        ex.Message.ShouldContain("loadbearing baseline --add");
        ex.Message.ShouldContain("loadbearing baseline --accept-reductions");
        ex.Message.ShouldNotContain("--init");
        ex.Message.ShouldNotContain("delete");
    }

    [Fact]
    public void TryReadDocument_HandEditedBecause_IsTamper()
    {
        string path = WriteComposed(
            "attributed.json",
            ("data/x", [
                BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
                    .WithBecause("INC-1234")
            ]));
        // The attribution is folded into the digest — rewording it by hand is tamper too.
        File.WriteAllText(path, File.ReadAllText(path)
            .Replace("INC-1234", "INC-9999"));

        var ex = Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path));
        ex.Message.ShouldContain("failed its integrity check");
    }

    [Fact]
    public void TryReadDocument_MalformedJson_ThrowsNamingPath()
    {
        string path = Write("bad.json", "{ this is not json");

        var ex = Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path));
        ex.Message.ShouldContain("is not valid");
        ex.Message.ShouldContain(path);
    }

    [Fact]
    public void TryReadDocument_WrongSchemaVersion_Throws()
    {
        // 3 rather than 2: the reader now accepts both shipped versions, so the refusal has to be pinned
        // against one that does not exist yet — and the message names the range, because "unsupported"
        // without it leaves a reader of an older file unable to tell whether their tool is behind.
        string path = Write("v3.json", """
                                       {
                                         "schemaVersion": 3,
                                         "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                         "rules": {}
                                       }
                                       """);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("unsupported schemaVersion 3 (expected 1 or 2).");
    }

    [Fact]
    public void TryReadDocument_LegacyFile_ParsesEntriesUncounted()
    {
        // The transparent legacy read: same entries, no measure, and the digest verified in the grammar
        // of the version the file declares rather than the one this build composes.
        string path = WriteLegacyComposed(
            "v1.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));

        BaselineDocument document = BaselineStore.TryReadDocument(path)
            .ShouldNotBeNull();

        IReadOnlyList<BaselineEntry> entries = document.Sections["data/x"];
        entries.ShouldBe([BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]);
        entries[0]
            .SiteCount.ShouldBeNull();
    }

    [Fact]
    public void TryReadDocument_CountedEntry_RoundTripsSiteCountBesideBecause()
    {
        string path = WriteComposed(
            "counted.json",
            ("data/x", [
                BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
                    .WithSiteCount(2)
                    .WithBecause("INC-1234")
            ]));

        BaselineDocument document = BaselineStore.TryReadDocument(path)
            .ShouldNotBeNull();

        // Equality ignores both riders, so pin identity, the measure and the attribution separately.
        IReadOnlyList<BaselineEntry> entries = document.Sections["data/x"];
        entries.ShouldBe([BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]);
        entries[0]
            .SiteCount.ShouldBe(2);
        entries[0]
            .Because.ShouldBe("INC-1234");
    }

    [Fact]
    public void TryReadDocument_CountedAndUncountedEntriesTogether_BothParse()
    {
        // A partially upgraded file — one section counted by a recent write, another still as it was
        // captured — is valid, which is what keeps a shared baseline file usable while it is being moved
        // over. An uncounted entry omits the key rather than carrying a null.
        string path = WriteComposed(
            "mixed.json",
            ("data/x", [
                BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
                    .WithSiteCount(2),
                BaselineEntry.ForEdge("T:App.Web.Older", "T:App.Data.Db")
            ]));

        BaselineDocument document = BaselineStore.TryReadDocument(path)
            .ShouldNotBeNull();

        document.Sections["data/x"]
            .Select(entry => entry.SiteCount)
            .ShouldBe([2, null]);
    }

    [Fact]
    public void TryReadDocument_SiteCountOnSubjectEntry_Throws()
    {
        string path = WriteEntryDoc("subject-count.json", """{ "subject": "T:A", "siteCount": 2 }""");

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("has a 'siteCount' on a subject entry — only edge entries carry one.");
    }

    [Fact]
    public void TryReadDocument_SiteCountInLegacyFile_IsMalformed()
    {
        // Not an optional key a legacy reader ignores: v1 has no measure, so its digest was computed
        // without one, and a v1 file carrying a count was hand-edited. It fails as the stranger it is —
        // the same message a typo'd property gets, for the same reason.
        string path = WriteEntryDoc(
            "legacy-count.json", """{ "source": "T:A", "target": "T:B", "siteCount": 2 }""",
            BaselineFormat.LegacySchemaVersion);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("neither {source, target} nor {subject}");
    }

    [Fact]
    public void TryReadDocument_SiteCountThatIsNotAWholeNumberAtLeastOne_Throws()
    {
        string zeroPath = WriteEntryDoc("zero.json", """{ "source": "T:A", "target": "T:B", "siteCount": 0 }""");
        string stringPath = WriteEntryDoc("string.json", """{ "source": "T:A", "target": "T:B", "siteCount": "2" }""");
        string fractionPath = WriteEntryDoc("fraction.json", """{ "source": "T:A", "target": "T:B", "siteCount": 2.5 }""");

        const string expected = "has a 'siteCount' that is not an integer of at least 1.";
        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(zeroPath))
            .Message.ShouldContain(expected);
        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(stringPath))
            .Message.ShouldContain(expected);
        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(fractionPath))
            .Message.ShouldContain(expected);
    }

    [Fact]
    public void TryReadDocument_HandEditedSiteCount_IsTamper()
    {
        string path = WriteComposed(
            "counted.json",
            ("data/x", [
                BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
                    .WithSiteCount(2)
            ]));
        // The measure is folded into the digest, so raising the allowance by hand is tamper rather than a
        // quietly widened ratchet — the property that makes the count worth trusting at all.
        File.WriteAllText(path, File.ReadAllText(path)
            .Replace("\"siteCount\": 2", "\"siteCount\": 9"));

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("failed its integrity check");
    }

    [Fact]
    public void LegacyFixtureBaseline_IsHeldAtTheLegacySchemaVersion()
    {
        // The precondition this class's remark states: a dozen violated-spec end-to-end rows read this
        // file, so it is the suite's live proof that the legacy read path still works. A regeneration that
        // swept it up with the rest would upgrade it silently and take that proof away without a red.
        string path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp", "arch", "baselines", "layering",
            "services-behind-contracts.json");

        File.ReadAllText(path)
            .ShouldContain($"\"schemaVersion\": {BaselineFormat.LegacySchemaVersion},");
        BaselineStore.TryReadDocument(path)
            .ShouldNotBeNull()
            .Sections["layering/services-behind-contracts"]
            .ShouldBe([BaselineEntry.ForSubject("T:MyApp.Domain.OrderService")]);
    }

    [Fact]
    public void TryReadDocument_EntryWithSourceAndSubject_Throws()
    {
        string path = WriteEntryDoc("mixed.json", """{ "source": "T:A", "subject": "T:B" }""");

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("neither {source, target} nor {subject}");
    }

    [Fact]
    public void TryReadDocument_BlankBecause_Throws()
    {
        string path = WriteEntryDoc("blank.json", """{ "subject": "T:A", "because": "   " }""");

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("blank or multi-line 'because'");
    }

    [Fact]
    public void TryReadDocument_MultilineBecause_Throws()
    {
        string path = WriteEntryDoc("multiline.json", """{ "subject": "T:A", "because": "a\nb" }""");

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("blank or multi-line 'because'");
    }

    [Fact]
    public void TryReadDocument_NonStringBecause_Throws()
    {
        string numberPath = WriteEntryDoc("number.json", """{ "subject": "T:A", "because": 3 }""");
        string emptyPath = WriteEntryDoc("empty.json", """{ "subject": "T:A", "because": "" }""");

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(numberPath))
            .Message.ShouldContain("empty or non-string 'because'");
        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(emptyPath))
            .Message.ShouldContain("empty or non-string 'because'");
    }

    [Fact]
    public void TryReadDocument_UnknownPropertyAlongsideBecause_Throws()
    {
        string path = WriteEntryDoc("typo.json", """{ "subject": "T:A", "becuase": "x" }""");

        // The same message the source-and-subject entry gets, for a different reason: an entry is classified
        // by the ID slots it carries, and a typo'd `becuase` is simply an unknown property beside a subject
        // — so the entry never resolves to either shape. Keeping this a named fact of its own is what
        // records that; a shared table row would leave only the message.
        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("neither {source, target} nor {subject}");
    }

    [Fact]
    public void TryReadDocument_UnknownRootProperty_Throws()
    {
        string path = Write("extra.json", """
                                          {
                                            "schemaVersion": 1,
                                            "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                            "rules": {},
                                            "surprise": true
                                          }
                                          """);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("unknown property 'surprise'");
    }

    [Fact]
    public void TryReadDocument_CrlfFile_VerifiesAfterRecanonicalization()
    {
        string path = WriteComposed("b.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));
        // Simulate an autocrlf checkout: rewrite with CRLF endings. The digest is over entries, not bytes.
        File.WriteAllText(path, File.ReadAllText(path)
            .Replace("\n", "\r\n"));

        BaselineStore.TryReadDocument(path)
            .ShouldNotBeNull();
    }

    [Fact]
    public void TryReadDocument_UnknownRuleSection_IsPreserved()
    {
        // A shared file may carry a Quarantine section for a rule not in this model — kept, not rejected.
        string path = WriteComposed(
            "shared.json",
            ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]),
            ("legacy/billing/containment", [BaselineEntry.ForSubject("T:App.Legacy.Thing")]));

        BaselineDocument document = BaselineStore.TryReadDocument(path)
            .ShouldNotBeNull();

        document.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(["data/x", "legacy/billing/containment"]);
    }

    [Fact]
    public void ResolvePath_RelativePath_ResolvesAgainstSolutionDir()
    {
        BaselineStore.ResolvePath("arch/baselines/data-access/no-inline-sql.json", _temp.Path)
            .ShouldBe(Path.GetFullPath(Path.Combine(_temp.Path, "arch/baselines/data-access/no-inline-sql.json")));
    }

    [Fact]
    public void ResolvePath_AbsolutePath_ReturnsItVerbatim()
    {
        string absolute = Path.Combine(_temp.Path, "elsewhere", "b.json");

        BaselineStore.ResolvePath(absolute, Path.Combine(_temp.Path, "unrelated"))
            .ShouldBe(Path.GetFullPath(absolute));
    }

    [Fact]
    public void LoadForModel_MissingFile_LeavesRuleUncaptured()
    {
        BaselineIndex index = BaselineStore.LoadForModel(MigrateModel("data/x", "arch/absent.json"), _temp.Path);

        index.TryGet("data/x", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void LoadForModel_FileWithoutSection_LeavesRuleUncaptured()
    {
        WriteComposed("arch/b.json", ("other/rule", [BaselineEntry.ForSubject("T:App.Thing")]));

        BaselineIndex index = BaselineStore.LoadForModel(MigrateModel("data/x", "arch/b.json"), _temp.Path);

        index.TryGet("data/x", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void LoadForModel_ValidFile_CapturesSection()
    {
        WriteComposed("arch/b.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));

        BaselineIndex index = BaselineStore.LoadForModel(MigrateModel("data/x", "arch/b.json"), _temp.Path);

        index.TryGet("data/x", out RuleBaseline? section)
            .ShouldBeTrue();
        section.ShouldNotBeNull()
            .Entries.ShouldHaveSingleItem();
    }

    [Fact]
    public void Write_MatchingCrlfFile_ReportsUnchanged()
    {
        var document = new BaselineDocument(new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal)
        {
            ["data/x"] = [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]
        });
        string path = Path.Combine(_temp.Path, "w.json");
        BaselineStore.Write(path, document)
            .ShouldBe(WriteOutcome.Wrote);
        File.WriteAllText(path, File.ReadAllText(path)
            .Replace("\n", "\r\n")); // autocrlf checkout

        BaselineStore.Write(path, document)
            .ShouldBe(WriteOutcome.Unchanged);
    }

    private static ArchitectureModel MigrateModel(string ruleId, string baselinePath)
    {
        return Checker.Model(arch =>
            arch.Rule(ruleId)
                .Migrate(
                    "old",
                    arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
                .Baseline(baselinePath)
                .Because("b"));
    }

    private string WriteComposed(string relativePath, params (string RuleId, BaselineEntry[] Entries)[] rules)
    {
        return Write(relativePath, BaselineComposer.Compose(rules));
    }

    /// <summary>
    ///     The same file as a <em>legacy</em> baseline — see
    ///     <see cref="BaselineComposer.ComposeLegacy(string, BaselineEntry[])" />.
    /// </summary>
    private string WriteLegacyComposed(string relativePath, params (string RuleId, BaselineEntry[] Entries)[] rules)
    {
        return Write(relativePath, BaselineComposer.ComposeLegacy(rules));
    }

    /// <summary>
    ///     A document carrying <paramref name="entryJson" /> as the sole <c>data/x</c> entry, behind the
    ///     schemaVersion/digest/rules envelope every malformed-entry refusal has to spell. The all-zero
    ///     digest is never reached: an entry this malformed is refused while the file is being parsed, which
    ///     is what the messages pinned below say. <paramref name="schemaVersion" /> is the current one
    ///     unless a row's whole subject is what a legacy file makes of a key.
    /// </summary>
    private string WriteEntryDoc(
        string relativePath, string entryJson, int schemaVersion = BaselineFormat.SchemaVersion)
    {
        return Write(relativePath, $$"""
                                     {
                                       "schemaVersion": {{schemaVersion}},
                                       "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                       "rules": {
                                         "data/x": {
                                           "entries": [
                                             {{entryJson}}
                                           ]
                                         }
                                       }
                                     }
                                     """);
    }

    private string Write(string relativePath, string content)
    {
        return _temp.WriteFile([relativePath], content);
    }
}
