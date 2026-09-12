using System.Text.Json;
using Shouldly;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Diagnostics;
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
    ///     Checks over a run a solution filter narrowed — the same internal overload a narrowed production
    ///     run takes — so a test can put a rule's empty subject in front of a universe that is smaller than
    ///     the solution.
    /// </summary>
    public static CheckReport Run(
        CodebaseModel codebase, BaselineIndex baselines, NarrowedUniverse? narrowing, Action<Arch> define)
    {
        return ArchChecker.Check(Model(define).Rules, codebase, baselines, null, narrowing, null);
    }

    /// <summary>The same narrowed run, extracting <paramref name="source" /> first.</summary>
    public static CheckReport Run(
        string source, BaselineIndex baselines, NarrowedUniverse? narrowing, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), baselines, narrowing, define);
    }

    /// <summary>
    ///     Checks over a run part of whose model never loaded — the sibling of the narrowed overload above,
    ///     through the same internal seam, so a test can put a selection that matched nothing in front of a
    ///     model that is smaller than the codebase rather than in front of a spec defect.
    /// </summary>
    public static CheckReport Run(CodebaseModel codebase, IncompleteModel? incompleteModel, Action<Arch> define)
    {
        return ArchChecker.Check(Model(define).Rules, codebase, BaselineIndex.Empty, null, null, incompleteModel);
    }

    /// <summary>The same partial-model run, extracting <paramref name="source" /> first.</summary>
    public static CheckReport Run(string source, IncompleteModel? incompleteModel, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), incompleteModel, define);
    }

    /// <summary>The reified model of a one-off spec — for the tests whose subject is the model, not the check.</summary>
    public static ArchitectureModel Model(Action<Arch> define)
    {
        return ArchModelBuilder.Build(new InlineSpec(define));
    }

    /// <summary>
    ///     The reified canonical sample (GRAMMAR §12), built once — the model is immutable and every
    ///     caller only reads it.
    /// </summary>
    public static readonly ArchitectureModel Canonical = ArchModelBuilder.Build(new ArchSpec());

    /// <summary>
    ///     The tripwire a scope spec desugars to — a caution's only child, or a quarantine's second.
    /// </summary>
    public static ArchRule Tripwire(Action<Arch> define)
    {
        return Model(define)
            .Rules.Single(rule => rule.Scope is { Role: ScopeRole.Tripwire });
    }

    /// <summary>The rule <paramref name="id" /> names, for the tests that reach into a multi-rule model.</summary>
    /// <remarks>
    ///     A raw <c>Rules.Single(rule =&gt; rule.Id == id)</c> reds "Sequence contains no matching element",
    ///     which names neither the id that was wanted nor what the model does carry — the two things that
    ///     settle whether the spec moved or the test's expectation did.
    /// </remarks>
    public static ArchRule Rule(this ArchitectureModel model, string id)
    {
        List<ArchRule> matching = model.Rules.Where(rule => rule.Id == id)
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
    ///     <para>
    ///         Read back through a probe rule nothing can satisfy: no fixture type is named <c>ZZZ*</c>, so
    ///         every selected type lands as a shape violation and the violation list IS the selected set. The
    ///         verb is incidental — only the selection is under test — which is why it is spelled once here
    ///         rather than scaffolded at each site, and explained once rather than in a comment per site.
    ///     </para>
    ///     <para>
    ///         Its callers therefore keep this raw list rather than stating
    ///         <c>ShouldHaveFailedWithSubjects</c>: they hold no <see cref="RuleResult" />, and the probe
    ///         rule's redness is precisely the incidental fact above — a row that asserted it would be
    ///         claiming the scaffolding.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<string> Selects(CodebaseModel codebase, Func<Arch, Selection> select)
    {
        return Run(codebase, arch => arch.Rule("probe/selection")
                .Enforce(select(arch).MustHavePrefix("ZZZ"))
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

    /// <summary>
    ///     A one-rule model built by hand, bypassing spec-build validation so a check-time backstop can be
    ///     reached. Sentence and because are placeholders: a caller that needed either would build a spec.
    /// </summary>
    public static ArchitectureModel HandBuilt(string ruleId, Constraint constraint)
    {
        return new ArchitectureModel(
            [new ArchRule(ruleId, Posture.Enforce, "b", null, "sentence", constraint, null, null)], []);
    }

    /// <summary>
    ///     A metadata-only rule built by hand — no constraint, migrate or scope payload — for the renderer
    ///     rows whose subject is what a rule carries rather than what checking it produced.
    ///     <paramref name="because" /> and <paramref name="sentence" /> are placeholders; a row that asserts
    ///     on either says so by passing it.
    /// </summary>
    public static ArchRule Rule(
        string id, Posture posture = Posture.Enforce, string because = "b", string? fix = null,
        string sentence = "s", string? citation = null)
    {
        return new ArchRule(id, posture, because, fix, sentence, null, null, null, citation);
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
    /// <remarks>
    ///     <para>
    ///         These readers stay here rather than moving beside
    ///         <see cref="RuleResultAssertions" />, which now states the same claims as outcome verbs. The
    ///         split is by subject, not by verb shape: this type runs and reads a check, that one claims
    ///         things about one <see cref="RuleResult" />. Both share this namespace, so a move would buy no
    ///         call site anything, and <see cref="Selects" /> reads <see cref="ShapeSubjects" /> internally —
    ///         moving it would make the harness depend on the assertions rather than the other way round.
    ///     </para>
    ///     <para>
    ///         What stays here is the rows whose subject is the projection itself — report order, an
    ///         arbitrary slot no named reader offers, or two reads cross-compared. No row calls both a
    ///         reader and an outcome verb.
    ///     </para>
    /// </remarks>
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

    /// <summary>Member-shape violation subject SymbolIds, in report order (§4.6).</summary>
    public static IReadOnlyList<string> MemberShapeSubjects(this RuleResult result)
    {
        return result.Violators(ViolationKind.MemberShape, v => v.SubjectMember!.SymbolId);
    }

    /// <summary>Project-shape violation subject names, in report order (§4.10).</summary>
    public static IReadOnlyList<string> ProjectSubjects(this RuleResult result)
    {
        return result.Violators(ViolationKind.ProjectShape, v => v.SubjectProject!.Name);
    }

    /// <summary>
    ///     The projects <paramref name="select" /> matches over <paramref name="codebase" />, in report
    ///     order — the project-stratum twin of <see cref="Selects" /> and read back the same way, through a
    ///     probe rule nothing can satisfy.
    /// </summary>
    /// <remarks>
    ///     The probe is the escape hatch with a constant-false predicate rather than a naming verb, because
    ///     every packaging verb passes a project whose facts nothing evaluated — which would make the probe
    ///     silently under-report exactly the fixtures the absent-fact rows are built from.
    /// </remarks>
    public static IReadOnlyList<string> SelectsProjects(CodebaseModel codebase, Func<Arch, ProjectSelection> select)
    {
        return Run(codebase, arch => arch.Rule("probe/projects")
                .Enforce(select(arch).Must(_ => false, description: "be selected by the probe"))
                .Because("b"))
            .Single()
            .ProjectSubjects();
    }

    /// <summary>
    ///     This result rendered as the human failure block, relative to the current directory — the text a
    ///     developer reads from the CLI or the xUnit adapter.
    /// </summary>
    public static string HumanBlock(this RuleResult result)
    {
        return HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
    }

    /// <summary>
    ///     This report rendered as the check verb's <c>--json</c> document at <paramref name="grain" /> —
    ///     the product's wire format, full by default.
    /// </summary>
    /// <remarks>
    ///     The solution directory is the current directory, and the solution and spec names are fixed
    ///     placeholders every wire-shape pin shares: none of them is what such an assertion is about, so
    ///     spelling them once here keeps the pins reading as the slot claims they are. The grain is the one
    ///     slot a caller does vary, so it is a parameter rather than a second spelling of the whole call.
    /// </remarks>
    public static string JsonReport(this CheckReport report, DocumentGrain grain = DocumentGrain.Full)
    {
        return JsonReportRenderer.Document(
            report, Directory.GetCurrentDirectory(), "S.sln", "Spec.dll", null, [], WorkspaceDiagnostics.None, [],
            grain);
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

    /// <summary>
    ///     Asserts the first violation of the first rule renders as a project subject:
    ///     <paramref name="kind" /> over <paramref name="subjectProject" />, with <c>package</c> present
    ///     only when <paramref name="package" /> is given, and every type- and member-level slot omitted.
    ///     The fourth distinct slot combination the wire format carries.
    /// </summary>
    public static void ShouldRenderProjectShapeViolation(
        this CheckReport report, string kind, string subjectProject, string? package = null)
    {
        using JsonDocument document = JsonDocument.Parse(report.JsonReport());
        document.RootElement.GetProperty("schemaVersion")
            .GetInt32()
            .ShouldBe(3);
        JsonElement violation = FirstViolation(document);
        violation.GetProperty("kind")
            .GetString()
            .ShouldBe(kind);
        violation.GetProperty("subjectProject")
            .GetString()
            .ShouldBe(subjectProject);

        if (package is null)
            violation.TryGetProperty("package", out _)
                .ShouldBeFalse();
        else
            violation.GetProperty("package")
                .GetString()
                .ShouldBe(package);

        violation.TryGetProperty("source", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("target", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("subject", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("subjectMember", out _)
            .ShouldBeFalse();
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
