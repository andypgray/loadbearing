using Shouldly;
using Xunit;
using Zphil.LoadBearing.Validation;
using Code = Zphil.LoadBearing.Validation.SpecValidationErrorCode;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The spec-build validation catalog (GRAMMAR §8): one failing spec per code, pinned by code
///     plus rule ID with one representative message, and one all-errors spec proving every problem
///     is reported in a single pass.
/// </summary>
public class SpecValidationTests
{
    private static SpecValidationException BuildExpectingFailure(params IArchitectureSpec[] specs)
    {
        return Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(specs));
    }

    [Fact]
    public void DuplicateId_AcrossTwoSpecClasses_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DuplicateIdSpecA(), new DuplicateIdSpecB());

        ex.Errors.ShouldContain(e => e.Code == Code.DuplicateId && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.DuplicateId).Message.ShouldBe("SpecValidationTests.cs:549: Duplicate rule ID 'area/rule'.");
    }

    [Fact]
    public void IdExtendsScope_ReservedNamespace_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new IdExtendsScopeSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.IdExtendsScope && e.RuleId == "legacy/billing/foo");
        ex.Errors.First(e => e.Code == Code.IdExtendsScope).Message.ShouldContain("extends scope 'legacy/billing'");
    }

    [Fact]
    public void DanglingAnchor_RuleWithNoPosture_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DanglingRuleSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.DanglingAnchor && e.RuleId == "area/dangling");
    }

    [Fact]
    public void MissingBecause_OnRule_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingBecauseRuleSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MissingBecause && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MissingBecause).Message.ShouldBe("SpecValidationTests.cs:583: 'area/rule' is missing a required .Because(...).");
    }

    [Fact]
    public void MissingBecause_OnFrozenScope_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingBecauseScopeSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MissingBecause && e.RuleId == "legacy/billing");
    }

    [Fact]
    public void MissingDragons_OnFrozenScope_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingDragonsSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MissingDragons && e.RuleId == "legacy/billing");
    }

    [Fact]
    public void BlankProse_BlankEscapeHatchDescription_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankDescriptionSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void MultiLineProse_NewlineInBecause_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultiLineBecauseSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MultiLineProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void RepeatedTrailer_BecauseTwice_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new RepeatedBecauseSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.RepeatedTrailer && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.RepeatedTrailer).Message.ShouldBe("SpecValidationTests.cs:623: Repeated trailer 'Because' on 'area/rule'.");
    }

    [Fact]
    public void MalformedId_NonConventionId_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MalformedIdSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MalformedId && e.RuleId == "Bad_Id");
    }

    [Fact]
    public void RepeatedPosture_PostureVerbTwiceViaStoredRuleBuilder_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DoublePostureRuleSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.RepeatedPosture && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.RepeatedPosture).Message
            .ShouldBe("SpecValidationTests.cs:642: Rule 'area/rule' has more than one posture; call .Enforce(...) or .Migrate(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_FreezeTwiceViaStoredScopeBuilder_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DoubleFreezeScopeSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.RepeatedPosture && e.RuleId == "legacy/billing");
        ex.Errors.First(e => e.Code == Code.RepeatedPosture).Message
            .ShouldBe("SpecValidationTests.cs:654: Scope 'legacy/billing' has more than one posture; call .Freeze(...) exactly once.");
    }

    [Fact]
    public void EmptyBoundary_BoundaryOnlyViaWithNoTypes_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new EmptyBoundarySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.EmptyBoundary && e.RuleId == "legacy/billing");
        ex.Errors.First(e => e.Code == Code.EmptyBoundary).Message.ShouldContain("omit the call for a hermetic freeze");
    }

    [Fact]
    public void DuplicateLayerName_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new DuplicateLayerSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.DuplicateLayerName && e.RuleId == null);
        ex.Errors.First(e => e.Code == Code.DuplicateLayerName).Message.ShouldBe("Duplicate layer name 'Dup'.");
    }

    [Fact]
    public void ForeignSelection_SelectionFromAnotherArch_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ForeignSelectionSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.ForeignSelection && e.RuleId == "area/rule");
    }

    [Fact]
    public void AllErrors_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultipleProblemsSpec());

        ex.Errors.Select(e => e.Code).Distinct().Count().ShouldBeGreaterThanOrEqualTo(3);
        ex.Errors.ShouldContain(e => e.Code == Code.MalformedId);
        ex.Errors.ShouldContain(e => e.Code == Code.MissingBecause);
        ex.Errors.ShouldContain(e => e.Code == Code.DanglingAnchor);
    }

    [Fact]
    public void BlankMemberName_OnMustNotUse_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberNameSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankMemberName && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankMemberName).Message
            .ShouldBe("SpecValidationTests.cs:692: Blank member name on a member of 'System.DateTime' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_TypoName_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new TypoMemberSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberNotDeclared && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberNotDeclared).Message
            .ShouldBe("SpecValidationTests.cs:700: 'System.DateTime' does not declare a member named 'Nows' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_MemberOnBaseType_NamesBaseAndTypeof()
    {
        SpecValidationException ex = BuildExpectingFailure(new BaseTypeMemberSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberNotDeclared && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberNotDeclared).Message
            .ShouldBe("SpecValidationTests.cs:709: 'System.Threading.Tasks.Task<TResult>' does not declare 'Wait'; it is declared on base type " +
                      "'System.Threading.Tasks.Task' — use typeof(Task) (used by 'area/rule').");
    }

    [Fact]
    public void ForeignMember_MemberFromAnotherArch_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ForeignMemberSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.ForeignMember && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.ForeignMember).Message
            .ShouldBe("SpecValidationTests.cs:718: A member used by 'area/rule' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void ValidMemberUse_MustNotUseSpec_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberUseSpec()));
    }

    [Fact]
    public void MemberReturningClosedGeneric_ClosedAnchor_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ClosedGenericReturningSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberReturningClosedGeneric && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberReturningClosedGeneric).Message
            .ShouldBe("SpecValidationTests.cs:743: 'System.Threading.Tasks.Task<System.Int32>' is a closed generic; .Returning matches definition-level — " +
                      "use typeof(Task<>) (used by 'area/rule').");
    }

    [Fact]
    public void BlankProse_BlankMemberWhereDescription_IsReported()
    {
        // The extended prose walk reaches a member Where description (GRAMMAR §8 item 5, §4.6).
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberWhereSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankProse_BlankMemberMustDescription_IsReported()
    {
        // The extended prose walk reaches a member Must description (GRAMMAR §8 item 5, §4.6).
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberMustSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void ValidMemberSubject_AsyncSuffixSpec_BuildsWithoutError()
    {
        // Non-generic + open-generic Returning anchors are both accepted (only closed generics fail).
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberSubjectSpec()));
    }

    [Fact]
    public void BlankPattern_BlankNamespaceGlob_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankNamespaceGlobSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankPattern).Message.ShouldBe("SpecValidationTests.cs:782: Blank namespace pattern on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankSuffix_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankSuffixSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankPattern).Message.ShouldBe("SpecValidationTests.cs:790: Blank suffix on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankMemberSubjectAffix_IsReported()
    {
        // The member-subject adjective walk (parallel to the member prose/Returning walks) reaches a
        // blank member .WithSuffix (GRAMMAR §8 item 15, §4.6).
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberSuffixSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankPattern).Message.ShouldBe("SpecValidationTests.cs:798: Blank member suffix on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankLayerGlob_IsReportedSpecWide()
    {
        // The layer flavor of §8 item 15: like the dead-subtree layer case below, layer globs are
        // validated at their declaration (spec-wide, null rule ID, named by layer), used or not.
        SpecValidationException ex = BuildExpectingFailure(new BlankLayerGlobSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == null);
        ex.Errors.First(e => e.Code == Code.BlankPattern).Message.ShouldBe("Blank namespace pattern on layer 'Bad'.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_WildcardInSubtreePrefix_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DeadSubtreeGlobSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.UnanchoredSubtreePattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.UnanchoredSubtreePattern).Message
            .ShouldBe("SpecValidationTests.cs:814: The namespace pattern 'MyApp.*.Controllers.*' on 'area/rule' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_OnLayerGlob_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new DeadSubtreeLayerSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.UnanchoredSubtreePattern && e.RuleId == null);
        ex.Errors.First(e => e.Code == Code.UnanchoredSubtreePattern).Message
            .ShouldBe("The namespace pattern 'MyApp.*.Svc.*' on layer 'Bad' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void NamespacePattern_InteriorWildcardWithoutSubtree_BuildsWithoutError()
    {
        // MyApp.*.Orders is legitimate single-segment matching (GRAMMAR §4.2), not a dead subtree pattern.
        Should.NotThrow(() => ArchModelBuilder.Build(new InteriorWildcardSpec()));
    }

    [Fact]
    public void AllErrors_ThreeBadPatterns_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new ThreeBadPatternsSpec());

        // Two dead subtree globs (the subject noun and the verb) plus one blank affix, reported together.
        ex.Errors.Count(e => e.Code == Code.UnanchoredSubtreePattern).ShouldBe(2);
        ex.Errors.Count(e => e.Code == Code.BlankPattern).ShouldBe(1);
    }

    [Fact]
    public void MemberExpression_NonMemberBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new NonMemberBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:849: A member anchor lambda must be a single property, field, or method access " +
                      "(x => x.Member or () => Type.Member); this lambda body is neither (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_MethodGroupBody_IsReportedWithInvocationGuidance()
    {
        // A method-group anchor `w => w.Reset` compiles on C# 14 (converting the method group to object,
        // warning CS8974 — suppressed in the spec) and lowers to a CreateDelegate tree the resolver detects;
        // on C# <= 13 it does not compile at all. Steers to the invocation form.
        SpecValidationException ex = BuildExpectingFailure(new MethodGroupBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:858: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_ChainedReceiver_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ChainedReceiverSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:868: A member anchor lambda must reach its member directly on the lambda parameter (an interface " +
                      "cast or as-cast is allowed; a chained access like x => x.A.B, a captured local or field, or a " +
                      "user-defined conversion is not); anchor the declaring type you mean directly (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_StaticMemberInInstanceForm_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new StaticInInstanceFormSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:877: A typed member anchor arch.Member<T>(x => ...) accesses a static member; anchor statics " +
                      "with the parameterless overload arch.Member(() => Type.Member) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_InstanceMemberInStaticForm_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new InstanceInStaticFormSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:886: A parameterless member anchor arch.Member(() => ...) must access a static member " +
                      "directly; anchor an instance member with the typed overload arch.Member<T>(x => x.Member) " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_IndexerBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new IndexerBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:895: A member anchor lambda resolves to an indexer accessor (get_Item), which is outside the " +
                      "member-anchor surface (GRAMMAR §4.5); anchor a named property, field, or method " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_StaticMethodGroupBody_IsReported()
    {
        // A static method-group anchor `() => AnchorStatics.Beep` compiles on C# 14 (method group to object,
        // CS8974 — suppressed in the spec) and lowers to a CreateDelegate tree carrying the MethodInfo as a
        // compiler-emitted constant. Doubles as the T1 static-lowering regression oracle; steers to the
        // invocation form like the instance case.
        SpecValidationException ex = BuildExpectingFailure(new StaticMethodGroupBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:904: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_UserDefinedConversionReceiver_IsReported()
    {
        // ((AnchorFahrenheit)c).Value reaches its member through a user-defined conversion — following it
        // would silently anchor the post-conversion type, so it is reported (identity casts are the only
        // receiver peel).
        SpecValidationException ex = BuildExpectingFailure(new UserOpConversionReceiverSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:914: A member anchor lambda must reach its member directly on the lambda parameter (an interface " +
                      "cast or as-cast is allowed; a chained access like x => x.A.B, a captured local or field, or a " +
                      "user-defined conversion is not); anchor the declaring type you mean directly (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_CompileTimeConstantBody_IsReported()
    {
        // () => DayOfWeek.Monday inlines the enum member to its value (Convert(Constant(...), object)), so
        // Unwrap peels to a ConstantExpression with no member left to anchor.
        SpecValidationException ex = BuildExpectingFailure(new CompileTimeConstantBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:923: A member anchor lambda body is a compile-time constant (a const field, an enum member, or a " +
                      "literal) that the compiler inlines to its value, so no member remains to anchor; name a const " +
                      "or enum member with the typeof form arch.Member(typeof(T), nameof(T.M)) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpressions_MultiplePoisoned_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultiplePoisonedMembersSpec());

        // Two poisoned member anchors on one rule → two errors in one pass (the §8 all-at-once contract).
        ex.Errors.Count(e => e.Code == Code.MemberExpressionUnresolvable).ShouldBe(2);
    }

    [Fact]
    public void ForeignMember_ExpressionMintedFromAnotherArch_IsReported()
    {
        // An expression-minted member is Owner-stamped like the typeof form, so the foreign-Arch check
        // (which precedes the poison short-circuit) catches it (GRAMMAR §8 item 13).
        SpecValidationException ex = BuildExpectingFailure(new ForeignExpressionMemberSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.ForeignMember && e.RuleId == "area/rule");
    }

    [Fact]
    public void ValidExpressionMemberUse_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidExpressionMemberSpec()));
    }

    // Verb-position poison parity (Phase 16): the same unresolvable-anchor diagnostics the anchor-position
    // twins pin (above) fire when the poisoned lambda is passed bare to MustNotUse's static forms, because
    // the verb desugars each target through the identical MemberExpressionResolver. Five of the seven poison
    // classes are reachable from the static forms; the two instance-form steers have no static-verb spelling.
    // Message strings are copied verbatim from the anchor-position twins — the pinned strings are the spec.
    [Fact]
    public void MemberExpression_VerbNonMemberBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbNonMemberBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:969: A member anchor lambda must be a single property, field, or method access " +
                      "(x => x.Member or () => Type.Member); this lambda body is neither (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbStaticMethodGroupBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbStaticMethodGroupBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:978: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbInstanceMemberInStaticForm_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbInstanceInStaticFormSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:988: A parameterless member anchor arch.Member(() => ...) must access a static member " +
                      "directly; anchor an instance member with the typed overload arch.Member<T>(x => x.Member) " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbIndexerBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbIndexerBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:997: A member anchor lambda resolves to an indexer accessor (get_Item), which is outside the " +
                      "member-anchor surface (GRAMMAR §4.5); anchor a named property, field, or method " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbCompileTimeConstantBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbCompileTimeConstantBodySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable).Message
            .ShouldBe("SpecValidationTests.cs:1006: A member anchor lambda body is a compile-time constant (a const field, an enum member, or a " +
                      "literal) that the compiler inlines to its value, so no member remains to anchor; name a const " +
                      "or enum member with the typeof form arch.Member(typeof(T), nameof(T.M)) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpressions_VerbMultiplePoisoned_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbMultiplePoisonedMembersSpec());

        // Two poisoned verb-position anchors on one rule → two errors in one pass (the §8 all-at-once contract).
        ex.Errors.Count(e => e.Code == Code.MemberExpressionUnresolvable).ShouldBe(2);
    }

    [Fact]
    public void ValidVerbMemberUse_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidVerbMemberSpec()));
    }

    private sealed class DuplicateIdSpecA : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("A.");
        }
    }

    private sealed class DuplicateIdSpecB : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("B.");
        }
    }

    private sealed class IdExtendsScopeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Dragons.").Because("Frozen.");
            arch.Rule("legacy/billing/foo").Enforce(arch.Types.MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class DanglingRuleSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/dangling");
        }
    }

    private sealed class MissingBecauseRuleSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I"));
        }
    }

    private sealed class MissingBecauseScopeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*")).Dragons("Dragons.");
        }
    }

    private sealed class MissingDragonsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*")).Because("Frozen.");
        }
    }

    private sealed class BlankDescriptionSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.Must(_ => true, "")).Because("Reason.");
        }
    }

    private sealed class MultiLineBecauseSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("line one\nline two");
        }
    }

    private sealed class RepeatedBecauseSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("First.").Because("Second.");
        }
    }

    private sealed class MalformedIdSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("Bad_Id").Enforce(arch.Types.MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class DoublePostureRuleSpec : IArchitectureSpec
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

    private sealed class DoubleFreezeScopeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // A stored IScopeBuilder re-called with .Freeze silently overwrites the frozen selection (§8 item 17).
            IScopeBuilder scope = arch.Scope("legacy/billing");
            scope.Freeze(arch.Namespace("MyApp.Legacy.Billing.*"));
            scope.Freeze(arch.Namespace("MyApp.Legacy.Other.*")).Dragons("Dragons.").Because("Frozen.");
        }
    }

    private sealed class EmptyBoundarySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .BoundaryOnlyVia().Dragons("Dragons.").Because("Frozen.");
        }
    }

    private sealed class DuplicateLayerSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Layer("Dup", "MyApp.A.*");
            arch.Layer("Dup", "MyApp.B.*");
        }
    }

    private sealed class ForeignSelectionSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            var other = new Arch();
            Selection foreign = other.Types.OfKind(TypeKind.Interface);
            arch.Rule("area/rule").Enforce(foreign.MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class BlankMemberNameSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), " "))).Because("Reason.");
        }
    }

    private sealed class TypoMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), "Nows"))).Because("Reason.");
        }
    }

    private sealed class BaseTypeMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // Wait lives on the non-generic base Task, not on Task<TResult> — the base-type guidance case.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(Task<>), "Wait"))).Because("Reason.");
        }
    }

    private sealed class ForeignMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            var other = new Arch();
            Member foreign = other.Member(typeof(DateTime), nameof(DateTime.Now));
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(foreign)).Because("Reason.");
        }
    }

    private sealed class ValidMemberUseSpec : IArchitectureSpec
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

    private sealed class ClosedGenericReturningSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // typeof(Task<int>) is a closed construction — refused; ban the open definition instead.
            arch.Rule("area/rule")
                .Enforce(arch.Types.Methods.Returning(typeof(Task<int>)).MustHaveSuffix("Async"))
                .Because("Reason.");
        }
    }

    private sealed class BlankMemberWhereSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule")
                .Enforce(arch.Types.Methods.Where(_ => true, "").MustHaveSuffix("Async"))
                .Because("Reason.");
        }
    }

    private sealed class BlankMemberMustSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.Methods.Must(_ => true, "")).Because("Reason.");
        }
    }

    private sealed class ValidMemberSubjectSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Selection web = arch.Namespace("MyApp.Web.*");
            arch.Rule("naming/async-suffix")
                .Enforce(web.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
                .Because("Async methods are discovered by suffix.");
        }
    }

    private sealed class BlankNamespaceGlobSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Namespace(" ").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class BlankSuffixSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHaveSuffix(" ")).Because("Reason.");
        }
    }

    private sealed class BlankMemberSuffixSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.Methods.WithSuffix(" ").MustHaveSuffix("Async")).Because("Reason.");
        }
    }

    private sealed class BlankLayerGlobSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Layer("Bad", " ");
        }
    }

    private sealed class DeadSubtreeGlobSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Namespace("MyApp.*.Controllers.*").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class DeadSubtreeLayerSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Layer("Bad", "MyApp.*.Svc.*");
        }
    }

    private sealed class InteriorWildcardSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Namespace("MyApp.*.Orders").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class ThreeBadPatternsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule")
                .Enforce(arch.Namespace("MyApp.*.A.*").WithSuffix(" ").MustResideInNamespace("Bad.*.X.*"))
                .Because("Reason.");
        }
    }

    private sealed class NonMemberBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // w.Count + 1 is an arithmetic expression, not a member access.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorWidget>(w => w.Count + 1))).Because("Reason.");
        }
    }

    private sealed class MethodGroupBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
#pragma warning disable CS8974 // deliberately anchoring a method group (the mistake under test)
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorWidget>(w => w.Reset))).Because("Reason.");
#pragma warning restore CS8974
        }
    }

    private sealed class ChainedReceiverSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // w.Inner.Count reaches through a chained access — anchor Count's declaring type directly instead.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorWidget>(w => w.Inner!.Count))).Because("Reason.");
        }
    }

    private sealed class StaticInInstanceFormSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // Instance form (arch.Member<T>) but the body reads a static member.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<DateTime>(_ => DateTime.Now))).Because("Reason.");
        }
    }

    private sealed class InstanceInStaticFormSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // Parameterless form (arch.Member(() => ...)) but the body reads an instance member off a static field.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(() => DateTime.MinValue.Ticks))).Because("Reason.");
        }
    }

    private sealed class IndexerBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // l[0] resolves to the get_Item accessor (IsSpecialName) — an indexer, outside the member-anchor surface.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<List<int>>(l => l[0]))).Because("Reason.");
        }
    }

    private sealed class StaticMethodGroupBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
#pragma warning disable CS8974 // deliberately anchoring a static method group (the mistake under test)
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(() => AnchorStatics.Beep))).Because("Reason.");
#pragma warning restore CS8974
        }
    }

    private sealed class UserOpConversionReceiverSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // ((AnchorFahrenheit)c).Value peels through the explicit user-defined conversion — not an identity cast.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member<AnchorCelsius>(c => ((AnchorFahrenheit)c).Value))).Because("Reason.");
        }
    }

    private sealed class CompileTimeConstantBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // DayOfWeek.Monday is a compile-time enum constant — inlined to its value, no member remains.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(() => DayOfWeek.Monday))).Because("Reason.");
        }
    }

    private sealed class MultiplePoisonedMembersSpec : IArchitectureSpec
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

    private sealed class ForeignExpressionMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            var other = new Arch();
            Member foreign = other.Member<Task>(t => t.Wait());
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(foreign)).Because("Reason.");
        }
    }

    private sealed class ValidExpressionMemberSpec : IArchitectureSpec
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
    private sealed class VerbNonMemberBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // new object() is an object-creation expression, not a member access.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => new object())).Because("Reason.");
        }
    }

    private sealed class VerbStaticMethodGroupBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
#pragma warning disable CS8974 // deliberately anchoring a static method group (the mistake under test)
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => AnchorStatics.Beep)).Because("Reason.");
#pragma warning restore CS8974
        }
    }

    private sealed class VerbInstanceInStaticFormSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // Static form (() => ...) but the body reads an instance member (.Ticks) off a static field.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => DateTime.MinValue.Ticks)).Because("Reason.");
        }
    }

    private sealed class VerbIndexerBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // new List<int>()[0] resolves to the get_Item accessor (IsSpecialName) — checked before receiver classification.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => new List<int>()[0])).Because("Reason.");
        }
    }

    private sealed class VerbCompileTimeConstantBodySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // DayOfWeek.Monday is a compile-time enum constant — inlined to its value, no member remains.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(() => DayOfWeek.Monday)).Because("Reason.");
        }
    }

    private sealed class VerbMultiplePoisonedMembersSpec : IArchitectureSpec
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
    private sealed class ValidVerbMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule")
                .Enforce(arch.Types.MustNotUse(() => DateTime.Now, () => DateTime.UtcNow))
                .Because("Reason.");
        }
    }

    private sealed class MultipleProblemsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // Bad_Id → MalformedId; no .Because → MissingBecause; dangling scope → DanglingAnchor.
            arch.Rule("Bad_Id").Enforce(arch.Types.MustHavePrefix("I"));
            arch.Scope("other/scope");
        }
    }

    // Phase 17 — spec-source locations (caller-info diagnostics). These are appended at the end of the class
    // so the existing failing-spec fixtures above keep their authored line numbers, which the re-pinned
    // messages encode (the file:line maintenance contract, same as violation goldens).
    [Fact]
    public void SpecValidationError_NoCapturedLocation_RendersMessageWithNoPrefix()
    {
        // The pre-caller-info degradation seam: an error minted without a source location — as a spec DLL
        // compiled against the previous Core yields — renders today's message verbatim, no location prefix
        // and no leading blank. Simulated via the internal ctor since we cannot compile against an older Core.
        var error = new SpecValidationError(Code.DanglingAnchor, "area/x",
            "Rule 'area/x' has no posture; call .Enforce(...) or .Migrate(...).");

        error.Location.ShouldBeNull();
        error.Message.ShouldBe("Rule 'area/x' has no posture; call .Enforce(...) or .Migrate(...).");
    }

    [Fact]
    public void SpecValidationError_Location_IsFileNameOnlyAndLineCaptured()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingBecauseRuleSpec());
        SpecValidationError error = ex.Errors.First(e => e.Code == Code.MissingBecause);

        error.Location.ShouldNotBeNull();
        // File name only — never the machine-specific directory — so goldens stay byte-identical across build
        // machines; the line is the captured 1-based anchor line.
        error.Location!.File.ShouldBe("SpecValidationTests.cs");
        error.Location.Line.ShouldBeGreaterThan(0);
        error.Message.ShouldStartWith("SpecValidationTests.cs:");
    }

    [Fact]
    public void AllErrors_MultiErrorSpec_EachRendersItsAnchorLocation()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultipleProblemsSpec());

        // Every error in the one-pass batch lands at its own anchor's file:line — the rule's malformed ID and
        // missing Because on one line, the dangling scope on the next (two distinct anchor lines).
        ex.Errors.ShouldAllBe(e => e.Location != null && e.Location.File == "SpecValidationTests.cs");
        ex.Errors.Select(e => e.Location!.Line).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void MemberPoison_AnchorsToMemberCallSite_RuleErrorAnchorsToRuleAnchor()
    {
        SpecValidationException ex = BuildExpectingFailure(new MemberPoisonBelowRuleSpec());
        SpecValidationError ruleError = ex.Errors.First(e => e.Code == Code.MissingBecause);
        SpecValidationError memberError = ex.Errors.First(e => e.Code == Code.MemberExpressionUnresolvable);

        ruleError.Location.ShouldNotBeNull();
        memberError.Location.ShouldNotBeNull();
        // The member poison steers to its own arch.Member(...) lambda line — below the arch.Rule(...) anchor
        // the rule-level MissingBecause renders at — proving item-18 steers point at the offending construct,
        // not the consuming rule (GRAMMAR §8).
        memberError.Location!.Line.ShouldBeGreaterThan(ruleError.Location!.Line);
    }

    private sealed class MemberPoisonBelowRuleSpec : IArchitectureSpec
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

    // WP-2 — SpecValidator blank-pattern arms (GRAMMAR §8 item 15). Appended at the very end of the class so
    // every file:line golden above keeps its authored line number (the caller-info maintenance contract) — and
    // for the same reason this file must NOT be run through a member-reordering cleanup profile. Each arm — the
    // shape/naming verb's own glob, a subject-side adjective, and their member analogs — routes through
    // CheckPattern and emits the one shared Code.BlankPattern; a distinct spec per arm walks each
    // ConstraintPatterns / SelectionPatterns / MemberAdjectivePatterns code path.
    [Fact]
    public void BlankPattern_BlankTypeNameMatchingVerb_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankTypeNameMatchingVerbSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankPattern_BlankTypeNameMatchingAdjective_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankTypeNameMatchingAdjectiveSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankPattern_BlankTypePrefixAdjective_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankTypePrefixAdjectiveSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankPattern_BlankMemberNameMatchingAdjective_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberNameMatchingAdjectiveSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankPattern_BlankMemberPrefixAdjective_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberPrefixAdjectiveSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankPattern_BlankMemberNameMatchingVerb_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberNameMatchingVerbSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankPattern_BlankMemberPrefixVerb_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberPrefixVerbSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
    }

    private sealed class BlankTypeNameMatchingVerbSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The verb's own name glob (ConstraintPatterns MustHaveNameMatchingConstraint arm).
            arch.Rule("area/rule").Enforce(arch.Types.MustHaveNameMatching(" ")).Because("Reason.");
        }
    }

    private sealed class BlankTypeNameMatchingAdjectiveSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The subject-side WithNameMatching adjective glob (SelectionPatterns WithNameMatchingAdjective arm).
            arch.Rule("area/rule").Enforce(arch.Types.WithNameMatching(" ").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class BlankTypePrefixAdjectiveSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The subject-side WithPrefix adjective affix (SelectionPatterns WithPrefixAdjective arm).
            arch.Rule("area/rule").Enforce(arch.Types.WithPrefix(" ").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class BlankMemberNameMatchingAdjectiveSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The member-subject WithNameMatching adjective glob (MemberAdjectivePatterns arm).
            arch.Rule("area/rule").Enforce(arch.Types.Methods.WithNameMatching(" ").MustBePublic()).Because("Reason.");
        }
    }

    private sealed class BlankMemberPrefixAdjectiveSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The member-subject WithPrefix adjective affix (MemberAdjectivePatterns arm).
            arch.Rule("area/rule").Enforce(arch.Types.Methods.WithPrefix(" ").MustBePublic()).Because("Reason.");
        }
    }

    private sealed class BlankMemberNameMatchingVerbSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The member verb's own name glob (ConstraintPatterns MemberMustHaveNameMatchingConstraint arm).
            arch.Rule("area/rule").Enforce(arch.Types.Members.MustHaveNameMatching(" ")).Because("Reason.");
        }
    }

    private sealed class BlankMemberPrefixVerbSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The member verb's own affix (ConstraintPatterns MemberMustHavePrefixConstraint arm).
            arch.Rule("area/rule").Enforce(arch.Types.Members.MustHavePrefix(" ")).Because("Reason.");
        }
    }
}