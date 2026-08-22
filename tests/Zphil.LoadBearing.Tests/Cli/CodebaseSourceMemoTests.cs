using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     What a second <see cref="CodebaseSource.ExtractAsync" /> on one source costs, and what it returns.
///     The source walks the workspace once for its lifetime and memoizes the merged models against the
///     fragments it walked, keyed by exclusion set — so a repeat with the same exclusion is free, and a
///     repeat with a <em>different</em> one is a merge rather than a second walk and still yields the model
///     a cold extraction with that exclusion would have.
/// </summary>
/// <remarks>
///     The keying is the whole point rather than an optimization detail: <c>render --diagram</c>'s two calls
///     have deliberately different exclusion sets, so a memo keyed on nothing would hand the survey fence
///     the cards' model and draw a diagram missing the spec project's neighbours. These pin both halves
///     against the same oracle the cold path uses, which is what makes "identical by construction" checkable
///     rather than merely argued. Serial with the rest of the workspace-loading suites.
/// </remarks>
[Collection("Serial")]
public sealed class CodebaseSourceMemoTests
{
    private const string Billing = "MyApp.Legacy.Billing";
    private const string Domain = "MyApp.Domain";

    [Fact]
    public async Task ExtractAsync_WithSeveralDifferentExclusions_WalksOnceAndEachModelMatchesItsColdExtraction()
    {
        using var fixture = new TempFixtureWorkspace();
        var counting = new CountingSolutionSource();
        using var source = await CodebaseSource.CreateSpeclessAsync(
            counting, EnvironmentFor(), fixture.SolutionPath,
            SolutionPaths.SolutionDirectoryOf(fixture.SolutionPath), true, Ct);

        CodebaseModel excludingBilling = await source.ExtractAsync([Billing], Ct);
        CodebaseModel excludingNothing = await source.ExtractAsync([], Ct);
        CodebaseModel excludingDomain = await source.ExtractAsync([Domain], Ct);

        // One walk for three models — the term the memo exists to collapse.
        source.ExtractionCount.ShouldBe(1);
        counting.AcquireCount.ShouldBe(1);

        // And each is the model a cold extraction with that exclusion produces. Dropping a project at merge
        // time is byte-identical to never extracting it (a referenced-but-dropped project survives as an
        // external node), which is what lets one walk serve every exclusion set a run asks for. One
        // workspace for all three oracles: what is under test is the extraction, and a second load of the
        // same untouched tree would only cost the suite a design-time build per oracle.
        using SolutionHandle oracle = await new ColdSolutionSource().AcquireAsync(fixture.SolutionPath, Ct);
        excludingBilling.ShouldModelTheSameAs(await ColdAsync(oracle, [Billing]));
        excludingNothing.ShouldModelTheSameAs(await ColdAsync(oracle, []));
        excludingDomain.ShouldModelTheSameAs(await ColdAsync(oracle, [Domain]));

        // Keyed, not global: three exclusion sets are three models, and no two of them are the same object.
        excludingBilling.ShouldNotBeSameAs(excludingNothing);
        excludingNothing.ShouldNotBeSameAs(excludingDomain);
    }

    [Fact]
    public async Task ExtractAsync_TwiceWithTheSameExclusion_HandsBackTheModelItAlreadyBuilt()
    {
        using var fixture = new TempFixtureWorkspace();
        var counting = new CountingSolutionSource();
        using var source = await CodebaseSource.CreateSpeclessAsync(
            counting, EnvironmentFor(), fixture.SolutionPath,
            SolutionPaths.SolutionDirectoryOf(fixture.SolutionPath), true, Ct);

        CodebaseModel first = await source.ExtractAsync([Billing], Ct);
        CodebaseModel second = await source.ExtractAsync([Billing], Ct);

        // The same instance, not merely an equal one: a merged model is read-only once built, so there is
        // nothing to gain by rebuilding it and every consumer reads.
        second.ShouldBeSameAs(first);
        source.ExtractionCount.ShouldBe(1);

        // Order-insensitive keying, since two callers naming the same projects are asking for one merge.
        (await source.ExtractAsync([Billing, Domain], Ct)).ShouldBeSameAs(
            await source.ExtractAsync([Domain, Billing], Ct));
    }

    [Fact]
    public async Task ExtractAsync_ASecondTime_DoesNotClearWhatTheFirstRecordedAboutTheWalk()
    {
        using var fixture = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("codebase-source-memo");
        var counting = new CountingSolutionSource();
        using var source = await CodebaseSource.CreateSpeclessAsync(
            counting, EnvironmentFor(cache.Root), fixture.SolutionPath,
            SolutionPaths.SolutionDirectoryOf(fixture.SolutionPath), false, Ct);

        await source.ExtractAsync([], Ct);
        IReadOnlySet<string> afterFirst = source.ReExtractedProjects.ToHashSet(StringComparer.Ordinal);
        await source.ExtractAsync([Billing], Ct);

        // "The projects extracted from the workspace on this run" is a fact about the run, not about the last
        // call — a memoized second call extracted nothing and must not report that as "nothing was extracted".
        afterFirst.ShouldNotBeEmpty();
        source.ReExtractedProjects.ShouldBe(afterFirst, true);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // The oracle every claim above is checked against: the extraction the cold path performs, with the
    // exclusion applied at collection rather than at merge.
    private static Task<CodebaseModel> ColdAsync(SolutionHandle handle, string[] excludeProjectNames)
    {
        return CodebaseExtractor.ExtractFromSolutionAsync(handle.Solution, excludeProjectNames, ct: Ct);
    }

    // The cache root only matters where a fact turns the cache on; elsewhere the run is disabled and reads
    // nothing, so an environment with no override is the honest thing to hand it.
    private static FakeEnvironment EnvironmentFor(string? cacheRoot = null)
    {
        var environment = new FakeEnvironment();
        return cacheRoot is null
            ? environment
            : environment.SetVariable(LoadBearingEnvVars.CacheDirectory, cacheRoot);
    }
}
