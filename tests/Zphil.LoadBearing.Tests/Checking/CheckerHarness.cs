using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
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
        return ArchChecker.Check(ArchModelBuilder.Build(new InlineSpec(define)), codebase);
    }

    public static CheckReport Run(string source, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), define);
    }

    public static CheckReport Run(CodebaseModel codebase, BaselineIndex baselines, Action<Arch> define)
    {
        return ArchChecker.Check(ArchModelBuilder.Build(new InlineSpec(define)), codebase, baselines);
    }

    public static CheckReport Run(string source, BaselineIndex baselines, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), baselines, define);
    }

    public static CheckReport Run(CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff, Action<Arch> define)
    {
        return ArchChecker.Check(ArchModelBuilder.Build(new InlineSpec(define)), codebase, baselines, diff);
    }

    /// <summary>The single rule result — most specs under test carry exactly one rule.</summary>
    public static RuleResult Single(this CheckReport report)
    {
        return report.Results.Single();
    }

    /// <summary>The rule result for a given ID (for multi-rule specs, e.g. a desugared Freeze scope).</summary>
    public static RuleResult ForRule(this CheckReport report, string ruleId)
    {
        return report.Results.Single(r => r.Rule.Id == ruleId);
    }

    /// <summary>Reference violations rendered as <c>Source -&gt; Target</c>, in report order.</summary>
    public static IReadOnlyList<string> ReferencePairs(this RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.Reference)
            .Select(v => $"{v.Source!.FullName} -> {v.Target!.FullName}")
            .ToList();
    }

    /// <summary>Construction violations rendered as <c>Source -&gt; Constructed</c>, in report order (§4.5).</summary>
    public static IReadOnlyList<string> ConstructionPairs(this RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.Construction)
            .Select(v => $"{v.Source!.FullName} -> {v.Target!.FullName}")
            .ToList();
    }

    /// <summary>Injection violations rendered as <c>Source -&gt; Injected</c>, in report order (§4.7).</summary>
    public static IReadOnlyList<string> InjectionPairs(this RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.Injection)
            .Select(v => $"{v.Source!.FullName} -> {v.Target!.FullName}")
            .ToList();
    }

    /// <summary>Catch violations rendered as <c>Source -&gt; Caught</c>, in report order (§4.8).</summary>
    public static IReadOnlyList<string> CatchPairs(this RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.Catch)
            .Select(v => $"{v.Source!.FullName} -> {v.Target!.FullName}")
            .ToList();
    }

    /// <summary>Throw violations rendered as <c>Source -&gt; Thrown</c>, in report order (§4.8).</summary>
    public static IReadOnlyList<string> ThrowPairs(this RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.Throw)
            .Select(v => $"{v.Source!.FullName} -> {v.Target!.FullName}")
            .ToList();
    }

    /// <summary>Shape-violation subject FullNames, in report order.</summary>
    public static IReadOnlyList<string> ShapeSubjects(this RuleResult result)
    {
        return result.Violations
            .Where(v => v.Kind == ViolationKind.Shape)
            .Select(v => v.Subject!.FullName)
            .ToList();
    }
}