using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The SARIF renderer's per-site mapping (<see cref="SarifReportRenderer.Serialize" />, the workspace-
///     free seam), driven over in-memory reports: a red reference emits an <c>error</c> /
///     <c>baselineState: new</c> result with no suppressions; a violation touched at several sites in one
///     file gets consecutive per-file fingerprint ordinals; site-less EmptySubject/RuleError violations
///     contribute no results (metadata only); a Freeze tripwire's empty law sentence omits
///     <c>shortDescription</c>; and a grandfathered violation suppresses as a <c>note</c> whose
///     justification is the baseline entry's <c>because</c> when present, else the generic
///     <c>grandfathered in {path}</c> fallback — the latter exercising the checker's baseline-attribution
///     recovery. The full-report byte shape is pinned separately by the <c>violated-check.sarif</c> golden.
/// </summary>
public sealed class SarifReportRendererTests
{
    // One controller opening the data layer directly — a single forbidden edge (OldController -> App.Data.Db).
    private const string OneController = """
                                         namespace App.Web { public class OldController { public App.Data.Db Load() => new App.Data.Db(); } }
                                         namespace App.Data { public class Db {} }
                                         """;

    // The site path is a relative "Test.cs" from the MSBuild-free factory, so any solution directory works —
    // the tests never assert on the resolved URI, only on level, state, suppressions, and fingerprint ordinals.
    private static readonly string SolutionDir = Directory.GetCurrentDirectory();

    [Fact]
    public void Serialize_RedReference_EmitsErrorLevelNewBaselineStateNoSuppressions()
    {
        // An uncaptured Enforce reference violation: every site is a red error at baselineState new, and —
        // nothing grandfathers it — carries no suppressions property at all (null-omitted, not an empty array).
        CheckReport report = Checker.Run(OneController, arch =>
            arch.Rule("layer/no-data")
                .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("The web layer must not open the data layer directly."));

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        var results = Results(json);
        results.ShouldNotBeEmpty();
        foreach (JsonElement result in results)
        {
            result.GetProperty("level").GetString().ShouldBe("error");
            result.GetProperty("baselineState").GetString().ShouldBe("new");
            result.TryGetProperty("suppressions", out _).ShouldBeFalse();
        }
    }

    [Fact]
    public void Serialize_MultiSiteSameFile_AssignsDistinctFingerprintOrdinalsPerSite()
    {
        // One violation (Page -> Db) touched at several sites in one file: the per-file ordinal counter resets
        // per violation and increments per site, so the sites get consecutive 0-based ordinals and thus
        // distinct fingerprints — the (ruleId, fingerprint) alert key stays unique when code motion shifts lines.
        const string source = """
                              namespace App.Web
                              {
                                  public class Page
                                  {
                                      public App.Data.Db First() => new App.Data.Db();
                                      public App.Data.Db Second() => new App.Data.Db();
                                  }
                              }
                              namespace App.Data { public class Db {} }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("layer/no-data")
                .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("The web layer must not open the data layer directly."));

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        var results = Results(json);
        results.Count.ShouldBeGreaterThan(1); // genuinely multi-site
        // All sites belong to the one Page -> Db violation, so the fingerprints share every slot but the ordinal.
        IReadOnlyList<string> fingerprints = results.Select(Fingerprint).ToList();
        fingerprints.ShouldAllBe(f => f.StartsWith("v1|T:App.Web.Page|T:App.Data.Db|", StringComparison.Ordinal));
        var ordinals = fingerprints.Select(f => int.Parse(f.Split('|')[^1])).ToList();
        ordinals.ShouldBe(Enumerable.Range(0, ordinals.Count)); // consecutive, 0-based, distinct
    }

    [Fact]
    public void Serialize_CatchViolation_EmitsCatchesMessageText()
    {
        // A red MustNotCatch violation drives the SARIF MessageText catch arm (a missing arm renders empty text
        // and reads green): message `Source catches Target`, level error (GRAMMAR §4.8).
        const string source = """
                              namespace Errors { public class DbError : System.Exception {} }
                              namespace App { public class Handler { public void Run() { try { } catch (Errors.DbError) { } } } }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("ex/no-catch")
                .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                .Because("Catch specific exceptions, not the domain base."));

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        JsonElement result = Results(json).ShouldHaveSingleItem();
        result.GetProperty("level").GetString().ShouldBe("error");
        result.GetProperty("message").GetProperty("text").GetString().ShouldBe("App.Handler catches Errors.DbError");
    }

    [Fact]
    public void Serialize_ExposeViolation_EmitsExposesMessageText()
    {
        // A red MustNotExpose violation drives the SARIF MessageText expose arm (a missing arm renders empty text
        // and reads green): message `Source exposes Target`, level error (GRAMMAR §4.9).
        const string source = """
                              namespace Secrets { public class Data {} }
                              namespace App { public class Facade { public void Take(Secrets.Data d) {} } }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("api/no-expose")
                .Enforce(arch.Namespace("App.*").MustNotExpose(arch.Namespace("Secrets.*")))
                .Because("Keep internal types off the public API."));

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        JsonElement result = Results(json).ShouldHaveSingleItem();
        result.GetProperty("level").GetString().ShouldBe("error");
        result.GetProperty("message").GetProperty("text").GetString().ShouldBe("App.Facade exposes Secrets.Data");
    }

    [Fact]
    public void Serialize_ThrowViolation_EmitsThrowsMessageText()
    {
        // A red MustOnlyThrow violation drives the SARIF MessageText throw arm: an external, unlisted throw is
        // red (no external exemption), message `Source throws Target`, level error (GRAMMAR §4.8).
        const string source = """
                              namespace App { public class Service { public void Run() => throw new System.InvalidOperationException(); } }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("ex/only-throw")
                .Enforce(arch.Namespace("App.*").MustOnlyThrow(arch.Namespace("Sanctioned.*")))
                .Because("Throw only the sanctioned exception types."));

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        JsonElement result = Results(json).ShouldHaveSingleItem();
        result.GetProperty("level").GetString().ShouldBe("error");
        result.GetProperty("message").GetProperty("text").GetString()
            .ShouldBe("App.Service throws System.InvalidOperationException");
    }

    [Fact]
    public void Serialize_EmptySubjectAndRuleError_ProduceNoResults()
    {
        // EmptySubject and RuleError violations are site-less by construction (the model deliberately carries no
        // spec-source location for them), so they contribute zero results — they still gate via the exit code
        // and show in human/JSON output, but SARIF is a per-site artifact. The rule metadata is still emitted.
        var report = new CheckReport(
        [
            new RuleResult(
                Rule("naming/empty", Posture.Enforce), RuleStatus.Failed,
                [Violation.EmptySubject("The subject selection matched no solution-declared types.")],
                [], null, [], 0, false),
            new RuleResult(
                Rule("ref/error", Posture.Enforce), RuleStatus.Failed,
                [Violation.RuleError("a closed-generic backstop message")],
                [], null, [], 0, false)
        ]);

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        Results(json).ShouldBeEmpty();
        Rules(json).Select(r => r.GetProperty("id").GetString()).ShouldBe(["naming/empty", "ref/error"]);
    }

    [Fact]
    public void Serialize_TripwireEmptySentence_OmitsShortDescription()
    {
        // A Freeze tripwire carries no law sentence (empty string), so its reportingDescriptor omits
        // shortDescription entirely rather than emitting an empty one, while still carrying its Because as
        // fullDescription. This is the freeze-tripwire pin the golden's last rule also holds.
        var report = new CheckReport(
        [
            new RuleResult(
                new ArchRule(
                    "legacy/tripwire", Posture.Freeze, "Replacement scheduled; not worth stabilizing.", null, "",
                    null, null, null),
                RuleStatus.Skipped, [], [], "no diff context", [], 0, false)
        ]);

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        JsonElement rule = Rules(json).Single();
        rule.TryGetProperty("shortDescription", out _).ShouldBeFalse();
        rule.GetProperty("fullDescription").GetProperty("text").GetString()
            .ShouldBe("Replacement scheduled; not worth stabilizing.");
    }

    [Fact]
    public void Serialize_GrandfatheredWithoutAttribution_SuppressesWithGenericBaselinePathJustification()
    {
        // A grandfathered Migrate violation whose baseline entry has no `because`: the suppression falls back to
        // the generic `grandfathered in {conventional baseline path}` justification (note level, unchanged state).
        BaselineIndex index = Index("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));
        CheckReport report = Checker.Run(OneController, index, NoDataAccess);

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        var notes = Notes(json);
        notes.ShouldNotBeEmpty();
        foreach (JsonElement note in notes)
        {
            note.GetProperty("baselineState").GetString().ShouldBe("unchanged");
            JsonElement suppression = note.GetProperty("suppressions").EnumerateArray().Single();
            suppression.GetProperty("kind").GetString().ShouldBe("external");
            suppression.GetProperty("justification").GetString()
                .ShouldBe("grandfathered in arch/baselines/data/x.json");
        }
    }

    [Fact]
    public void Serialize_GrandfatheredWithBecause_SuppressesWithAttributionJustification()
    {
        // The step-1 Core path: RuleBaseline.TryMatch recovers the stored entry's attribution (identity equality
        // excludes Because), and RuleResult.GrandfatheredEntries carries it index-aligned into the report, so the
        // suppression justification is the operator's own `because`, not the generic fallback.
        const string because = "Legacy Active Record; scheduled for removal in Q3.";
        BaselineEntry entry = BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db").WithBecause(because);
        CheckReport report = Checker.Run(OneController, Index("data/x", entry), NoDataAccess);

        string json = SarifReportRenderer.Serialize(report, SolutionDir, true, []);

        var notes = Notes(json);
        notes.ShouldNotBeEmpty();
        foreach (JsonElement note in notes)
        {
            note.GetProperty("baselineState").GetString().ShouldBe("unchanged");
            JsonElement suppression = note.GetProperty("suppressions").EnumerateArray().Single();
            suppression.GetProperty("kind").GetString().ShouldBe("external");
            suppression.GetProperty("justification").GetString().ShouldBe(because);
        }
    }

    // The Migrate rule OneController is checked against: Web controllers must not reference the data layer. It
    // omits .Baseline, so its conventional path is arch/baselines/data/x.json (GRAMMAR §4.4).
    private static void NoDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "Controllers open the data layer directly (legacy Active Record style).",
                arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("Repository pattern for testability.");
    }

    private static BaselineIndex Index(string ruleId, params BaselineEntry[] entries)
    {
        return new BaselineIndex(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal)
        {
            [ruleId] = new(entries)
        });
    }

    // A metadata-only ArchRule (no constraint/migrate/freeze payload) for hand-built reports — the renderer's
    // rule catalog reads only Id, Sentence, Because, Fix, and Posture.
    private static ArchRule Rule(string id, Posture posture)
    {
        return new ArchRule(id, posture, "because", null, "sentence", null, null, null);
    }

    private static IReadOnlyList<JsonElement> Rules(string json)
    {
        return Run(json).GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().ToList();
    }

    private static IReadOnlyList<JsonElement> Results(string json)
    {
        return Run(json).GetProperty("results").EnumerateArray().ToList();
    }

    // The grandfathered results — every note-level result carries a suppression by construction.
    private static IReadOnlyList<JsonElement> Notes(string json)
    {
        return Results(json).Where(r => r.GetProperty("level").GetString() == "note").ToList();
    }

    private static string Fingerprint(JsonElement result)
    {
        return result.GetProperty("partialFingerprints").GetProperty("loadBearingViolationIdentity/v1").GetString()!;
    }

    private static JsonElement Run(string json)
    {
        return JsonDocument.Parse(json).RootElement.GetProperty("runs")[0];
    }
}