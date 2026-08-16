using System.Text.Json;
using Shouldly;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>Wraps an <see cref="Action{Arch}" /> as a one-off spec so tests can build models inline.</summary>
internal sealed class InlineSpec(Action<Arch> define) : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        define(arch);
    }
}

/// <summary>Fast-path checker harness: extract a codebase from source, build a one-rule model, check.</summary>
internal static class Checker
{
    public static CheckReport Run(CodebaseModel codebase, Action<Arch> define)
    {
        return ArchChecker.Check(Model(define), codebase);
    }

    public static CheckReport Run(string source, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), define);
    }

    public static CheckReport Run(CodebaseModel codebase, BaselineIndex baselines, Action<Arch> define)
    {
        return ArchChecker.Check(Model(define), codebase, baselines);
    }

    public static CheckReport Run(string source, BaselineIndex baselines, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), baselines, define);
    }

    public static CheckReport Run(CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff, Action<Arch> define)
    {
        return ArchChecker.Check(Model(define), codebase, baselines, diff);
    }

    /// <summary>
    ///     Checks over a run a solution filter narrowed — the internal overload the CLI and the adapter
    ///     reach through <c>ArchCheckSequence</c>, so a test can put a rule's empty subject in front of a
    ///     universe that is smaller than the solution.
    /// </summary>
    public static CheckReport Run(
        CodebaseModel codebase, BaselineIndex baselines, NarrowedUniverse? narrowing, Action<Arch> define)
    {
        return ArchChecker.Check(Model(define).Rules, codebase, baselines, null, narrowing);
    }

    /// <summary>The same narrowed run, extracting <paramref name="source" /> first.</summary>
    public static CheckReport Run(
        string source, BaselineIndex baselines, NarrowedUniverse? narrowing, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), baselines, narrowing, define);
    }

    /// <summary>The reified model of a one-off spec — for the tests whose subject is the model, not the check.</summary>
    public static ArchitectureModel Model(Action<Arch> define)
    {
        return ArchModelBuilder.Build(new InlineSpec(define));
    }

    /// <summary>The rule <paramref name="id" /> names, for the tests that reach into a multi-rule model.</summary>
    /// <remarks>
    ///     A raw <c>Rules.Single(rule =&gt; rule.Id == id)</c> reds "Sequence contains no matching element",
    ///     which names neither the id that was wanted nor what the model does carry — the two things that
    ///     settle whether the spec moved or the test's expectation did.
    /// </remarks>
    public static ArchRule Rule(this ArchitectureModel model, string id)
    {
        var matching = model.Rules.Where(rule => rule.Id == id)
            .ToList();
        string declared = string.Join(", ", model.Rules.Select(rule => rule.Id));

        return matching.ShouldHaveSingleItem($"Looking for rule '{id}'; the model declares: {declared}.");
    }

    /// <summary>
    ///     The rendered sentence of a one-rule spec carrying <paramref name="constraint" /> — the model is
    ///     the sole source of prose, so two spellings that render one sentence reified to one thing.
    /// </summary>
    public static string Sentence(Func<Arch, Constraint> constraint)
    {
        return Model(arch => arch.Rule("area/rule")
                .Enforce(constraint(arch))
                .Because("b"))
            .Rules.Single()
            .Sentence;
    }

    /// <summary>
    ///     The subjects <paramref name="select" /> matches over <paramref name="codebase" />, in report
    ///     order — for the rows whose whole question is which types a selection reaches.
    /// </summary>
    /// <remarks>
    ///     Read back through a probe rule nothing can satisfy: no fixture type is named <c>ZZZ*</c>, so every
    ///     selected type lands as a shape violation and the violation list IS the selected set. The verb is
    ///     incidental — only the selection is under test — which is why it is spelled once here rather than
    ///     scaffolded at each site, and explained once rather than in a comment per site.
    /// </remarks>
    public static IReadOnlyList<string> Selects(CodebaseModel codebase, Func<Arch, Selection> select)
    {
        return Run(codebase, arch => arch.Rule("probe/selection")
                .Enforce(select(arch)
                    .MustHavePrefix("ZZZ"))
                .Because("b"))
            .Single()
            .ShapeSubjects();
    }

    /// <summary>A baseline index carrying <paramref name="entries" /> under one rule — the ratchet's input.</summary>
    public static BaselineIndex Baselines(string ruleId, params BaselineEntry[] entries)
    {
        return new BaselineIndex(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal)
        {
            [ruleId] = new(entries)
        });
    }

    /// <summary>The single rule result — most specs under test carry exactly one rule.</summary>
    public static RuleResult Single(this CheckReport report)
    {
        return report.Results.Single();
    }

    /// <summary>The rule result for a given ID (for multi-rule specs, e.g. a desugared Quarantine scope).</summary>
    public static RuleResult ForRule(this CheckReport report, string ruleId)
    {
        return report.Results.Single(r => r.Rule.Id == ruleId);
    }

    /// <summary>
    ///     The violations of one <paramref name="kind" />, each rendered by <paramref name="identify" />, in
    ///     report order — the one walk every violation-listing assertion below is a naming of.
    /// </summary>
    public static IReadOnlyList<string> Violators(
        this RuleResult result, ViolationKind kind, Func<Violation, string> identify)
    {
        return result.Violations.Where(v => v.Kind == kind)
            .Select(identify)
            .ToList();
    }

    /// <summary>Reference violations rendered as <c>Source -&gt; Target</c>, in report order.</summary>
    public static IReadOnlyList<string> ReferencePairs(this RuleResult result)
    {
        return result.Violators(ViolationKind.Reference, Pair);
    }

    /// <summary>Construction violations rendered as <c>Source -&gt; Constructed</c>, in report order (§4.5).</summary>
    public static IReadOnlyList<string> ConstructionPairs(this RuleResult result)
    {
        return result.Violators(ViolationKind.Construction, Pair);
    }

    /// <summary>Injection violations rendered as <c>Source -&gt; Injected</c>, in report order (§4.7).</summary>
    public static IReadOnlyList<string> InjectionPairs(this RuleResult result)
    {
        return result.Violators(ViolationKind.Injection, Pair);
    }

    /// <summary>Catch violations rendered as <c>Source -&gt; Caught</c>, in report order (§4.8).</summary>
    public static IReadOnlyList<string> CatchPairs(this RuleResult result)
    {
        return result.Violators(ViolationKind.Catch, Pair);
    }

    /// <summary>Throw violations rendered as <c>Source -&gt; Thrown</c>, in report order (§4.8).</summary>
    public static IReadOnlyList<string> ThrowPairs(this RuleResult result)
    {
        return result.Violators(ViolationKind.Throw, Pair);
    }

    /// <summary>Exposure violations rendered as <c>Source -&gt; Exposed</c>, in report order (§4.9).</summary>
    public static IReadOnlyList<string> ExposurePairs(this RuleResult result)
    {
        return result.Violators(ViolationKind.Expose, Pair);
    }

    /// <summary>Shape-violation subject FullNames, in report order.</summary>
    public static IReadOnlyList<string> ShapeSubjects(this RuleResult result)
    {
        return result.Violators(ViolationKind.Shape, v => v.Subject!.FullName);
    }

    /// <summary>
    ///     This result rendered as the human failure block, relative to the current directory — the text a
    ///     developer reads from the CLI or the xUnit adapter.
    /// </summary>
    public static string HumanBlock(this RuleResult result)
    {
        return HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
    }

    /// <summary>This report rendered as the check verb's <c>--json</c> document — the product's wire format.</summary>
    /// <remarks>
    ///     The solution directory is the current directory, and the solution and spec names are fixed
    ///     placeholders every wire-shape pin shares: none of them is what such an assertion is about, so
    ///     spelling them once here keeps the pins reading as the slot claims they are.
    /// </remarks>
    public static string JsonReport(this CheckReport report)
    {
        return JsonReportRenderer.Document(
            report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, [], WorkspaceDiagnostics.None, [],
            DocumentGrain.Full);
    }

    /// <summary>
    ///     Asserts the first violation of the first rule renders as an edge: <paramref name="kind" /> over
    ///     <paramref name="source" /> and <paramref name="target" /> with sites, and every member and subject
    ///     slot omitted rather than null (which is what holds the schema at version 3).
    /// </summary>
    public static void ShouldRenderEdgeViolation(this CheckReport report, string kind, string source, string target)
    {
        using JsonDocument document = JsonDocument.Parse(report.JsonReport());
        document.RootElement.GetProperty("schemaVersion")
            .GetInt32()
            .ShouldBe(3);
        JsonElement violation = FirstViolation(document);
        violation.GetProperty("kind")
            .GetString()
            .ShouldBe(kind);
        violation.GetProperty("source")
            .GetString()
            .ShouldBe(source);
        violation.GetProperty("target")
            .GetString()
            .ShouldBe(target);
        violation.TryGetProperty("targetMember", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("subject", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("subjectMember", out _)
            .ShouldBeFalse();
        violation.GetProperty("sites")
            .GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    /// <summary>
    ///     Asserts the first violation of the first rule renders as a member subject: <paramref name="kind" />
    ///     over <paramref name="subjectMember" /> with sites, and the type-level and target slots omitted.
    /// </summary>
    public static void ShouldRenderMemberShapeViolation(this CheckReport report, string kind, string subjectMember)
    {
        using JsonDocument document = JsonDocument.Parse(report.JsonReport());
        document.RootElement.GetProperty("schemaVersion")
            .GetInt32()
            .ShouldBe(3);
        JsonElement violation = FirstViolation(document);
        violation.GetProperty("kind")
            .GetString()
            .ShouldBe(kind);
        violation.GetProperty("subjectMember")
            .GetString()
            .ShouldBe(subjectMember);
        violation.TryGetProperty("subject", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("target", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("targetMember", out _)
            .ShouldBeFalse();
        violation.GetProperty("sites")
            .GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    /// <summary>
    ///     Asserts the first violation of the first rule renders as a use of a member: <paramref name="kind" />
    ///     over <paramref name="source" /> and <paramref name="targetMember" /> with sites, and the type-level
    ///     target and subject slots omitted. The third distinct slot combination the wire format carries.
    /// </summary>
    public static void ShouldRenderTargetMemberViolation(
        this CheckReport report, string kind, string source, string targetMember)
    {
        using JsonDocument document = JsonDocument.Parse(report.JsonReport());
        document.RootElement.GetProperty("schemaVersion")
            .GetInt32()
            .ShouldBe(3);
        JsonElement violation = FirstViolation(document);
        violation.GetProperty("kind")
            .GetString()
            .ShouldBe(kind);
        violation.GetProperty("source")
            .GetString()
            .ShouldBe(source);
        violation.GetProperty("targetMember")
            .GetString()
            .ShouldBe(targetMember);
        violation.TryGetProperty("target", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("subject", out _)
            .ShouldBeFalse();
        violation.GetProperty("sites")
            .GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    private static string Pair(Violation v)
    {
        return $"{v.Source!.FullName} -> {v.Target!.FullName}";
    }

    private static JsonElement FirstViolation(JsonDocument document)
    {
        return document.RootElement.GetProperty("rules")[0]
            .GetProperty("violations")[0];
    }
}
