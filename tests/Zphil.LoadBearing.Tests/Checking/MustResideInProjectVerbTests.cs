using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The residence verb <c>MustResideInProject</c> over the fast path (GRAMMAR §5.3, §4.1, §4.3): a
///     subject is red unless the named project declares it, and declaration is N-way — one source file
///     compiled into several projects resides in every one of them, so <em>any</em> declarer satisfies the
///     verb while a project that merely references the declaration does not.
/// </summary>
/// <remarks>
///     The verb reads the declarer roster extraction already records, so it adds no extracted fact, no
///     cache-schema change and nothing for the extraction tests to cover. The bed is
///     <see cref="MultiplyDeclaredCodebase" />, described in full in its own remarks — Core is the winner
///     declarer of the linked file, Tool and Stub compile their own copies, and Client references Core's
///     declaration rather than compiling it, which is the negative half of the any-declarer claim.
///     Violations are <see cref="ViolationKind.Shape" />: identity is the subject symbol ID riding
///     <see cref="BaselineEntry.ForSubject" />, and the subject's declaration sites are evidence, never
///     identity.
/// </remarks>
public sealed class MustResideInProjectVerbTests
{
    [Fact]
    public void MustResideInProject_SubjectDeclaredByAnotherProject_FailsWithSubjectAndDeclarationSites()
    {
        // A shape violation has no edge to cite, so the file:line an agent jumps to is where the offending
        // type is declared — the subject's own declaration sites, carried as evidence.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-in-core")
                    .Enforce(arch.Namespace("Tool.*").MustResideInProject("Core"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjectAtSites("Tool.Command", ["Command.cs:3"]);
    }

    [Fact]
    public void MustResideInProject_EverySubjectDeclaredByTheNamedProject_PassesClean()
    {
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/core-in-core")
                    .Enforce(arch.Namespace("Core.*").MustResideInProject("Core"))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustResideInProject_MultiplyDeclaredSubject_IsSatisfiedByAnyDeclarerAndRedByANonDeclarer()
    {
        // The shared types are one node each, whose facts follow Core, the first declarer. Naming Tool — a
        // project that compiles its own copy — satisfies the verb all the same, because residence is the
        // declarer roster and not the single ProjectName the node carries.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/shared-in-tool")
                    .Enforce(arch.Namespace("Shared.*").MustResideInProject("Tool"))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();

        // The negative half over a project that exists: Client references the declaration rather than
        // compiling it, so it is on no roster and both shared types red.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/shared-in-client")
                    .Enforce(arch.Namespace("Shared.*").MustResideInProject("Client"))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["Shared.Widget", "Shared.WidgetPart"]);
    }

    [Fact]
    public void MustResideInProject_UnknownProjectName_RedsEverySubject()
    {
        // A misspelt project name is on no roster, so every subject fails. That is the documented behaviour,
        // not a gap: the name is never held against the codebase at build time (validation runs before any
        // workspace exists), so a loud red at check time is how the typo surfaces at all.
        RuleResult result = Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/typo")
                    .Enforce(arch.Types.MustResideInProject("Cor"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(
        [
            "Shared.Widget", "Shared.WidgetPart", "Core.CoreOnly", "Core.User", "Tool.Command",
            "Client.Consumer"
        ]);
    }

    [Fact]
    public void MustResideInProject_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the residence verb takes the same subject gate.
        RuleResult result = Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/empty")
                    .Enforce(arch.Namespace("Nowhere.*").MustResideInProject("Core"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustResideInProject_GrandfatheredSubjectPasses_NewMisplacedTypeStaysRed()
    {
        // Identity is the subject symbol ID (GRAMMAR §4.3), one entry per misplaced type. Tool.Command is
        // blessed; Client.Consumer is a distinct identity the same rule keeps red.
        BaselineIndex index = Checker.Baselines("layering/consolidate", BaselineEntry.ForSubject("T:Tool.Command"));

        RuleResult result = Checker.Run(MultiplyDeclaredCodebase.Model, index, arch =>
                arch.Rule("layering/consolidate")
                    .Migrate(
                        "Satellite projects still declare types of their own.",
                        arch.AnyOf(arch.Namespace("Tool.*"), arch.Namespace("Client.*"))
                            .MustResideInProject("Core"))
                    .Because("One project owns the product's types."))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Client.Consumer"]);
        result.ShouldHaveGrandfathered(1);
    }
}
