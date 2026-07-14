using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The CLI baseline I/O boundary (<see cref="BaselineStore" />), exercised over scratch temp files
///     (no workspace): a missing file or missing section is uncaptured; a valid file parses (an
///     attributed entry round-trips its <c>because</c>); a digest mismatch, malformed JSON, a bad
///     schemaVersion, a malformed entry (including an empty, blank, multi-line, or non-string
///     <c>because</c>, or an unknown property beside one), or an unknown property are all loud
///     <see cref="UserErrorException" />s naming the path — and a hand-edited <c>because</c> is tamper;
///     a CRLF checkout still verifies (the digest is over recanonicalized entries); unknown rule
///     sections are preserved; and path resolution honours the solution directory while an absolute
///     path wins.
/// </summary>
public sealed class BaselineStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "loadbearing-baseline-tests", Guid.NewGuid().ToString("N"));

    public BaselineStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void TryReadDocument_MissingFile_ReturnsNull()
    {
        BaselineStore.TryReadDocument(Path.Combine(_dir, "nope.json")).ShouldBeNull();
    }

    [Fact]
    public void TryReadDocument_ValidFile_ParsesEntries()
    {
        string path = WriteComposed("b.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));

        BaselineDocument? document = BaselineStore.TryReadDocument(path);

        document.ShouldNotBeNull();
        document!.Sections["data/x"].ShouldBe([BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]);
    }

    [Fact]
    public void TryReadDocument_AttributedEntries_RoundTripBecause()
    {
        string path = WriteComposed(
            "attributed.json",
            ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db").WithBecause("INC-1234")]),
            ("legacy/billing/containment", [BaselineEntry.ForSubject("T:App.Legacy.Thing").WithBecause("grandfathered pending rewrite")]));

        BaselineDocument? document = BaselineStore.TryReadDocument(path);

        document.ShouldNotBeNull();
        // Equality ignores attribution, so pin identity and .Because separately.
        var edges = document!.Sections["data/x"];
        edges.ShouldBe([BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]);
        edges[0].Because.ShouldBe("INC-1234");
        var subjects = document.Sections["legacy/billing/containment"];
        subjects.ShouldBe([BaselineEntry.ForSubject("T:App.Legacy.Thing")]);
        subjects[0].Because.ShouldBe("grandfathered pending rewrite");
    }

    [Fact]
    public void TryReadDocument_DigestMismatch_ThrowsWithRestoreHint()
    {
        string path = WriteComposed("b.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));
        // Hand-edit an entry without updating the digest — the tamper the ratchet must refuse.
        File.WriteAllText(path, File.ReadAllText(path).Replace("T:App.Web.Old", "T:App.Web.Hacked"));

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
            ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db").WithBecause("INC-1234")]));
        // The attribution is folded into the digest — rewording it by hand is tamper too.
        File.WriteAllText(path, File.ReadAllText(path).Replace("INC-1234", "INC-9999"));

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
        string path = Write("v2.json", """
                                       {
                                         "schemaVersion": 2,
                                         "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                         "rules": {}
                                       }
                                       """);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("unsupported schemaVersion 2");
    }

    [Fact]
    public void TryReadDocument_EntryWithSourceAndSubject_Throws()
    {
        string path = Write("mixed.json", """
                                          {
                                            "schemaVersion": 1,
                                            "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                            "rules": {
                                              "data/x": {
                                                "entries": [
                                                  { "source": "T:A", "subject": "T:B" }
                                                ]
                                              }
                                            }
                                          }
                                          """);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("neither {source, target} nor {subject}");
    }

    [Fact]
    public void TryReadDocument_BlankBecause_Throws()
    {
        string path = Write("blank.json", """
                                          {
                                            "schemaVersion": 1,
                                            "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                            "rules": {
                                              "data/x": {
                                                "entries": [
                                                  { "subject": "T:A", "because": "   " }
                                                ]
                                              }
                                            }
                                          }
                                          """);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("blank or multi-line 'because'");
    }

    [Fact]
    public void TryReadDocument_MultilineBecause_Throws()
    {
        string path = Write("multiline.json", """
                                              {
                                                "schemaVersion": 1,
                                                "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                                "rules": {
                                                  "data/x": {
                                                    "entries": [
                                                      { "subject": "T:A", "because": "a\nb" }
                                                    ]
                                                  }
                                                }
                                              }
                                              """);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(path))
            .Message.ShouldContain("blank or multi-line 'because'");
    }

    [Fact]
    public void TryReadDocument_NonStringBecause_Throws()
    {
        string numberPath = Write("number.json", """
                                                 {
                                                   "schemaVersion": 1,
                                                   "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                                   "rules": {
                                                     "data/x": {
                                                       "entries": [
                                                         { "subject": "T:A", "because": 3 }
                                                       ]
                                                     }
                                                   }
                                                 }
                                                 """);
        string emptyPath = Write("empty.json", """
                                               {
                                                 "schemaVersion": 1,
                                                 "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                                 "rules": {
                                                   "data/x": {
                                                     "entries": [
                                                       { "subject": "T:A", "because": "" }
                                                     ]
                                                   }
                                                 }
                                               }
                                               """);

        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(numberPath))
            .Message.ShouldContain("empty or non-string 'because'");
        Should.Throw<UserErrorException>(() => BaselineStore.TryReadDocument(emptyPath))
            .Message.ShouldContain("empty or non-string 'because'");
    }

    [Fact]
    public void TryReadDocument_UnknownPropertyAlongsideBecause_Throws()
    {
        string path = Write("typo.json", """
                                         {
                                           "schemaVersion": 1,
                                           "digest": "0000000000000000000000000000000000000000000000000000000000000000",
                                           "rules": {
                                             "data/x": {
                                               "entries": [
                                                 { "subject": "T:A", "becuase": "x" }
                                               ]
                                             }
                                           }
                                         }
                                         """);

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
        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n"));

        BaselineStore.TryReadDocument(path).ShouldNotBeNull();
    }

    [Fact]
    public void TryReadDocument_UnknownRuleSection_IsPreserved()
    {
        // A shared file may carry a Freeze section for a rule not in this model — kept, not rejected.
        string path = WriteComposed(
            "shared.json",
            ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]),
            ("legacy/billing/containment", [BaselineEntry.ForSubject("T:App.Legacy.Thing")]));

        BaselineDocument? document = BaselineStore.TryReadDocument(path);

        document!.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(["data/x", "legacy/billing/containment"]);
    }

    [Fact]
    public void ResolvePath_RelativePath_ResolvesAgainstSolutionDir()
    {
        BaselineStore.ResolvePath("arch/baselines/data-access/no-inline-sql.json", _dir)
            .ShouldBe(Path.GetFullPath(Path.Combine(_dir, "arch/baselines/data-access/no-inline-sql.json")));
    }

    [Fact]
    public void ResolvePath_AbsolutePath_ReturnsItVerbatim()
    {
        string absolute = Path.Combine(_dir, "elsewhere", "b.json");

        BaselineStore.ResolvePath(absolute, Path.Combine(_dir, "unrelated"))
            .ShouldBe(Path.GetFullPath(absolute));
    }

    [Fact]
    public void LoadForModel_MissingFile_LeavesRuleUncaptured()
    {
        BaselineIndex index = BaselineStore.LoadForModel(MigrateModel("data/x", "arch/absent.json"), _dir);

        index.TryGet("data/x", out _).ShouldBeFalse();
    }

    [Fact]
    public void LoadForModel_FileWithoutSection_LeavesRuleUncaptured()
    {
        WriteComposed("arch/b.json", ("other/rule", [BaselineEntry.ForSubject("T:App.Thing")]));

        BaselineIndex index = BaselineStore.LoadForModel(MigrateModel("data/x", "arch/b.json"), _dir);

        index.TryGet("data/x", out _).ShouldBeFalse();
    }

    [Fact]
    public void LoadForModel_ValidFile_CapturesSection()
    {
        WriteComposed("arch/b.json", ("data/x", [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]));

        BaselineIndex index = BaselineStore.LoadForModel(MigrateModel("data/x", "arch/b.json"), _dir);

        index.TryGet("data/x", out RuleBaseline? section).ShouldBeTrue();
        section!.Count.ShouldBe(1);
    }

    [Fact]
    public void Write_MatchingCrlfFile_ReportsUnchanged()
    {
        var document = new BaselineDocument(new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal)
        {
            ["data/x"] = [BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]
        });
        string path = Path.Combine(_dir, "w.json");
        BaselineStore.Write(path, document).ShouldBe(WriteOutcome.Wrote);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n")); // autocrlf checkout

        BaselineStore.Write(path, document).ShouldBe(WriteOutcome.Unchanged);
    }

    private static ArchitectureModel MigrateModel(string ruleId, string baselinePath)
    {
        return ArchModelBuilder.Build(new OneMigrateSpec(ruleId, baselinePath));
    }

    private string WriteComposed(string relativePath, params (string RuleId, BaselineEntry[] Entries)[] rules)
    {
        var input = rules.ToDictionary(
            r => r.RuleId, r => (IReadOnlyCollection<BaselineEntry>)r.Entries, StringComparer.Ordinal);
        return Write(relativePath, BaselineFormat.ComposeFile(input));
    }

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class OneMigrateSpec(string ruleId, string baselinePath) : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule(ruleId)
                .Migrate(
                    "old",
                    arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
                .Baseline(baselinePath)
                .Because("b");
        }
    }
}