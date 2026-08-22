using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Prompts;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     Holds the <c>derive_spec</c> recipe's authoring reference to the shipped constraint vocabulary:
///     every public <see cref="Constraint" />-returning verb on the fluent surface must be named
///     somewhere in the served prompt, or carry a <see cref="WithheldVerbs" /> entry saying why it is
///     deliberately not taught.
/// </summary>
/// <remarks>
///     Exists because the vocabulary and the recipe drifted apart silently — verbs shipped across two
///     releases with the recipe never learning them, and nothing red. Name presence is the whole
///     contract: how a verb is taught stays a prose judgment, so this gate checks reachability, never
///     wording. The withheld registry is two-sided like the doc-hygiene exemptions: an entry for a verb
///     that no longer ships is dead, and an entry for a verb the prompt does teach is stale — both red.
/// </remarks>
public sealed class DeriveSpecVocabularyTests
{
    // A verb withheld from the recipe on purpose: name → the reason. Empty today — the recipe teaches
    // the whole shipped vocabulary and routes to GRAMMAR.md for depth.
    private static readonly IReadOnlyDictionary<string, string> WithheldVerbs =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void EveryShippedConstraintVerb_IsTaughtByTheRecipe_OrExplicitlyWithheld()
    {
        string body = ArchPrompts.DeriveSpec();

        List<string> missing = ShippedConstraintVerbNames()
            .Where(name => !WithheldVerbs.ContainsKey(name))
            .Where(name => !AppearsIn(body, name))
            .ToList();

        missing.ShouldBeEmpty(
            "every shipped constraint verb must appear in derive-spec.md, or carry a WithheldVerbs entry saying why not");
    }

    [Fact]
    public void WithheldVerbs_EachStillShips()
    {
        IReadOnlyCollection<string> shipped = ShippedConstraintVerbNames();

        List<string> dead = WithheldVerbs.Keys
            .Where(name => !shipped.Contains(name, StringComparer.Ordinal))
            .ToList();

        dead.ShouldBeEmpty("a WithheldVerbs entry for a verb that no longer ships is dead — delete it");
    }

    [Fact]
    public void WithheldVerbs_EachStaysOutOfTheRecipe()
    {
        string body = ArchPrompts.DeriveSpec();

        List<string> stale = WithheldVerbs.Keys
            .Where(name => AppearsIn(body, name))
            .ToList();

        stale.ShouldBeEmpty(
            "a WithheldVerbs entry for a verb the recipe teaches is stale — delete it so the verb is gated again");
    }

    [Fact]
    public void TheReflectionSweep_SeesTheVocabulary()
    {
        // Blind-scanner guard: if the sweep's filter rots, the reachability fact above would pass
        // vacuously over an empty verb list rather than red.
        ShippedConstraintVerbNames()
            .Count.ShouldBeGreaterThan(30);
    }

    private static IReadOnlyCollection<string> ShippedConstraintVerbNames()
    {
        return typeof(Constraint).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.ReturnType == typeof(Constraint))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // Word-bounded so a verb whose name prefixes another (MustNotCatch, MustNotCatchUnfiltered) is
    // only satisfied by its own mention.
    private static bool AppearsIn(string body, string verbName)
    {
        return Regex.IsMatch(body, $@"\b{Regex.Escape(verbName)}\b");
    }
}
