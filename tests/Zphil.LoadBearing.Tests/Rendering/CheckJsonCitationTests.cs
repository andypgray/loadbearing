using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;
// The suite's reader over a check document, not the CLI renderer of the same name that this file also imports.
using CheckJson = Zphil.LoadBearing.Tests.TestSupport.CheckJson;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     A rule's citation on the <c>check --json</c> document (and so on <c>arch_check</c>'s): present at
///     full grain, omitted rather than written null when the rule cites nothing, and elided with the rest
///     of the authored prose at index grain. No fixture spec cites anything, so the beds the goldens and
///     the MCP budget pins ride on cannot answer this — the report here is built from source in-process.
/// </summary>
public sealed class CheckJsonCitationTests
{
    private const string Page = "https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures";

    // One extraction, two reports: the arms differ only in whether the rule cites, so the compile is the
    // class's rather than each field's.
    private static readonly CodebaseModel Codebase = CompilationFactory.Extract(Sources.OneController);

    private static readonly CheckReport Cited = Checker.Run(Codebase, arch =>
        arch.Rule("layer/no-data")
            .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("The web layer must not open the data layer directly.")
            .Citation(Page));

    private static readonly CheckReport Uncited = Checker.Run(Codebase, arch =>
        arch.Rule("layer/no-data")
            .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("The web layer must not open the data layer directly."));

    [Fact]
    public void Rule_WithCitation_CarriesItAtFullGrain()
    {
        Rule(Cited, DocumentGrain.Full)
            .GetProperty("citation")
            .GetString()
            .ShouldBe(Page);
    }

    [Fact]
    public void Rule_WithoutCitation_OmitsTheKey()
    {
        // Omitted rather than null, like every other optional field on the document: a consumer reading the
        // report of a spec that cites nothing sees exactly the keys it saw before the trailer existed.
        Rule(Uncited, DocumentGrain.Full)
            .TryGetProperty("citation", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Rule_AtIndexGrain_ElidesTheCitationWithTheRestOfTheProse()
    {
        // Index grain competes with a cut document, not with skeleton: what survives is the id and the
        // verdict, which is the menu the next call needs. The citation is authored prose like the reason and
        // the fix, so it goes with them — and `arch_explain` returns the rule whole, citation included.
        JsonElement rule = Rule(Cited, DocumentGrain.Index);

        rule.TryGetProperty("citation", out _)
            .ShouldBeFalse();
        rule.TryGetProperty("because", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Rule_AtSkeletonGrain_KeepsTheCitation()
    {
        // Skeleton keeps the prose: it scales with the rule count, which is authored and small, while what
        // overruns a channel is the violations. A citation dropped here would leave a verdict a reader can
        // act on with its provenance cut away.
        Rule(Cited, DocumentGrain.Skeleton)
            .GetProperty("citation")
            .GetString()
            .ShouldBe(Page);
    }

    private static JsonElement Rule(CheckReport report, DocumentGrain grain)
    {
        return CheckJson.Rule(report.JsonReport(grain), "layer/no-data");
    }
}
