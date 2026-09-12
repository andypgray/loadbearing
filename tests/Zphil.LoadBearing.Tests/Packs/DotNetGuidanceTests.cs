using System.Reflection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Packs.DotNet;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.DocHygiene;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Packs;

/// <summary>
///     The pack's own surface: nine IDs pinned one method at a time, <c>ApplyAll</c>'s roster and
///     ordering, the posture and <c>Fix</c> contract between pack and consumer, and the anchor doctrine
///     that lets a pack ship inside the codebase it governs.
/// </summary>
public class DotNetGuidanceTests
{
    private const string SubjectNamespace = "Sample.*";

    private static readonly string[] CanonicalOrder =
    [
        "http/reuse-httpclient",
        "di/no-service-locator",
        "di/no-buildserviceprovider",
        "async/no-sync-over-async",
        "di/no-captive-dependencies",
        "naming/async-suffix",
        "exceptions/no-general-catch",
        "async/accept-cancellation",
        "persistence/no-mapping-attributes"
    ];

    /// <summary>The nine pack rule IDs, in the order <c>ApplyAll</c> declares them.</summary>
    public static TheoryData<string> PackRuleIds => [.. CanonicalOrder];

    [Theory]
    [MemberData(nameof(PackRuleIds))]
    public void Method_CalledAlone_DeclaresExactlyItsOwnId(string id)
    {
        // A la carte is the whole point: calling one method lands one rule, and nothing else rides along.
        ArchitectureModel model = Checker.Model(arch => DeclareOne(arch, id, PackPosture.Enforce));

        model.Rules.Select(rule => rule.Id)
            .ShouldBe([id]);
    }

    [Fact]
    public void ApplyAll_LandsTheNineInCanonicalOrder()
    {
        ArchitectureModel model = Checker.Model(arch => ApplyAll(arch, PackPosture.Enforce));

        model.Rules.Select(rule => rule.Id)
            .ShouldBe(CanonicalOrder);
    }

    [Fact]
    public void ApplyAll_CoversEveryPublicRuleMethod()
    {
        // The ratchet: add a tenth rule method and forget to wire it into ApplyAll, and this counts 9
        // rules against 10 methods. Nothing else in the suite would notice. Returning void is what makes
        // a method a rule method — the pack publishes that, since it is what makes a second trailer
        // uncompilable — so the teeth stay on a tenth rule while a set the pack exports for a consumer's
        // own rule to ban is not counted as one.
        List<string> ruleMethods = typeof(DotNetGuidance)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(void))
            .Where(method => method.Name != nameof(DotNetGuidance.ApplyAll))
            .Select(method => method.Name)
            .ToList();

        ArchitectureModel model = Checker.Model(arch => ApplyAll(arch, PackPosture.Enforce));

        model.Rules.Count.ShouldBe(ruleMethods.Count);
    }

    [Fact]
    public void ApplyAll_AtEnforce_GivesEveryRuleABecauseAFixAndACitation()
    {
        ArchitectureModel model = Checker.Model(arch => ApplyAll(arch, PackPosture.Enforce));

        model.Rules.ShouldAllBe(rule => rule.Posture == Posture.Enforce);
        model.Rules.ShouldAllBe(rule => !string.IsNullOrWhiteSpace(rule.Because));
        model.Rules.ShouldAllBe(rule => !string.IsNullOrWhiteSpace(rule.Fix));

        // Every rule in this pack quotes canonical Microsoft guidance, so every one of them cites the page
        // it quotes: the provenance pillar the pack exists to carry, and now a field rather than a suffix
        // on the reason.
        model.Rules.ShouldAllBe(rule => rule.Citation!.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal));
    }

    [Fact]
    public void RuleProse_ReasonAndCitation_AreSeparateFields()
    {
        // The reason ends as a sentence and the page rides beside it. Pinned on one rule because the split
        // is what the whole corpus migration did: before the trailer, the URL was the reason's last token
        // and every consuming surface had to read it back out of prose.
        ArchRule rule = Checker.Model(arch => DeclareOne(arch, "http/reuse-httpclient", PackPosture.Enforce))
            .Rules.Single();

        rule.Because.ShouldBe(
            "A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers.");
        rule.Citation.ShouldBe(
            "https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines");
    }

    [Fact]
    public void ApplyAll_AtMigrate_CarriesTheProseAndTheConventionalBaselinePath()
    {
        const string from = "Most of this codebase predates the guidance.";

        ArchitectureModel model = Checker.Model(arch => ApplyAll(arch, PackPosture.Migrate(from)));

        model.Rules.ShouldAllBe(rule => rule.Posture == Posture.Migrate);
        model.Rules.ShouldAllBe(rule => rule.Migrate!.From == from);

        // The pack never names a baseline path; every Migrate rule takes the conventional default
        // derived from its own ID, so a consumer's grandfather store lands where the tooling looks.
        foreach (ArchRule rule in model.Rules)
            rule.BaselinePath.ShouldBe($"arch/baselines/{rule.Id}.json");
    }

    [Fact]
    public void FixOverride_ReplacesTheDefault_AndExactlyOneFixReachesTheRule()
    {
        const string overridden = "Take IPartnerClient in the constructor; MeridianHost owns the wiring.";

        ArchitectureModel defaulted = Checker.Model(arch => DeclareOne(arch, "di/no-service-locator", PackPosture.Enforce));
        ArchitectureModel overrode = Checker.Model(arch =>
            DotNetGuidance.NoServiceLocator(arch, arch.Types.InNamespace(SubjectNamespace), arch.Namespace("Sample.Host.*"),
                PackPosture.Enforce, overridden));

        overrode.Rules.Single()
            .Fix.ShouldBe(overridden);
        defaulted.Rules.Single()
            .Fix.ShouldNotBe(overridden);

        // A repeated Fix is a spec-build error (a rule carries at most one), so the model building at
        // all is the proof that the override replaced the default rather than being appended to it.
        overrode.Rules.Single()
            .Because.ShouldBe(defaulted.Rules.Single()
                .Because);
    }

    [Fact]
    public void SameRule_EnforceInOneSpecAndMigrateInAnother_IsTheConsumersChoice()
    {
        ArchitectureModel enforced = Checker.Model(arch => DeclareOne(arch, "naming/async-suffix", PackPosture.Enforce));
        ArchitectureModel migrated = Checker.Model(arch =>
            DeclareOne(arch, "naming/async-suffix", PackPosture.Migrate("Task-returning methods here are bare-named.")));

        enforced.Rules.Single()
            .Posture.ShouldBe(Posture.Enforce);
        migrated.Rules.Single()
            .Posture.ShouldBe(Posture.Migrate);

        // Posture belongs to the consumer; the reason the rule exists — and the page that reason rests on —
        // belongs to the pack, so both ride either posture.
        migrated.Rules.Single()
            .Because.ShouldBe(enforced.Rules.Single()
                .Because);
        migrated.Rules.Single()
            .Citation.ShouldBe(enforced.Rules.Single()
                .Citation);
        migrated.Rules.Single()
            .Sentence.ShouldBe(enforced.Rules.Single()
                .Sentence);
    }

    [Fact]
    public void PackSource_UsesNoExpressionMemberAnchors()
    {
        // The anchor doctrine, pinned in the cheapest way that survives a reformat: an expression anchor
        // in the pack would be real syntax minting a use edge attributed to the pack's own type, which
        // sits inside the cone this repo's spec governs. nameof operands mint nothing. Phrased over every
        // tracked source in the pack rather than over the one file the doctrine was written for, so a
        // second pack file cannot land outside it.
        IReadOnlyList<string> sources = PackSources();
        List<string> expressive = new();
        foreach (string path in sources)
        {
            string source = RepoRoot.ReadText(path);
            if (source.Contains("arch.Member<", StringComparison.Ordinal)
                || source.Contains("arch.Member(()", StringComparison.Ordinal))
                expressive.Add(path);
        }

        // The sweep is held non-empty rather than trusted: a glob that matches nothing passes silently.
        sources.ShouldNotBeEmpty("No tracked C# source was found in the pack project; the arm below swept nothing.");
        expressive.ShouldReportNothing(
            "Pack source(s) writing an expression member anchor, which is real syntax minting a use edge "
            + "attributed to the pack's own type inside the cone this repository's spec governs");
    }

    // Every tracked C# source in the rule pack, repository-relative.
    private static IReadOnlyList<string> PackSources()
    {
        return TrackedFiles.CSharp
            .Where(static path => path.StartsWith("src/Zphil.LoadBearing.Packs.DotNet/", StringComparison.Ordinal))
            .ToList();
    }

    private static void ApplyAll(Arch arch, PackPosture posture)
    {
        DotNetGuidance.ApplyAll(
            arch,
            arch.Types.InNamespace(SubjectNamespace),
            arch.Namespace("Sample.Host.*"),
            arch.Registered(Lifetime.Singleton),
            arch.Types.DerivedFrom<BackgroundService>(),
            posture);
    }

    // The method↔ID mapping written out longhand, which is exactly what makes it a pin: the pack derives
    // its method names from its IDs by convention, and this is the table that would disagree if either
    // side drifted.
    private static void DeclareOne(Arch arch, string id, PackPosture posture)
    {
        Selection subject = arch.Types.InNamespace(SubjectNamespace);
        Selection host = arch.Namespace("Sample.Host.*");

        switch (id)
        {
            case "http/reuse-httpclient":
                DotNetGuidance.ReuseHttpClient(arch, subject, host, posture);
                break;
            case "di/no-service-locator":
                DotNetGuidance.NoServiceLocator(arch, subject, host, posture);
                break;
            case "di/no-buildserviceprovider":
                DotNetGuidance.NoBuildServiceProvider(arch, subject, posture);
                break;
            case "async/no-sync-over-async":
                DotNetGuidance.NoSyncOverAsync(arch, subject, posture);
                break;
            case "di/no-captive-dependencies":
                DotNetGuidance.NoCaptiveDependencies(arch, arch.Registered(Lifetime.Singleton), posture);
                break;
            case "naming/async-suffix":
                DotNetGuidance.AsyncSuffix(arch, subject, posture);
                break;
            case "exceptions/no-general-catch":
                DotNetGuidance.NoGeneralCatch(arch, subject, arch.Types.DerivedFrom<BackgroundService>(), posture);
                break;
            case "async/accept-cancellation":
                DotNetGuidance.AcceptCancellation(arch, subject, posture);
                break;
            case "persistence/no-mapping-attributes":
                DotNetGuidance.NoMappingAttributes(arch, subject, posture);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown pack rule ID.");
        }
    }
}
