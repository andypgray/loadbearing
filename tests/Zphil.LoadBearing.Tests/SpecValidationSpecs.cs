namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The failing specs the validation catalog is asserted against (GRAMMAR §8): one per code, plus the
///     multi-error and anchor-steering fixtures. Split out of the test file so that editing a test can
///     never move a fixture, and named for what each one is wrong about.
/// </summary>
/// <remarks>
///     <b>This file is line-sensitive and must not be run through a member-reordering cleanup profile.</b>
///     Every anchor here captures its own line through <c>[CallerLineNumber]</c>, and the catalog pins a
///     number of those <c>file:line</c> prefixes verbatim. Splitting the fixtures out does not remove that
///     sensitivity — it confines it to a file nobody edits to change a test.
/// </remarks>
internal sealed class DuplicateIdSpecA : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("A.");
    }
}

internal sealed class DuplicateIdSpecB : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("B.");
    }
}

internal sealed class IdExtendsScopeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("legacy/billing").Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .Dragons("Dragons.").Because("Quarantined.");
        arch.Rule("legacy/billing/foo").Enforce(arch.Types.MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class DanglingRuleSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/dangling");
    }
}

internal sealed class MissingBecauseRuleSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I"));
    }
}

internal sealed class MissingBecauseScopeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("legacy/billing").Quarantine(arch.Namespace("MyApp.Legacy.Billing.*")).Dragons("Dragons.");
    }
}

internal sealed class MissingDragonsSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("legacy/billing").Quarantine(arch.Namespace("MyApp.Legacy.Billing.*")).Because("Quarantined.");
    }
}

internal sealed class BlankDescriptionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.Must(_ => true, "")).Because("Reason.");
    }
}

internal sealed class MultiLineBecauseSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("line one\nline two");
    }
}

internal sealed class RepeatedBecauseSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("First.").Because("Second.");
    }
}

internal sealed class MalformedIdSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("Bad_Id").Enforce(arch.Types.MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class DoublePostureRuleSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The stage machine forbids the fluent double-call, but a stored IRuleBuilder is mutable, so a
        // second posture verb silently overwrites the first (§8 item 17). Only one .Because so the
        // repeated posture is the sole error.
        IRuleBuilder rule = arch.Rule("area/rule");
        rule.Enforce(arch.Types.MustHavePrefix("I"));
        rule.Migrate("Controllers open SqlConnection directly.", arch.Types.MustHaveSuffix("Handler"))
            .Because("Reason.");
    }
}

internal sealed class DoubleQuarantineScopeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // A stored IScopeBuilder re-called with .Quarantine silently overwrites the quarantined selection (§8 item 17).
        IScopeBuilder scope = arch.Scope("legacy/billing");
        scope.Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"));
        scope.Quarantine(arch.Namespace("MyApp.Legacy.Other.*")).Dragons("Dragons.").Because("Quarantined.");
    }
}

internal sealed class EmptyBoundarySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("legacy/billing").Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia().Dragons("Dragons.").Because("Quarantined.");
    }
}

internal sealed class DuplicateLayerSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Dup", "MyApp.A.*");
        arch.Layer("Dup", "MyApp.B.*");
    }
}

internal sealed class ForeignSelectionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        Selection foreign = other.Types.OfKind(TypeKind.Interface);
        arch.Rule("area/rule").Enforce(foreign.MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class BlankMemberNameSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), " "))).Because("Reason.");
    }
}

internal sealed class TypoMemberSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), "Nows"))).Because("Reason.");
    }
}

internal sealed class BaseTypeMemberSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Wait lives on the non-generic base Task, not on Task<TResult> — the base-type guidance case.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(Task<>), "Wait"))).Because("Reason.");
    }
}

internal sealed class ForeignMemberSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        Member foreign = other.Member(typeof(DateTime), nameof(DateTime.Now));
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(foreign)).Because("Reason.");
    }
}

internal sealed class ValidMemberUseSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("time/inject-clock")
            .Migrate(
                "Code reads the ambient clock directly.",
                arch.Types.MustNotUse(
                    arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                    arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
            .Because("Wall-clock reads are untestable; inject IClock — ADR-nnn.")
            .Fix("Take IClock in the constructor; see OrderService for the pattern.");
    }
}

internal sealed class ClosedGenericReturningSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // typeof(Task<int>) is a closed construction — refused; ban the open definition instead.
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.Returning(typeof(Task<int>)).MustHaveSuffix("Async"))
            .Because("Reason.");
    }
}

internal sealed class BlankMemberWhereSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.Where(_ => true, "").MustHaveSuffix("Async"))
            .Because("Reason.");
    }
}

internal sealed class BlankMemberMustSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.Methods.Must(_ => true, "")).Because("Reason.");
    }
}

internal sealed class ValidMemberSubjectSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Selection web = arch.Namespace("MyApp.Web.*");
        arch.Rule("naming/async-suffix")
            .Enforce(web.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
            .Because("Async methods are discovered by suffix.");
    }
}

internal sealed class BlankNamespaceGlobSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Namespace(" ").MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class BlankSuffixSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.MustHaveSuffix(" ")).Because("Reason.");
    }
}

internal sealed class BlankMemberSuffixSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.Methods.WithSuffix(" ").MustHaveSuffix("Async")).Because("Reason.");
    }
}

internal sealed class BlankLayerGlobSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Bad", " ");
    }
}

internal sealed class DeadSubtreeGlobSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Namespace("MyApp.*.Controllers.*").MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class DeadSubtreeLayerSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Bad", "MyApp.*.Svc.*");
    }
}

internal sealed class InteriorWildcardSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Namespace("MyApp.*.Orders").MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class ThreeBadPatternsSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Namespace("MyApp.*.A.*").WithSuffix(" ").MustResideInNamespace("Bad.*.X.*"))
            .Because("Reason.");
    }
}

internal sealed class NonMemberBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // w.Count + 1 is an arithmetic expression, not a member access.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorWidget>(w => w.Count + 1))).Because("Reason.");
    }
}

internal sealed class MethodGroupBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
#pragma warning disable CS8974 // deliberately anchoring a method group (the mistake under test)
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorWidget>(w => w.Reset))).Because("Reason.");
#pragma warning restore CS8974
    }
}

internal sealed class ChainedReceiverSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // w.Inner.Count reaches through a chained access — anchor Count's declaring type directly instead.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorWidget>(w => w.Inner!.Count))).Because("Reason.");
    }
}

internal sealed class StaticInInstanceFormSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Instance form (arch.Member<T>) but the body reads a static member.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<DateTime>(_ => DateTime.Now))).Because("Reason.");
    }
}

internal sealed class InstanceInStaticFormSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Parameterless form (arch.Member(() => ...)) but the body reads an instance member off a static field.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(() => DateTime.MinValue.Ticks))).Because("Reason.");
    }
}

internal sealed class IndexerBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // l[0] resolves to the get_Item accessor (IsSpecialName) — an indexer, outside the member-anchor surface.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<List<int>>(l => l[0]))).Because("Reason.");
    }
}

internal sealed class StaticMethodGroupBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
#pragma warning disable CS8974 // deliberately anchoring a static method group (the mistake under test)
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(() => AnchorStatics.Beep))).Because("Reason.");
#pragma warning restore CS8974
    }
}

internal sealed class UserOpConversionReceiverSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // ((AnchorFahrenheit)c).Value peels through the explicit user-defined conversion — not an identity cast.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorCelsius>(c => ((AnchorFahrenheit)c).Value))).Because("Reason.");
    }
}

internal sealed class CompileTimeConstantBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // DayOfWeek.Monday is a compile-time enum constant — inlined to its value, no member remains.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(() => DayOfWeek.Monday))).Because("Reason.");
    }
}

internal sealed class MultiplePoisonedMembersSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotUse(
                arch.Member<AnchorWidget>(w => w.Count + 1),
                arch.Member<AnchorWidget>(w => w.Inner!.Count)))
            .Because("Reason.");
    }
}

internal sealed class ForeignExpressionMemberSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        Member foreign = other.Member<Task>(t => t.Wait());
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(foreign)).Because("Reason.");
    }
}

internal sealed class ValidExpressionMemberSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotUse(
                arch.Member(() => DateTime.Now),
                arch.Member(() => DateTime.UtcNow)))
            .Because("Reason.");
    }
}

// Verb-position twins of the poison specs above: the poisoned lambda is passed bare to MustNotUse's
// static forms rather than wrapped in arch.Member(...). Each reifies through the identical resolver, so
// it reproduces the identical diagnostic.
internal sealed class VerbNonMemberBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // new object() is an object-creation expression, not a member access.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => new object())).Because("Reason.");
    }
}

internal sealed class VerbStaticMethodGroupBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
#pragma warning disable CS8974 // deliberately anchoring a static method group (the mistake under test)
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => AnchorStatics.Beep)).Because("Reason.");
#pragma warning restore CS8974
    }
}

internal sealed class VerbInstanceInStaticFormSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Static form (() => ...) but the body reads an instance member (.Ticks) off a static field.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => DateTime.MinValue.Ticks)).Because("Reason.");
    }
}

internal sealed class VerbIndexerBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // new List<int>()[0] resolves to the get_Item accessor (IsSpecialName) — checked before receiver classification.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => new List<int>()[0])).Because("Reason.");
    }
}

internal sealed class VerbCompileTimeConstantBodySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // DayOfWeek.Monday is a compile-time enum constant — inlined to its value, no member remains.
        arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => DayOfWeek.Monday)).Because("Reason.");
    }
}

internal sealed class VerbMultiplePoisonedMembersSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Both bind the Func<object?> overload (an enum read is not a statement, so only that overload spans both).
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotUse(() => new object(), () => DayOfWeek.Monday))
            .Because("Reason.");
    }
}

// No verb-position foreign-Arch twin: the resolver's owner comes from subject.Owner by construction, so
// there is no seam to pass a foreign Arch. The ForeignMember check that ForeignExpressionMemberSpec
// exercises (via a Member minted on another Arch) has no static-verb spelling — hence no test here.
internal sealed class ValidVerbMemberSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotUse(() => DateTime.Now, () => DateTime.UtcNow))
            .Because("Reason.");
    }
}

internal sealed class MultipleProblemsSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Bad_Id → MalformedId; no .Because → MissingBecause; dangling scope → DanglingAnchor.
        arch.Rule("Bad_Id").Enforce(arch.Types.MustHavePrefix("I"));
        arch.Scope("other/scope");
    }
}

internal sealed class MemberPoisonBelowRuleSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The arch.Member(...) poison sits two lines below the arch.Rule(...) anchor; the rule also omits
        // .Because, so the one-pass batch carries a rule-anchored error at the rule line and a member
        // -anchored one at the lambda line.
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotUse(
                arch.Member<AnchorWidget>(w => w.Count + 1)));
    }
}

internal sealed class BlankTypeNameMatchingVerbSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The verb's own name glob (ConstraintPatterns MustHaveNameMatchingConstraint arm).
        arch.Rule("area/rule").Enforce(arch.Types.MustHaveNameMatching(" ")).Because("Reason.");
    }
}

internal sealed class BlankTypeNameMatchingAdjectiveSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The subject-side WithNameMatching adjective glob (SelectionPatterns WithNameMatchingAdjective arm).
        arch.Rule("area/rule").Enforce(arch.Types.WithNameMatching(" ").MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class BlankTypePrefixAdjectiveSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The subject-side WithPrefix adjective affix (SelectionPatterns WithPrefixAdjective arm).
        arch.Rule("area/rule").Enforce(arch.Types.WithPrefix(" ").MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class BlankMemberNameMatchingAdjectiveSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The member-subject WithNameMatching adjective glob (MemberAdjectivePatterns arm).
        arch.Rule("area/rule").Enforce(arch.Types.Methods.WithNameMatching(" ").MustBePublic()).Because("Reason.");
    }
}

internal sealed class BlankMemberPrefixAdjectiveSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The member-subject WithPrefix adjective affix (MemberAdjectivePatterns arm).
        arch.Rule("area/rule").Enforce(arch.Types.Methods.WithPrefix(" ").MustBePublic()).Because("Reason.");
    }
}

internal sealed class BlankMemberNameMatchingVerbSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The member verb's own name glob (ConstraintPatterns MemberMustHaveNameMatchingConstraint arm).
        arch.Rule("area/rule").Enforce(arch.Types.Members.MustHaveNameMatching(" ")).Because("Reason.");
    }
}

internal sealed class BlankMemberPrefixVerbSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The member verb's own affix (ConstraintPatterns MemberMustHavePrefixConstraint arm).
        arch.Rule("area/rule").Enforce(arch.Types.Members.MustHavePrefix(" ")).Because("Reason.");
    }
}

internal sealed class ForeignConstructTargetSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The construct target is minted on a different Arch — caught by the shared Operands foreign walk.
        var other = new Arch();
        Selection foreignTarget = other.Namespace("MyApp.Services.*");
        arch.Rule("area/rule").Enforce(arch.Types.MustNotConstruct(foreignTarget)).Because("Reason.");
    }
}

internal sealed class UndefinedLifetimeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // (Lifetime)7 names no defined lifetime — item 19 refuses it at spec build (all-at-once).
        arch.Rule("di/lifetimes")
            .Enforce(arch.Registered((Lifetime)7).MustNotReference(typeof(DateTime)))
            .Because("Reason.");
    }
}

internal sealed class ClosedGenericAcceptParameterSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // typeof(IProgress<int>) is a closed construction — refused; ban the open definition instead.
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.MustAcceptParameter(typeof(IProgress<int>)))
            .Because("Reason.");
    }
}

internal sealed class AcceptParameterAllAtOnceSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Closed-generic parameter anchor AND no .Because → two codes in one pass.
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.MustAcceptParameter(typeof(IProgress<int>)));
    }
}

internal sealed class ValidAcceptParameterSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/nongeneric")
            .Enforce(arch.Types.Methods.MustAcceptParameter(typeof(CancellationToken)))
            .Because("Reason.");
        arch.Rule("area/opengeneric")
            .Enforce(arch.Types.Methods.MustAcceptParameter(typeof(IProgress<>)))
            .Because("Reason.");
    }
}

internal sealed class NonInterfaceImplementAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // System.Exception is a class, not an interface — refused; use MustNotDeriveFrom for a base class.
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotImplement(typeof(Exception)))
            .Because("Reason.");
    }
}

internal sealed class InterfaceDeriveFromAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // System.IDisposable is an interface — refused on the positive DeriveFrom; use MustImplement.
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustDeriveFrom(typeof(IDisposable)))
            .Because("Reason.");
    }
}

internal sealed class NonAttributeAttributedAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // typeof(Attribute) itself does not derive from System.Attribute — refused (the ratified edge case).
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotBeAttributedWith(typeof(Attribute)))
            .Because("Reason.");
    }
}

internal sealed class HierarchyAllAtOnceSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // First anchor (IDisposable) is a valid interface; the second (Exception) is not — and no .Because.
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotImplement(typeof(IDisposable), typeof(Exception)));
    }
}

internal sealed class ValidHierarchyAnchorsSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/implement").Enforce(arch.Types.MustNotImplement(typeof(IDisposable))).Because("Reason.");
        arch.Rule("area/derive").Enforce(arch.Types.MustNotDeriveFrom(typeof(Exception))).Because("Reason.");
        arch.Rule("area/attributed").Enforce(arch.Types.MustNotBeAttributedWith(typeof(SerializableAttribute))).Because("Reason.");
        arch.Rule("area/implement-pos").Enforce(arch.Types.MustImplement(typeof(IDisposable))).Because("Reason.");
        arch.Rule("area/derive-pos").Enforce(arch.Types.MustDeriveFrom(typeof(Exception))).Because("Reason.");
        arch.Rule("area/attributed-pos").Enforce(arch.Types.MustBeAttributedWith(typeof(SerializableAttribute))).Because("Reason.");
    }
}

internal sealed class UnionBlankWhereSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.AnyOf(arch.Project("A"), arch.Project("B"))
                .Where(t => t.Name.Length > 0, "  ")
                .MustHavePrefix("I"))
            .Because("Reason.");
    }
}

internal sealed class UnionBlankPatternSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.AnyOf(arch.Project("A"), arch.Project("B")).InNamespace("").MustHavePrefix("I"))
            .Because("Reason.");
    }
}

internal sealed class UnionForeignExceptPayloadSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        arch.Rule("area/rule")
            .Enforce(arch.AnyOf(arch.Project("A"), arch.Project("B")).Except(other.Types).MustHavePrefix("I"))
            .Because("Reason.");
    }
}

internal sealed class UnionForeignOperandSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        arch.Rule("area/rule")
            .Enforce(arch.AnyOf(arch.Project("A"), other.Project("B")).MustHavePrefix("I"))
            .Because("Reason.");
    }
}

internal sealed class BlankAttributeAdjectiveSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.AttributedWith("   ").MustBeSealed())
            .Because("Reason.");
    }
}

internal sealed class BlankAttributeVerbSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustBeAttributedWith(""))
            .Because("Reason.");
    }
}

internal sealed class BlankAttributeNegativeVerbSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotBeAttributedWith("N.MarkAttribute", "  "))
            .Because("Reason.");
    }
}

internal sealed class BlankAttributeAllAtOnceSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // A blank adjective anchor AND a blank verb anchor AND no .Because → three errors in one pass.
        arch.Rule("area/rule")
            .Enforce(arch.Types.AttributedWith("").MustBeAttributedWith(" "));
    }
}

internal sealed class NonsenseStringAttributeAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/not-an-attribute").Enforce(arch.Types.MustBeAttributedWith("System.Object")).Because("Reason.");
        arch.Rule("area/dotless").Enforce(arch.Types.AttributedWith("Nonsense").MustBeSealed()).Because("Reason.");
        arch.Rule("area/no-suffix").Enforce(arch.Types.MustNotBeAttributedWith("N.Mark")).Because("Reason.");
        arch.Rule("area/attribute-itself").Enforce(arch.Types.MustNotBeAttributedWith("System.Attribute")).Because("Reason.");
    }
}

internal sealed class MemberNonAttributeAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.MustBeAttributedWith(typeof(Exception)))
            .Because("Reason.");
    }
}

internal sealed class MemberAttributeItselfAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.MustNotBeAttributedWith(typeof(Attribute)))
            .Because("Reason.");
    }
}

internal sealed class ValidMemberAttributeAnchorsSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("member/attributed-pos").Enforce(arch.Types.Methods.MustBeAttributedWith(typeof(SerializableAttribute))).Because("Reason.");
        arch.Rule("member/attributed-neg").Enforce(arch.Types.Methods.MustNotBeAttributedWith(typeof(SerializableAttribute))).Because("Reason.");
        arch.Rule("member/adjective-uncategorized").Enforce(arch.Types.Methods.AttributedWith(typeof(Exception)).MustBePublic()).Because("Reason.");
    }
}

internal sealed class BlankMemberAttributeNameSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("member/adjective").Enforce(arch.Types.Methods.AttributedWith("   ").MustBePublic()).Because("Reason.");
        arch.Rule("member/positive").Enforce(arch.Types.Methods.MustBeAttributedWith("")).Because("Reason.");
        arch.Rule("member/negative").Enforce(arch.Types.Methods.MustNotBeAttributedWith("N.MarkAttribute", " ")).Because("Reason.");
    }
}

internal sealed class BlankHierarchyNameSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("hierarchy/implementing").Enforce(arch.Types.Implementing("   ").MustBeSealed()).Because("Reason.");
        arch.Rule("hierarchy/must-implement").Enforce(arch.Types.MustImplement("")).Because("Reason.");
        arch.Rule("hierarchy/must-not-implement").Enforce(arch.Types.MustNotImplement("N.IThing", " ")).Because("Reason.");
        arch.Rule("hierarchy/derived-from").Enforce(arch.Types.DerivedFrom("   ").MustBeSealed()).Because("Reason.");
        arch.Rule("hierarchy/must-derive-from").Enforce(arch.Types.MustDeriveFrom("")).Because("Reason.");
        arch.Rule("hierarchy/must-not-derive-from").Enforce(arch.Types.MustNotDeriveFrom("N.Base", " ")).Because("Reason.");
    }
}

internal sealed class NonsenseStringHierarchyAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/class-as-interface").Enforce(arch.Types.MustImplement("System.Exception")).Because("Reason.");
        arch.Rule("area/interface-as-base").Enforce(arch.Types.MustNotDeriveFrom("System.IDisposable")).Because("Reason.");
        arch.Rule("area/dotless").Enforce(arch.Types.Implementing("Nonsense").MustBeSealed()).Because("Reason.");
        arch.Rule("area/constructed").Enforce(arch.Types.MustNotImplement("N.IHandler<System.Int32>")).Because("Reason.");
    }
}

internal sealed class BlankProjectNameSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("project/noun-subject").Enforce(arch.Project("").MustBeSealed()).Because("Reason.");
        arch.Rule("project/noun-operand").Enforce(arch.Types.MustNotReference(arch.Project("   "))).Because("Reason.");
        arch.Rule("project/verb").Enforce(arch.Types.MustResideInProject(" ")).Because("Reason.");
    }
}

internal sealed class ValidProjectNamesSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("project/named-noun").Enforce(arch.Project("Some.Name").MustBeSealed()).Because("Reason.");
        arch.Rule("project/named-verb").Enforce(arch.Types.MustResideInProject("Another.Name")).Because("Reason.");
    }
}

internal sealed class ForeignProjectSelectionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        arch.Rule("area/rule").Enforce(other.Projects.Named("A").MustNotBePackable()).Because("Reason.");
    }
}

internal sealed class ForeignProjectExceptPayloadSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        arch.Rule("area/rule")
            .Enforce(arch.Projects.Matching("*").Except(other.Projects.Named("A")).MustNotBePackable())
            .Because("Reason.");
    }
}

internal sealed class BlankProjectPatternSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("project/blank-name").Enforce(arch.Projects.Named("   ").MustNotBePackable()).Because("Reason.");
        arch.Rule("project/blank-glob").Enforce(arch.Projects.Matching("A", "").MustNotBePackable()).Because("Reason.");
        arch.Rule("project/blank-in-except")
            .Enforce(arch.Projects.Matching("*").Except(arch.Projects.Named(" ")).MustNotBePackable())
            .Because("Reason.");
    }
}

internal sealed class BlankTargetFrameworkSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Projects.Named("A").MustOnlyTarget("net8.0", "  ")).Because("Reason.");
    }
}

internal sealed class BlankProjectWhereSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Projects.Where(project => project.IsPackable == true, "  ").MustNotBePackable())
            .Because("Reason.");
    }
}

internal sealed class BlankProjectMustSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Projects.Named("A").Must(project => project.IsPackable == true, ""))
            .Because("Reason.");
    }
}

internal sealed class BlankCounterpartTemplateSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustHaveExactlyOneCounterpart(among: arch.Types, named: " "))
            .Because("Reason.");
    }
}

internal sealed class PlaceholderFreeCounterpartTemplateSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustHaveExactlyOneCounterpart(among: arch.Types, named: "IService"))
            .Because("Reason.");
    }
}

internal sealed class BlankTypeNamedAdjectiveSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule").Enforce(arch.Types.Named("A", " ").MustHavePrefix("I")).Because("Reason.");
    }
}

internal sealed class ForeignBoundarySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The sanctioned surface is spec-authored selection like any other, so a boundary minted on a
        // different Arch is the same error the subject position reports.
        var other = new Arch();
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(other.Types.Named("BillingFacade"))
            .Dragons("Dragons.")
            .Because("Quarantined.");
    }
}

internal sealed class BlankBoundaryPatternSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(arch.Namespace(" "))
            .Dragons("Dragons.")
            .Because("Quarantined.");
    }
}

internal sealed class BlankLayerPurposeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Core", "MyApp.Core.*").Purpose(" ");
    }
}

internal sealed class MultiLineLayerPurposeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Core", "MyApp.Core.*").Purpose("line one\nline two");
    }
}

internal sealed class RepeatedLayerPurposeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The two-statement form: the trailer is called on the stored Layer, not chained.
        Layer core = arch.Layer("Core", "MyApp.Core.*").Purpose("First.");
        core.Purpose("Second.");
    }
}

internal sealed class DoubleCautionScopeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // A stored IScopeBuilder re-called with .Caution silently overwrites the scoped selection (§8 item 17).
        IScopeBuilder scope = arch.Scope("shared/utilities");
        scope.Caution(arch.Namespace("MyApp.Shared.*"));
        scope.Caution(arch.Namespace("MyApp.Shared.Other.*")).Dragons("Dragons.").Because("Cautioned.");
    }
}

internal sealed class MixedPostureScopeSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // Two different posture verbs overwrite each other exactly as two of the same one do: it is the
        // count that item 17 reports, not which verbs were called.
        IScopeBuilder scope = arch.Scope("shared/utilities");
        scope.Quarantine(arch.Namespace("MyApp.Shared.*"));
        scope.Caution(arch.Namespace("MyApp.Shared.*")).Dragons("Dragons.").Because("Cautioned.");
    }
}

internal sealed class MissingDragonsCautionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("shared/utilities")
            .Caution(arch.Namespace("MyApp.Shared.*"))
            .Because("Cautioned.");
    }
}

internal sealed class ValidCautionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("shared/utilities")
            .Caution(arch.Namespace("MyApp.Shared.*"))
            .Dragons("Every helper here is called from everywhere; the argument order is load-bearing.")
            .Because("The utilities are public API for the whole solution.");
    }
}

internal sealed class ForeignLayerDefinitionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // A definition is use-independent, so a foreign one is caught at the layer whether or not any rule
        // ever names the layer — and reported spec-wide, named by layer (§8 item 10).
        var other = new Arch();
        arch.Layer("Foreign", other.Project("MyApp.Core"));
    }
}

internal sealed class BlankProjectNameLayerDefinitionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Bad", arch.Project(" "));
    }
}

internal sealed class UndefinedLifetimeLayerDefinitionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Wiring", arch.Registered((Lifetime)7));
    }
}

internal sealed class DeadSubtreeLayerDefinitionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The definition's own Except payload: every walk a rule's selections take reaches a definition's
        // nesting too.
        arch.Layer("Bad", arch.Namespace("MyApp.Core.*").Except(arch.Namespace("MyApp.*.Svc.*")));
    }
}

// The family specs (§8 items 27–28). Appended at the end of the file on purpose: dozens of expected
// messages in SpecValidationTests quote this file's anchors as literal line numbers, so an insertion
// anywhere above would move every one of them.

internal sealed class FamilyAsOperandSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Rule("area/rule")
            .Enforce(arch.Types.MustNotReference(arch.Each(dispatch, tracking)))
            .Because("A partition means nothing at the far end of an edge.");
    }
}

internal sealed class FamilyAsExceptPayloadSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Rule("area/rule")
            .Enforce(arch.Types.Except(arch.Each(dispatch, tracking)).MustHaveSuffix("Service"))
            .Because("An Except payload is a set to subtract, not a partition.");
    }
}

internal sealed class FamilyAsUnionOperandSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Rule("area/rule")
            .Enforce(arch.AnyOf(arch.Each(dispatch, tracking), arch.Namespace("Ops.Client.*"))
                .MustHaveSuffix("Service"))
            .Because("A union flattens its operands into one set, which loses the partition.");
    }
}

internal sealed class FamilyAsMembershipSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Rule("area/rule")
            .Enforce(arch.Namespace("Ops.*").MustBelongTo(arch.Each(dispatch, tracking)))
            .Because("A membership operand is read as one set, like every other operand.");
    }
}

internal sealed class FamilyAsCounterpartAmongSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Rule("area/rule")
            .Enforce(arch.Types.WithSuffix("Service")
                .MustHaveExactlyOneCounterpart(
                    among: arch.Each(dispatch, tracking),
                    named: "I{Name}"))
            .Because("The among: operand is where a counterpart may stand, which is one set.");
    }
}

internal sealed class FamilyAsScopedSelectionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Scope("legacy/modules")
            .Quarantine(arch.Each(dispatch, tracking))
            .Dragons("The rounding is load-bearing.")
            .Because("A quarantine fences one region, not a partition of several.");
    }
}

internal sealed class FamilyAsBoundarySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Scope("legacy/modules")
            .Quarantine(arch.Namespace("Ops.Legacy.*"))
            .BoundaryOnlyVia(arch.Each(dispatch, tracking))
            .Dragons("The rounding is load-bearing.")
            .Because("A sanctioned surface is a set of types the fence lets through.");
    }
}

internal sealed class FamilyAsLayerDefinitionSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Layer("Modules", arch.Each(dispatch, tracking));
    }
}

internal sealed class EachOtherWithoutFamilySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Namespace("Ops.*").MustNotReferenceEachOther())
            .Because("Over a plain selection there are no others to name.");
    }
}

internal sealed class ForeignFamilyCellSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // A family's layer cells ride the same walks a union's operands do, so a cell minted elsewhere is
        // found by the ordinary foreign-Arch check rather than by an arm of its own (§8 item 10).
        var other = new Arch();
        arch.Rule("area/rule")
            .Enforce(arch.Each(other.Layer("Dispatch", "Ops.Dispatch.*")).MustNotReferenceEachOther())
            .Because("A spec assembled from two Arch instances is one mistake.");
    }
}

internal sealed class BlankFamilyGlobSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Ops.Dispatch.*");
        Layer tracking = arch.Layer("Tracking", "Ops.Tracking.*");
        arch.Rule("area/rule")
            .Enforce(arch.Each(dispatch, tracking).InNamespace("").MustNotReferenceEachOther())
            .Because("A family carries adjectives of its own, and they are checked like any other.");
    }
}

internal sealed class BlankProjectFamilyGlobSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Each(arch.Projects.Matching("")).MustNotReferenceEachOther())
            .Because("The project selection inside a family is reached by the project-stratum walks.");
    }
}

internal sealed class ForeignProjectFamilySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        var other = new Arch();
        arch.Rule("area/rule")
            .Enforce(arch.Each(other.Projects.Matching("Nop.Plugin.*")).MustNotReferenceEachOther())
            .Because("A project selection carries its own Arch, family or not.");
    }
}

internal sealed class CircularReferencesOnPlainSubjectSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Namespace("Ops.*").MustNotHaveCircularReferences())
            .Because("Over a plain selection there are no layers to reference each other.");
    }
}

internal sealed class CircularReferencesOnProjectFamilySpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Each(arch.Projects.Matching("Ops.*")).MustNotHaveCircularReferences())
            .Because("Projects cannot have circular references, so over a family of projects the law would hold by construction.");
    }
}

internal sealed class BlankReturnTypeNameSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.Returning("   ").MustBeStatic())
            .Because("Reason.");
    }
}

internal sealed class BlankParameterTypeNameSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("area/rule")
            .Enforce(arch.Types.Methods.MustAcceptParameter(""))
            .Because("Reason.");
    }
}

internal sealed class NonsenseStringMemberTypeAnchorSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("member/returning")
            .Enforce(arch.Types.Methods.Returning("System.Threading.Tasks.Task<System.Int32>").MustBeStatic())
            .Because("Reason.");
        arch.Rule("member/parameter")
            .Enforce(arch.Types.Methods.MustAcceptParameter("System.IProgress<System.Int32>"))
            .Because("Reason.");
    }
}
