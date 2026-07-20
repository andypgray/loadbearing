using System.Text;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The four dependency verbs over the fast path (GRAMMAR §4.1, §4.3, §5.3): forbidden-set and
///     allow-set, outbound and inbound. Pins the reference universe (external exemption), strictness
///     (no implicit self-allowance), and the Source/Target orientation of inbound verbs.
/// </summary>
public sealed class DependencyVerbTests
{
    // One `new` (Maker) and one bare reference (Holder) onto the same forbidden data-layer type — the fixture
    // that separates the construction verb from the reference verb.
    private const string ConstructsAndReferences = """
                                                   namespace App.Data { public class Db {} }
                                                   namespace App.Web
                                                   {
                                                       using App.Data;
                                                       public class Maker { public Db Open() => new Db(); }
                                                       public class Holder { public Db Handle; }
                                                   }
                                                   """;

    [Fact]
    public void MustNotReference_DomainReferencesWeb_FailsWithSourceTargetSites()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Layer("Domain", "App.Domain.*").MustNotReference(arch.Layer("Web", "App.Web.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ReferencePairs().ShouldContain("App.Domain.Service -> App.Web.Controller");
        result.Violations.First(v => v.Source!.FullName == "App.Domain.Service" && v.Target!.FullName == "App.Web.Controller")
            .Sites.ShouldNotBeEmpty();
    }

    [Fact]
    public void MustNotReference_CleanDirection_Passes()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Layer("Web", "App.Web.*").MustNotReference(arch.Layer("Domain", "App.Domain.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotReference_TypeofExternalTarget_Fails()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("no-sql/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(typeof(StringBuilder)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ReferencePairs().ShouldContain("App.Domain.Service -> System.Text.StringBuilder");
    }

    [Fact]
    public void MustNotBeReferencedBy_WebByDomain_OrientsSourceAtReferencingType()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("inbound/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotBeReferencedBy(arch.Namespace("App.Domain.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        // Source is the referencing Domain type (where the edit happens); Target is the referenced Web type.
        result.ReferencePairs().ShouldContain("App.Domain.Service -> App.Web.Controller");
        result.Violations.ShouldAllBe(v => v.Target!.Namespace == "App.Web");
    }

    [Fact]
    public void MustOnlyReference_ExternalTarget_IsExemptFromComplementUniverse()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustOnlyReference(arch.Namespace("App.Domain.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        // The BCL reference (StringBuilder) is exempt; the same-layer reference (Model) is allowed.
        result.Violations.ShouldAllBe(v => !v.Target!.IsExternal);
        result.ReferencePairs().ShouldNotContain("App.Domain.Service -> App.Domain.Model");
        result.ReferencePairs().ShouldContain("App.Domain.Service -> App.Web.Controller");
    }

    [Fact]
    public void MustOnlyReference_IsStrict_UnlistedSameLayerReferenceIsRed()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustOnlyReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        // Allowed = Web only; the Domain→Domain edge (Service→Model) is not implicitly self-allowed.
        result.ReferencePairs().ShouldContain("App.Domain.Service -> App.Domain.Model");
    }

    [Fact]
    public void MustOnlyBeReferencedBy_InboundFromOutsideAllowSet_IsRed()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("contain/x")
                    .Enforce(arch.Namespace("App.Web.*").MustOnlyBeReferencedBy(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        // Web may be referenced only by Web; the inbound Domain→Web edge is a violation.
        result.ReferencePairs().ShouldContain("App.Domain.Service -> App.Web.Controller");
    }

    [Fact]
    public void MustNotReference_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — it is inert.
        // A pattern operand (not a bare typeof) is the warning gate (ConstraintEvaluator.ForbiddenReference).
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(CheckWarningKind.InertTarget);
        warning.Message.ShouldBe("This rule is inert: its target selection matched no types.");
    }

    [Fact]
    public void MustNotConstruct_SubjectNewsTarget_FailsWithConstructionPair()
    {
        // The construction verb sits beside the reference verbs but walks object-creation edges: only Maker's
        // `new Db()` trips — Holder's Db field is a reference, invisible to MustNotConstruct.
        RuleResult result = Checker.Run(ConstructsAndReferences, arch =>
                arch.Rule("di/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotConstruct(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ConstructionPairs().ShouldBe(["App.Web.Maker -> App.Data.Db"]);
    }

    [Fact]
    public void MustNotConstruct_ReferenceWithoutConstruction_Passes()
    {
        // The reference/construction split, pinned: Holder holds a Db field (a reference edge) but never `new`s
        // it, so MustNotConstruct is silent exactly where MustNotReference would fire.
        RuleResult result = Checker.Run(ConstructsAndReferences, arch =>
                arch.Rule("di/x")
                    .Enforce(arch.Namespace("App.Web.*").WithSuffix("Holder").MustNotConstruct(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
    }
}