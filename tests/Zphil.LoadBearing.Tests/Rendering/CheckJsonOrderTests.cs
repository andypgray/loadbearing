using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The check document's key order (<see cref="JsonReportRenderer.Document" />, and so
///     <c>arch_check</c>'s too): the roll-up and every trust stamp serialize ahead of <c>rules</c> — the
///     bulk a truncating reader's cut lands in — and the stamps serialize ahead of the <c>summary</c> they
///     invalidate. The goldens cannot pin this: a stamp is omitted from every clean run, so five of the six
///     keys here appear in no golden at all. The one document under test carries all of them at once —
///     including the per-rule subject-coverage pair, which the same reasoning applies to one level down.
/// </summary>
public sealed class CheckJsonOrderTests
{
    // One controller opening the data layer directly, so the rule fails and the roll-up counts are real,
    // plus an attributed type inside the subject namespace so the rule's subject-coverage pair is populated
    // too — this document's job is to carry every optional slot at once, and those two are optional.
    private const string OneController = """
                                         namespace App.Web { public class OldController { public App.Data.Db Load() => new App.Data.Db(); } }
                                         namespace App.Web { [System.CodeDom.Compiler.GeneratedCode("Test", "1.0")] public class Emitted {} }
                                         namespace App.Data { public class Db {} }
                                         """;

    private static readonly CheckReport WebOpensData = Checker.Run(OneController, arch =>
        arch.Rule("layer/no-data")
            .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("The web layer must not open the data layer directly."));

    // A run whose workspace loaded incompletely, whose packages did not restore, that a solution filter
    // narrowed, whose solution declares a project in another language, and that a --rules glob narrowed
    // further: every optional slot populated at once, which no real run needs to be for the order to
    // matter and which no golden can be.
    private static readonly string StampedDocument = JsonReportRenderer.Document(
        report: WebOpensData,
        solutionDirectory: Directory.GetCurrentDirectory(),
        solutionName: "S.sln",
        specAssembly: "Spec.dll",
        diffBase: null,
        workspaceDiagnostics: ["App.Web/App.Web.csproj : error MSB4019: imported project was not found"],
        diagnostics: new WorkspaceDiagnostics(
            [], [], ["App.Web/App.Web.csproj"], ["App.Reports/App.Reports.csproj"],
            ["App.Data/App.Data.csproj"], ["App.Signals/App.Signals.fsproj"]),
        rulesFilter: ["layer/*"],
        grain: DocumentGrain.Full);

    [Theory]
    [InlineData("summary")]
    [InlineData("modelIncomplete")]
    [InlineData("failedProjects")]
    [InlineData("uncheckedProjects")]
    [InlineData("restoreFailedProjects")]
    [InlineData("unsupportedProjects")]
    public void Document_VerdictAndTrustStamps_SerializeAheadOfRules(string key)
    {
        // Below the grain ladder's coarsest rung a reader with a response budget cuts at the last newline
        // that fits, and that cut lands inside `rules` — so everything under it is what a red-heavy report
        // loses first, which is the report whose verdict matters most.
        ShouldSerializeBefore(StampedDocument, key, "rules");
    }

    [Theory]
    [InlineData("modelIncomplete")]
    [InlineData("failedProjects")]
    [InlineData("uncheckedProjects")]
    [InlineData("restoreFailedProjects")]
    [InlineData("unsupportedProjects")]
    public void Document_TrustStamps_SerializeAheadOfTheSummaryTheyInvalidate(string key)
    {
        ShouldSerializeBefore(StampedDocument, key, "summary");
    }

    [Theory]
    [InlineData("subjectTypes")]
    [InlineData("subjectGeneratedTypes")]
    public void Document_SubjectCoverage_SerializesAheadOfTheRulesViolations(string key)
    {
        // Inside a rule rather than at document level: the pair qualifies THIS rule's subject, so it sits
        // with the rule's own framing — id, verdict, prose, baseline — above the violation list that scales
        // with the codebase and is the first thing a truncating reader loses. Scoped to the rules block,
        // because `violations` also names a roll-up count in the summary above it.
        ShouldSerializeBefore(RulesBlock(StampedDocument), key, "violations");
    }

    [Fact]
    public void Document_SubjectCoverage_KeepsTheDenominatorAheadOfTheNumerator()
    {
        // "2 types, 1 generated" reads in that order for the same reason it is written in that order: the
        // count comes before the qualifier that narrows it.
        ShouldSerializeBefore(RulesBlock(StampedDocument), "subjectTypes", "subjectGeneratedTypes");
    }

    [Fact]
    public void Document_WorkspaceDiagnostics_StaysBelowTheRules()
    {
        // MSBuild's own words are evidence rather than verdict, they have no ceiling, and the actionable half
        // of them is already hoisted into failedProjects and restoreFailedProjects — so this is the one slot
        // deliberately left where a cut can reach it.
        ShouldSerializeBefore(StampedDocument, "rules", "workspaceDiagnostics");
    }

    // The document from `rules` onward. Several key names appear both in the roll-up summary and inside a
    // rule, and ShouldHaveKeyAt reads the first occurrence — so an ordering claim about two keys of one rule
    // has to be made where only the rules are.
    private static string RulesBlock(string document)
    {
        int at = ShouldHaveKeyAt(document, "rules");
        return document.Substring(at);
    }

    private static void ShouldSerializeBefore(string document, string earlier, string later)
    {
        int earlierAt = ShouldHaveKeyAt(document, earlier);
        int laterAt = ShouldHaveKeyAt(document, later);

        earlierAt.ShouldBeLessThan(laterAt, $"\"{earlier}\" must serialize before \"{later}\":\n{document}");
    }

    // A key's position, asserted present first. IndexOf answers -1 for a key that is not in the document and
    // -1 precedes every real position, so an ordering assertion over a stamp the renderer had dropped would
    // pass without ever having tested it.
    private static int ShouldHaveKeyAt(string document, string key)
    {
        int at = document.IndexOf($"\"{key}\":", StringComparison.Ordinal);

        at.ShouldBeGreaterThanOrEqualTo(0, $"the document has no \"{key}\" key:\n{document}");

        return at;
    }
}
