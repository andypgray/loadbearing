using Shouldly;
using Xunit;
using Zphil.LoadBearing.Hosting;
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

    // The catalog's data arms: one failing spec per code, where the reported (code, rule ID) pair is the
    // whole claim. The facts below are the arms that pin something more — a message, a null rule ID, or
    // several errors from one pass.
    [Theory]
    [InlineData(typeof(DanglingRuleSpec), Code.MissingPosture, "area/dangling")]
    [InlineData(typeof(MissingBecauseScopeSpec), Code.MissingBecause, "legacy/billing")]
    [InlineData(typeof(MissingDragonsSpec), Code.MissingDragons, "legacy/billing")]
    [InlineData(typeof(BlankDescriptionSpec), Code.BlankProse, "area/rule")]
    [InlineData(typeof(MultiLineBecauseSpec), Code.MultiLineProse, "area/rule")]
    [InlineData(typeof(MalformedIdSpec), Code.MalformedId, "Bad_Id")]
    [InlineData(typeof(ForeignSelectionSpec), Code.ForeignSelection, "area/rule")]
    // A scope's sanctioned surface rides the same walks its quarantined interior does, so the boundary's
    // foreign-Arch and blank-pattern reports need no arm of their own — they are the scope's, by its ID.
    [InlineData(typeof(ForeignBoundarySpec), Code.ForeignSelection, "legacy/billing")]
    [InlineData(typeof(BlankBoundaryPatternSpec), Code.BlankPattern, "legacy/billing")]
    // The extended prose walk reaches a member Where description and a member Must description
    // (GRAMMAR §8 item 5, §4.6).
    [InlineData(typeof(BlankMemberWhereSpec), Code.BlankProse, "area/rule")]
    [InlineData(typeof(BlankMemberMustSpec), Code.BlankProse, "area/rule")]
    // An expression-minted member is Owner-stamped like the typeof form, so the foreign-Arch check
    // (which precedes the poison short-circuit) catches it (GRAMMAR §8 item 13).
    [InlineData(typeof(ForeignExpressionMemberSpec), Code.ForeignMember, "area/rule")]
    // SpecValidator blank-pattern arms (GRAMMAR §8 item 15). Each arm — the shape/naming verb's own glob,
    // a subject-side adjective, and their member analogs — routes through CheckPattern and emits the one
    // shared Code.BlankPattern; a distinct spec per arm walks each ConstraintPatterns / SelectionPatterns /
    // MemberAdjectivePatterns code path.
    [InlineData(typeof(BlankTypeNameMatchingVerbSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankTypeNameMatchingAdjectiveSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankTypePrefixAdjectiveSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankTypeNamedAdjectiveSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankMemberNameMatchingAdjectiveSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankMemberPrefixAdjectiveSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankMemberNameMatchingVerbSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankMemberPrefixVerbSpec), Code.BlankPattern, "area/rule")]
    // MustNotConstruct's foreign-target reach. The verb overrides Operands (the dependency-verb walk hook,
    // like the reference verbs), so the existing §8 item 10 foreign-selection walk (ConstraintSelections →
    // CheckForeign) reaches a construct target minted on another Arch with no new validator arm.
    [InlineData(typeof(ForeignConstructTargetSpec), Code.ForeignSelection, "area/rule")]
    // Surface union: a union carries adjectives of its own (GRAMMAR §5.1), so every §8 walk must reach them
    // and not stop at the operands — including the union's own Except payload, not just its operands.
    [InlineData(typeof(UnionBlankWhereSpec), Code.BlankProse, "area/rule")]
    [InlineData(typeof(UnionBlankPatternSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(UnionForeignExceptPayloadSpec), Code.ForeignSelection, "area/rule")]
    [InlineData(typeof(UnionForeignOperandSpec), Code.ForeignSelection, "area/rule")]
    // The project stratum (GRAMMAR §4.10). A project selection carries its own Arch — it has no underlying
    // type selection to borrow one from — so §8 item 22 is its own walk, and it reaches an Except payload
    // exactly as the type-side foreign walk does. The blank checks (items 23–24) take their own codes
    // rather than the shared BlankPattern, because a blank project name matches nothing while a blank glob
    // matches everything and the catalog has to be able to say which. Blank project escape-hatch
    // descriptions ride the existing BlankProse, which already covers a description anywhere (item 5).
    [InlineData(typeof(ForeignProjectSelectionSpec), Code.ForeignProjectSelection, "area/rule")]
    [InlineData(typeof(ForeignProjectExceptPayloadSpec), Code.ForeignProjectSelection, "area/rule")]
    [InlineData(typeof(BlankProjectPatternSpec), Code.BlankProjectPattern, "project/blank-name")]
    [InlineData(typeof(BlankProjectPatternSpec), Code.BlankProjectPattern, "project/blank-glob")]
    [InlineData(typeof(BlankProjectPatternSpec), Code.BlankProjectPattern, "project/blank-in-except")]
    [InlineData(typeof(BlankTargetFrameworkSpec), Code.BlankTargetFramework, "area/rule")]
    [InlineData(typeof(BlankProjectWhereSpec), Code.BlankProse, "area/rule")]
    [InlineData(typeof(BlankProjectMustSpec), Code.BlankProse, "area/rule")]
    // A rule's Citation is prose in the item-5 walk and a URL in item 30's, so a blank one reports as prose
    // and only a well-formed single line reaches the shape check.
    [InlineData(typeof(BlankCitationSpec), Code.BlankProse, "area/rule")]
    [InlineData(typeof(MalformedCitationSpec), Code.MalformedCitation, "citation/page-title")]
    // The correspondence verb's name template (GRAMMAR §8 items 25–26). Blankness rides the shared
    // BlankPattern walk under its own label, because a blank template is the same slip as a blank affix;
    // a template that is merely missing its {Name} needs its own code, since it is well-formed text that
    // states the wrong kind of law.
    [InlineData(typeof(BlankCounterpartTemplateSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(PlaceholderFreeCounterpartTemplateSpec), Code.CounterpartTemplateWithoutPlaceholder, "area/rule")]
    // The family (GRAMMAR §5.1, §8 items 27–28). A family may stand only as a rule subject, so every
    // other position is item 27 under one code — the message names which, and the arms below walk each
    // path that reaches it. Its cells and its own adjectives ride the walks a union's operands do, so a
    // foreign cell, a blank glob on the family and the project stratum inside a project family need no
    // new codes at all.
    [InlineData(typeof(FamilyAsOperandSpec), Code.FamilyMisplaced, "area/rule")]
    [InlineData(typeof(FamilyAsExceptPayloadSpec), Code.FamilyMisplaced, "area/rule")]
    [InlineData(typeof(FamilyAsUnionOperandSpec), Code.FamilyMisplaced, "area/rule")]
    [InlineData(typeof(FamilyAsMembershipSpec), Code.FamilyMisplaced, "area/rule")]
    [InlineData(typeof(FamilyAsCounterpartAmongSpec), Code.FamilyMisplaced, "area/rule")]
    [InlineData(typeof(FamilyAsScopedSelectionSpec), Code.FamilyMisplaced, "legacy/modules")]
    [InlineData(typeof(FamilyAsBoundarySpec), Code.FamilyMisplaced, "legacy/modules")]
    [InlineData(typeof(EachOtherWithoutFamilySpec), Code.EachOtherWithoutFamily, "area/rule")]
    [InlineData(typeof(CircularReferencesOnPlainSubjectSpec), Code.CircularReferencesNeedLayerFamily, "area/rule")]
    [InlineData(typeof(CircularReferencesOnProjectFamilySpec), Code.CircularReferencesNeedLayerFamily, "area/rule")]
    [InlineData(typeof(ForeignFamilyCellSpec), Code.ForeignSelection, "area/rule")]
    [InlineData(typeof(BlankFamilyGlobSpec), Code.BlankPattern, "area/rule")]
    [InlineData(typeof(BlankProjectFamilyGlobSpec), Code.BlankProjectPattern, "area/rule")]
    [InlineData(typeof(ForeignProjectFamilySpec), Code.ForeignProjectSelection, "area/rule")]
    public void Validate_FailingSpec_ReportsItsCodeAndRuleId(Type specType, Code code, string ruleId)
    {
        var spec = (IArchitectureSpec)Activator.CreateInstance(specType)!;

        SpecValidationException ex = BuildExpectingFailure(spec);

        ex.ShouldHaveError(code, ruleId);
    }

    [Fact]
    public void DuplicateId_AcrossTwoSpecClasses_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DuplicateIdSpecA(), new DuplicateIdSpecB());

        ex.ShouldHaveError(Code.DuplicateId, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:18: Duplicate rule ID 'area/rule'.");
    }

    [Fact]
    public void IdExtendsScope_ReservedNamespace_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new IdExtendsScopeSpec());

        ex.ShouldHaveError(Code.IdExtendsScope, "legacy/billing/foo")
            .Message.ShouldContain("extends scope 'legacy/billing'");
    }

    [Fact]
    public void MissingBecause_OnRule_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingBecauseRuleSpec());

        ex.ShouldHaveError(Code.MissingBecause, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:52: 'area/rule' is missing a required .Because(...).");
    }

    [Fact]
    public void RepeatedCall_BecauseTwice_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new RepeatedBecauseSpec());

        ex.ShouldHaveError(Code.RepeatedCall, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:92: Repeated .Because(...) on 'area/rule'.");
    }

    [Fact]
    public void RepeatedPosture_PostureVerbTwiceViaStoredRuleBuilder_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DoublePostureRuleSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:111: Rule 'area/rule' has more than one posture; call .Enforce(...) or .Migrate(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_QuarantineTwiceViaStoredScopeBuilder_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DoubleQuarantineScopeSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "legacy/billing")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:123: Scope 'legacy/billing' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_CautionTwiceViaStoredScopeBuilder_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DoubleCautionScopeSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "shared/utilities")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1025: Scope 'shared/utilities' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_QuarantineThenCautionViaStoredScopeBuilder_IsReported()
    {
        // Two different verbs, not two of one: the count is what the check reads, so mixing them is the
        // same overwrite and reports the same way.
        SpecValidationException ex = BuildExpectingFailure(new MixedPostureScopeSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "shared/utilities")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1037: Scope 'shared/utilities' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.");
    }

    [Fact]
    public void DanglingScope_NoPostureVerb_IsReportedNamingBothVerbs()
    {
        // The dangling report reads the posture field rather than the selection, because a caution sets
        // both and "no posture" is the thing the author has to fix.
        SpecValidationException ex = BuildExpectingFailure(new MultipleProblemsSpec());

        ex.ShouldHaveError(Code.MissingPosture, "other/scope")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:509: Scope 'other/scope' has no posture; call .Quarantine(...) or .Caution(...).");
    }

    [Fact]
    public void MissingDragons_OnACaution_NamesTheCautionedScope()
    {
        // The message names the posture the scope was declared under, because the author's next move is to
        // find that verb in the spec and add the clause beneath it.
        SpecValidationException ex = BuildExpectingFailure(new MissingDragonsCautionSpec());

        ex.ShouldHaveError(Code.MissingDragons, "shared/utilities")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1047: Cautioned scope 'shared/utilities' is missing .Dragons(...) or .DragonsDoc(...).");
    }

    [Fact]
    public void ValidCaution_BuildsClean()
    {
        // The negative half of the catalog for the new posture: a caution carrying its two required clauses
        // is not merely unreported, it builds.
        ArchModelBuilder.Build(new ValidCautionSpec())
            .Rules.Select(rule => rule.Id)
            .ShouldBe(["shared/utilities/tripwire"]);
    }

    [Fact]
    public void EmptyBoundary_BoundaryOnlyViaWithNoTypes_IsReported()
    {
        // That EmptyBoundarySpec compiles at all is half the pin: BoundaryOnlyVia() binds uniquely to the
        // Type overload because the Selection form takes (first, params more). Flatten that form to plain
        // params and the call is ambiguous under CS0121, taking this hint out of reach (GRAMMAR §7).
        SpecValidationException ex = BuildExpectingFailure(new EmptyBoundarySpec());

        ex.ShouldHaveError(Code.EmptyBoundary, "legacy/billing")
            .Message.ShouldContain("omit the call for a hermetic quarantine");
    }

    [Fact]
    public void DuplicateLayerName_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new DuplicateLayerSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.DuplicateLayerName);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Duplicate layer name 'Dup'.");
    }

    [Theory]
    [InlineData(typeof(FamilyAsOperandSpec), "an operand")]
    [InlineData(typeof(FamilyAsExceptPayloadSpec), "an Except payload")]
    [InlineData(typeof(FamilyAsUnionOperandSpec), "a union operand")]
    [InlineData(typeof(FamilyAsMembershipSpec), "an operand")]
    [InlineData(typeof(FamilyAsCounterpartAmongSpec), "an operand")]
    public void FamilyMisplaced_OnARule_NamesThePositionItStandsIn(Type specType, string position)
    {
        // One sentence for every position, varying on the position alone: what an author has to move is
        // the family, and where it stands is the whole of what they need told (GRAMMAR §8 item 27).
        var spec = (IArchitectureSpec)Activator.CreateInstance(specType)!;

        SpecValidationException ex = BuildExpectingFailure(spec);

        ex.ShouldHaveError(Code.FamilyMisplaced, "area/rule")
            .Message.ShouldEndWith(
                $"A family (`arch.Each`) used as {position} by 'area/rule'; a family may stand only as a rule subject.");
    }

    [Theory]
    [InlineData(typeof(FamilyAsScopedSelectionSpec), "a scoped selection")]
    [InlineData(typeof(FamilyAsBoundarySpec), "a boundary")]
    public void FamilyMisplaced_OnAScope_NamesThePositionItStandsIn(Type specType, string position)
    {
        var spec = (IArchitectureSpec)Activator.CreateInstance(specType)!;

        SpecValidationException ex = BuildExpectingFailure(spec);

        ex.ShouldHaveError(Code.FamilyMisplaced, "legacy/modules")
            .Message.ShouldEndWith(
                $"A family (`arch.Each`) used as {position} by 'legacy/modules'; "
                + "a family may stand only as a rule subject.");
    }

    [Fact]
    public void FamilyMisplaced_AsALayerDefinition_IsReportedSpecWideNamingTheLayer()
    {
        // A definition is use-independent, so its error is the layer's: null ID, no location, named by
        // layer — the same terms every other layer error reports on.
        SpecValidationException ex = BuildExpectingFailure(new FamilyAsLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.FamilyMisplaced);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe(
            "A family (`arch.Each`) used as a layer definition by layer 'Modules'; "
            + "a family may stand only as a rule subject.");
    }

    [Fact]
    public void EachOtherWithoutFamily_OnAPlainSubject_NamesTheVerbTheAuthorMeant()
    {
        // The cross-cell ban's targets are the subject's own cells, so a cell-free subject names nothing
        // to forbid — and the plain-selection verb has a name (GRAMMAR §8 item 28).
        SpecValidationException ex = BuildExpectingFailure(new EachOtherWithoutFamilySpec());

        ex.ShouldHaveError(Code.EachOtherWithoutFamily, "area/rule")
            .Message.ShouldEndWith(
                "`MustNotReferenceEachOther` on 'area/rule' needs a family subject (`arch.Each`); "
                + "over a plain selection write `MustNotReference`.");
    }

    [Fact]
    public void CircularReferencesNeedLayerFamily_OnAPlainSubject_SaysThereAreNoLayers()
    {
        // The cycle gate's nodes are the subject's own cells, so a cell-free subject has no graph at all
        // (GRAMMAR §8 item 29).
        SpecValidationException ex = BuildExpectingFailure(new CircularReferencesOnPlainSubjectSpec());

        ex.ShouldHaveError(Code.CircularReferencesNeedLayerFamily, "area/rule")
            .Message.ShouldEndWith(
                "`MustNotHaveCircularReferences` on 'area/rule' needs a family of layers (`arch.Each`); "
                + "over a plain selection there are no layers to reference each other.");
    }

    [Fact]
    public void CircularReferencesNeedLayerFamily_OnAProjectFamily_NamesTheVerbsTheAuthorMeant()
    {
        // The build forbids circular project references, so over a family of projects the law holds by
        // construction — a rule that cannot red is a false promise, and the two verbs that can say
        // something are named (GRAMMAR §8 item 29).
        SpecValidationException ex = BuildExpectingFailure(new CircularReferencesOnProjectFamilySpec());

        ex.ShouldHaveError(Code.CircularReferencesNeedLayerFamily, "area/rule")
            .Message.ShouldEndWith(
                "`MustNotHaveCircularReferences` on 'area/rule' needs a family of layers; projects cannot "
                + "have circular references, so over a family of projects the law holds by construction. "
                + "Write `MustNotReferenceEachOther` or an ordering rule.");
    }

    [Fact]
    public void AllErrors_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultipleProblemsSpec());

        ex.Errors.Select(e => e.Code)
            .Distinct()
            .Count()
            .ShouldBeGreaterThanOrEqualTo(3);
        ex.ShouldHaveError(Code.MalformedId);
        ex.ShouldHaveError(Code.MissingBecause);
        ex.ShouldHaveError(Code.MissingPosture);
    }

    [Fact]
    public void BlankMemberName_OnMustNotUse_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberNameSpec());

        ex.ShouldHaveError(Code.BlankMemberName, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:161: Blank member name on a member of 'System.DateTime' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_TypoName_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new TypoMemberSpec());

        ex.ShouldHaveError(Code.MemberNotDeclared, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:169: 'System.DateTime' does not declare a member named 'Nows' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_MemberOnBaseType_NamesBaseAndTypeof()
    {
        SpecValidationException ex = BuildExpectingFailure(new BaseTypeMemberSpec());

        ex.ShouldHaveError(Code.MemberNotDeclared, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:178: 'System.Threading.Tasks.Task<TResult>' does not declare 'Wait'; it is declared on base type " +
                      "'System.Threading.Tasks.Task' — use typeof(Task) (used by 'area/rule').");
    }

    [Fact]
    public void ForeignMember_MemberFromAnotherArch_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ForeignMemberSpec());

        ex.ShouldHaveError(Code.ForeignMember, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:187: A member used by 'area/rule' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void ForeignProjectSelection_ProjectSubjectFromAnotherArch_IsReported()
    {
        // Named as a PROJECT selection rather than folded into the type-side message: an author whose rule
        // mixes strata needs the report to say which one was foreign to find it.
        SpecValidationException ex = BuildExpectingFailure(new ForeignProjectSelectionSpec());

        ex.ShouldHaveError(Code.ForeignProjectSelection, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:884: A project selection used by 'area/rule' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void BlankProjectPattern_BlankNameAndBlankGlob_AreReportedByKind()
    {
        // One code, two labels: the message names which operand was left empty, because a blank name
        // matches nothing while a blank glob matches everything.
        SpecValidationException ex = BuildExpectingFailure(new BlankProjectPatternSpec());

        ex.ShouldHaveError(Code.BlankProjectPattern, "project/blank-name")
            .Message.ShouldBe("SpecValidationSpecs.cs:903: Blank project name on 'project/blank-name'.");
        ex.ShouldHaveError(Code.BlankProjectPattern, "project/blank-glob")
            .Message.ShouldBe("SpecValidationSpecs.cs:904: Blank project name pattern on 'project/blank-glob'.");
    }

    [Fact]
    public void BlankTargetFramework_OnMustOnlyTarget_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankTargetFrameworkSpec());

        ex.ShouldHaveError(Code.BlankTargetFramework, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:915: Blank target framework on 'area/rule'.");
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

        ex.ShouldHaveError(Code.MemberReturningClosedGeneric, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:212: 'System.Threading.Tasks.Task<System.Int32>' is a closed generic; .Returning matches definition-level — " +
                      "use typeof(Task<>) (used by 'area/rule').");
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

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:251: Blank namespace pattern on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankSuffix_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankSuffixSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:259: Blank suffix on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankTypeName_IsReported()
    {
        // The exact-name adjective carries a list, so the walk yields one pattern per name and the blank one
        // reports under its own label. Blankness is the whole check: a name carries no structure to check.
        SpecValidationException ex = BuildExpectingFailure(new BlankTypeNamedAdjectiveSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:963: Blank type name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankMemberSubjectAffix_IsReported()
    {
        // The member-subject adjective walk (parallel to the member prose/Returning walks) reaches a
        // blank member .WithSuffix (GRAMMAR §8 item 15, §4.6).
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberSuffixSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:267: Blank member suffix on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankLayerGlob_IsReportedSpecWide()
    {
        // The layer flavor of §8 item 15: like the dead-subtree layer case below, layer globs are
        // validated at their declaration (spec-wide, null rule ID, named by layer), used or not.
        SpecValidationException ex = BuildExpectingFailure(new BlankLayerGlobSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.BlankPattern);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Blank namespace pattern on layer 'Bad'.");
    }

    [Fact]
    public void BlankProse_BlankLayerPurpose_IsReportedSpecWide()
    {
        // A layer purpose is prose like any other (§8 item 5), reported on the layer-glob terms above:
        // spec-wide, null rule ID, named by layer, and with no file:line prefix because there is no anchor.
        SpecValidationException ex = BuildExpectingFailure(new BlankLayerPurposeSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.BlankProse);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Blank purpose on layer 'Core'.");
    }

    [Fact]
    public void MultiLineProse_MultiLineLayerPurpose_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultiLineLayerPurposeSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.MultiLineProse);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Multi-line purpose on layer 'Core'; prose fields are single-line.");
    }

    [Fact]
    public void RepeatedCall_RepeatedLayerPurpose_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new RepeatedLayerPurposeSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.RepeatedCall);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Repeated .Purpose(...) on layer 'Core'.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_WildcardInSubtreePrefix_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DeadSubtreeGlobSpec());

        ex.ShouldHaveError(Code.UnanchoredSubtreePattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:283: The namespace pattern 'MyApp.*.Controllers.*' on 'area/rule' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_OnLayerGlob_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new DeadSubtreeLayerSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.UnanchoredSubtreePattern);
        error.RuleId.ShouldBeNull();
        error.Message
            .ShouldBe("The namespace pattern 'MyApp.*.Svc.*' on layer 'Bad' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void ForeignSelection_InALayerDefinition_IsReportedSpecWide()
    {
        // A definition is validated where the layer is declared, on the layer-glob terms: spec-wide, null
        // rule ID, named by layer, and found whether or not any rule ever names the layer (§8 item 10).
        SpecValidationException ex = BuildExpectingFailure(new ForeignLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.ForeignSelection);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe(
            "A selection used by layer 'Foreign' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void BlankPattern_BlankProjectNameInALayerDefinition_IsReportedSpecWide()
    {
        // The definition takes the same blank-operand walk a rule's selections take (§8 item 15), under the
        // noun the type-side walk names it by.
        SpecValidationException ex = BuildExpectingFailure(new BlankProjectNameLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.BlankPattern);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Blank project name on layer 'Bad'.");
    }

    [Fact]
    public void UndefinedLifetime_InALayerDefinition_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new UndefinedLifetimeLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.UndefinedLifetime);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("'(Lifetime)7' is not a defined Lifetime — " +
                               "use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by layer 'Wiring').");
    }

    [Fact]
    public void UnanchoredSubtreePattern_InALayerDefinitionsExceptPayload_IsReportedSpecWide()
    {
        // The walk reaches a definition's nesting, not just its head: an Except payload is where a
        // definition hides a second selection.
        SpecValidationException ex = BuildExpectingFailure(new DeadSubtreeLayerDefinitionSpec());

        ex.ShouldHaveError(Code.UnanchoredSubtreePattern)
            .Message
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
        ex.Errors.ShouldContain(e => e.Code == Code.UnanchoredSubtreePattern, expectedCount: 2);
        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern, expectedCount: 1);
    }

    [Fact]
    public void MemberExpression_NonMemberBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new NonMemberBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:318: A member anchor lambda must be a single property, field, or method access " +
                      "(x => x.Member or () => Type.Member); this lambda body is neither (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_MethodGroupBody_IsReportedWithInvocationGuidance()
    {
        // A method-group anchor `w => w.Reset` compiles on C# 14 (converting the method group to object,
        // warning CS8974 — suppressed in the spec) and lowers to a CreateDelegate tree the resolver detects;
        // on C# <= 13 it does not compile at all. Steers to the invocation form.
        SpecValidationException ex = BuildExpectingFailure(new MethodGroupBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:327: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_ChainedReceiver_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ChainedReceiverSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:337: A member anchor lambda must reach its member directly on the lambda parameter (an interface " +
                      "cast or as-cast is allowed; a chained access like x => x.A.B, a captured local or field, or a " +
                      "user-defined conversion is not); anchor the declaring type you mean directly (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_StaticMemberInInstanceForm_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new StaticInInstanceFormSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:346: A typed member anchor arch.Member<T>(x => ...) accesses a static member; anchor statics " +
                      "with the parameterless overload arch.Member(() => Type.Member) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_InstanceMemberInStaticForm_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new InstanceInStaticFormSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:355: A parameterless member anchor arch.Member(() => ...) must access a static member " +
                      "directly; anchor an instance member with the typed overload arch.Member<T>(x => x.Member) " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_IndexerBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new IndexerBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:364: A member anchor lambda resolves to an indexer accessor (get_Item), which is outside the " +
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

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:373: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
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

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:383: A member anchor lambda must reach its member directly on the lambda parameter (an interface " +
                      "cast or as-cast is allowed; a chained access like x => x.A.B, a captured local or field, or a " +
                      "user-defined conversion is not); anchor the declaring type you mean directly (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_CompileTimeConstantBody_IsReported()
    {
        // () => DayOfWeek.Monday inlines the enum member to its value (Convert(Constant(...), object)), so
        // Unwrap peels to a ConstantExpression with no member left to anchor.
        SpecValidationException ex = BuildExpectingFailure(new CompileTimeConstantBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:392: A member anchor lambda body is a compile-time constant (a const field, an enum member, or a " +
                      "literal) that the compiler inlines to its value, so no member remains to anchor; name a const " +
                      "or enum member with the typeof form arch.Member(typeof(T), nameof(T.M)) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpressions_MultiplePoisoned_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultiplePoisonedMembersSpec());

        // Two poisoned member anchors on one rule → two errors in one pass (the §8 all-at-once contract).
        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable, expectedCount: 2);
    }

    [Fact]
    public void ValidExpressionMemberUse_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidExpressionMemberSpec()));
    }

    // Verb-position poison parity: the same unresolvable-anchor diagnostics the anchor-position
    // twins pin (above) fire when the poisoned lambda is passed bare to MustNotUse's static forms, because
    // the verb desugars each target through the identical MemberExpressionResolver. Six of the eight poison
    // classes are reachable from the static forms; the two instance-form steers have no static-verb spelling.
    // Message strings are copied verbatim from the anchor-position twins — the pinned strings are the spec.
    [Fact]
    public void MemberExpression_VerbNonMemberBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbNonMemberBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:438: A member anchor lambda body is an object creation (a new expression, including target-typed new()); " +
                      "construction is not a member use, so ban the constructed type with the MustNotConstruct verb instead (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbStaticMethodGroupBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbStaticMethodGroupBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:447: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbInstanceMemberInStaticForm_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbInstanceInStaticFormSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:457: A parameterless member anchor arch.Member(() => ...) must access a static member " +
                      "directly; anchor an instance member with the typed overload arch.Member<T>(x => x.Member) " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbIndexerBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbIndexerBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:466: A member anchor lambda resolves to an indexer accessor (get_Item), which is outside the " +
                      "member-anchor surface (GRAMMAR §4.5); anchor a named property, field, or method " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbCompileTimeConstantBody_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbCompileTimeConstantBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:475: A member anchor lambda body is a compile-time constant (a const field, an enum member, or a " +
                      "literal) that the compiler inlines to its value, so no member remains to anchor; name a const " +
                      "or enum member with the typeof form arch.Member(typeof(T), nameof(T.M)) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpressions_VerbMultiplePoisoned_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new VerbMultiplePoisonedMembersSpec());

        // Two poisoned verb-position anchors on one rule → two errors in one pass (the §8 all-at-once contract).
        ex.Errors.ShouldContain(e => e.Code == Code.MemberExpressionUnresolvable, expectedCount: 2);
    }

    [Fact]
    public void ValidVerbMemberUse_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidVerbMemberSpec()));
    }

    // Spec-source locations (caller-info diagnostics): the file:line an error carries is its anchor's own,
    // captured where the fixture spells the rule and pinned here verbatim.
    [Fact]
    public void SpecValidationError_NoCapturedLocation_RendersMessageWithNoPrefix()
    {
        // The pre-caller-info degradation seam: an error minted without a source location — as a spec DLL
        // compiled against the previous Core yields — renders today's message verbatim, no location prefix
        // and no leading blank. Simulated via the internal ctor since we cannot compile against an older Core.
        var error = new SpecValidationError(Code.MissingPosture, "area/x",
            "Rule 'area/x' has no posture; call .Enforce(...) or .Migrate(...).");

        error.Location.ShouldBeNull();
        error.Message.ShouldBe("Rule 'area/x' has no posture; call .Enforce(...) or .Migrate(...).");
    }

    [Fact]
    public void SpecValidationError_Location_IsFileNameOnlyAndLineCaptured()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingBecauseRuleSpec());
        SpecValidationError error = ex.ShouldHaveError(Code.MissingBecause);

        SpecSourceLocation location = error.Location.ShouldNotBeNull();
        // File name only — never the machine-specific directory — so goldens stay byte-identical across build
        // machines; the line is the captured 1-based anchor line.
        location.File.ShouldBe("SpecValidationSpecs.cs");
        location.Line.ShouldBeGreaterThan(0);
        error.Message.ShouldStartWith("SpecValidationSpecs.cs:");
    }

    [Fact]
    public void AllErrors_MultiErrorSpec_EachRendersItsAnchorLocation()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultipleProblemsSpec());

        // Every error in the one-pass batch lands at its own anchor's file:line — the rule's malformed ID and
        // missing Because on one line, the dangling scope on the next (two distinct anchor lines).
        ex.Errors.ShouldAllBe(e => e.Location != null && e.Location.File == "SpecValidationSpecs.cs");
        ex.Errors.Select(e => e.Location!.Line)
            .Distinct()
            .Count()
            .ShouldBe(2);
    }

    [Fact]
    public void MemberPoison_AnchorsToMemberCallSite_RuleErrorAnchorsToRuleAnchor()
    {
        SpecValidationException ex = BuildExpectingFailure(new MemberPoisonBelowRuleSpec());
        SpecValidationError ruleError = ex.ShouldHaveError(Code.MissingBecause);
        SpecValidationError memberError = ex.ShouldHaveError(Code.MemberExpressionUnresolvable);

        SpecSourceLocation ruleAnchor = ruleError.Location.ShouldNotBeNull();
        SpecSourceLocation memberAnchor = memberError.Location.ShouldNotBeNull();
        // The member poison steers to its own arch.Member(...) lambda line — below the arch.Rule(...) anchor
        // the rule-level MissingBecause renders at — proving item-18 steers point at the offending construct,
        // not the consuming rule (GRAMMAR §8).
        memberAnchor.Line.ShouldBeGreaterThan(ruleAnchor.Line);
    }

    // GRAMMAR §8 item 19: an undefined Lifetime value on an arch.Registered noun used by a rule. The check rides the shared
    // RuleSelections walk, reaching the Registered subject with no new selection plumbing.
    [Fact]
    public void UndefinedLifetime_CastToUndefinedValue_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new UndefinedLifetimeSpec());

        ex.ShouldHaveError(Code.UndefinedLifetime, "di/lifetimes")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:605: '(Lifetime)7' is not a defined Lifetime — " +
                      "use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by 'di/lifetimes').");
    }

    // GRAMMAR §8 item 20: a closed-generic MustAcceptParameter anchor on a method selection. Parameter-type matching is
    // definition-level, so a closed construction is refused with the open-definition steer; the check mirrors
    // the item-14 .Returning refusal, reading as its sibling with the verb named in place of .Returning.
    [Fact]
    public void MemberAcceptParameterClosedGeneric_ClosedAnchor_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ClosedGenericAcceptParameterSpec());

        ex.ShouldHaveError(Code.MemberAcceptParameterClosedGeneric, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:616: 'System.IProgress<System.Int32>' is a closed generic; MustAcceptParameter matches definition-level — " +
                      "use typeof(IProgress<>) (used by 'area/rule').");
    }

    [Fact]
    public void MemberAcceptParameterClosedGeneric_BesideAnotherError_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new AcceptParameterAllAtOnceSpec());

        // The closed-generic parameter anchor and the rule's missing Because report together (the §8
        // all-at-once contract).
        ex.ShouldHaveError(Code.MemberAcceptParameterClosedGeneric, "area/rule");
        ex.ShouldHaveError(Code.MissingBecause, "area/rule");
    }

    [Fact]
    public void ValidAcceptParameter_NonGenericAndOpenGenericAnchors_BuildWithoutError()
    {
        // A non-generic anchor (typeof(CancellationToken)) and an open-generic one (typeof(IProgress<>)) are
        // both accepted — only closed generics fail.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidAcceptParameterSpec()));
    }

    // GRAMMAR §8 item 21: a category-invalid hierarchy anchor, both polarities. One shared code covers
    // the three categories; the check applies to the positives' single anchor and every anchor in a
    // negative's list, all reported in the same all-at-once pass with the rule's spec-source file:line.
    [Fact]
    public void HierarchyAnchorWrongCategory_NonInterfaceOnMustNotImplement_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new NonInterfaceImplementAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:650: 'System.Exception' is not an interface; MustNotImplement requires an interface anchor — " +
                      "use MustNotDeriveFrom for a base class (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_InterfaceOnMustDeriveFrom_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new InterfaceDeriveFromAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:661: 'System.IDisposable' is an interface; MustDeriveFrom requires a non-interface anchor — " +
                      "use MustImplement for an interface (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_NonAttributeOnMustNotBeAttributedWith_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new NonAttributeAttributedAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:672: 'System.Attribute' does not derive from System.Attribute; MustNotBeAttributedWith " +
                      "requires an attribute anchor (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_InvalidAnchorInNegativeList_BesideMissingBecause_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new HierarchyAllAtOnceSpec());

        // The invalid anchor is the SECOND in the negative's list (the first is a valid interface), and the
        // rule omits .Because — the category error and the missing Because report together (the §8 all-at-once
        // contract, and proof the check walks every anchor in a negative's list).
        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule");
        ex.ShouldHaveError(Code.MissingBecause, "area/rule");
    }

    [Fact]
    public void ValidHierarchyAnchors_CategoryCorrectPositivesAndNegatives_BuildWithoutError()
    {
        // Interface anchors on (Must[Not])Implement, non-interface anchors on (Must[Not])DeriveFrom, and
        // Attribute-derived anchors on (Must[Not])BeAttributedWith are all accepted — only wrong-category fails.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidHierarchyAnchorsSpec()));
    }

    // ---- String attribute anchors (GRAMMAR §5.2–§5.3). Blank is the ONLY well-formedness a definition FQN has, and it
    //      reports through the shared Code.BlankPattern family under the "attribute name" label (§8 item 15) —
    //      no new code. The item-21 category check deliberately does not apply: there is no category to read
    //      off a string, so a nonsense name builds clean and simply matches nothing. ----

    [Fact]
    public void BlankPattern_BlankAttributeNameOnAdjective_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankAttributeAdjectiveSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:749: Blank attribute name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankAttributeNameOnMustBeAttributedWith_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankAttributeVerbSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:759: Blank attribute name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankAttributeNameOnMustNotBeAttributedWith_IsReported()
    {
        // The blank is the SECOND anchor in the negative's list — proof the walk covers every anchor.
        SpecValidationException ex = BuildExpectingFailure(new BlankAttributeNegativeVerbSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:769: Blank attribute name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankAttributeNamesAcrossAdjectiveAndVerb_BesideMissingBecause_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankAttributeAllAtOnceSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern, expectedCount: 2);
        ex.ShouldHaveError(Code.MissingBecause, "area/rule");
    }

    [Fact]
    public void ValidStringAttributeAnchors_NonsenseNames_BuildWithoutError()
    {
        // No category check applies to a string: a non-attribute FQN, a dotless name, a suffix-less name, and
        // even "System.Attribute" — the one spelling whose typeof form item 21 refuses — all build. They name
        // no declared attribute, so they match nothing; that is the hatch's stated honesty boundary.
        Should.NotThrow(() => ArchModelBuilder.Build(new NonsenseStringAttributeAnchorSpec()));
    }

    // ---- The member attribute axis (GRAMMAR §5.7). The two VERBS carry the item-21 category check with the type
    //      side's message verbatim; the ADJECTIVE deliberately does not — a wrong-category adjective empties
    //      the subject, which the fail-on-empty gate reds loudly (pinned in MemberSubjectVerbTests), whereas
    //      the always-passing MustNot verb is the silent slip the check exists to catch. ----

    [Fact]
    public void HierarchyAnchorWrongCategory_NonAttributeOnMemberMustBeAttributedWith_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MemberNonAttributeAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:800: 'System.Exception' does not derive from System.Attribute; MustBeAttributedWith " +
                      "requires an attribute anchor (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_AttributeItselfOnMemberMustNotBeAttributedWith_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MemberAttributeItselfAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:810: 'System.Attribute' does not derive from System.Attribute; MustNotBeAttributedWith " +
                      "requires an attribute anchor (used by 'area/rule').");
    }

    [Fact]
    public void ValidMemberAttributeAnchors_AndAnUncheckedAdjective_BuildWithoutError()
    {
        // Attribute-derived anchors on both member verbs are accepted — and so is a wrong-category anchor on
        // the member ADJECTIVE, which is category-checked on neither axis.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberAttributeAnchorsSpec()));
    }

    [Fact]
    public void BlankPattern_BlankAttributeNamesOnTheMemberNodes_AreReportedInOnePass()
    {
        // One blank per member node — the adjective, the positive verb, and the SECOND anchor of a negative's
        // list — all three reported together under the shared "attribute name" label.
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberAttributeNameSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern, expectedCount: 3);
        ex.ShouldHaveError(Code.BlankPattern)
            .Message
            .ShouldBe("SpecValidationSpecs.cs:830: Blank attribute name on 'member/adjective'.");
    }

    // ---- String hierarchy anchors (GRAMMAR §5.2–§5.3, §8 items 15 and 21).
    //      Blankness is the whole of a string anchor's well-formedness, and it is checked on the adjectives as
    //      well as the verbs; the item-21 CATEGORY check reaches neither, because a string carries no category
    //      to read and refusing a spelling the host cannot load would break the hatch. ----

    [Fact]
    public void BlankPattern_BlankHierarchyNamesOnTheStringAnchors_AreReportedInOnePass()
    {
        // Six blanks across both families and all three positions each — adjective, positive verb, and the
        // SECOND anchor of a negative's list — under two labels that name which kind of anchor was left empty.
        SpecValidationException ex = BuildExpectingFailure(new BlankHierarchyNameSpec());

        List<SpecValidationError> blanks = ex.Errors.Where(e => e.Code == Code.BlankPattern)
            .ToList();
        blanks.Count.ShouldBe(6);
        blanks.ShouldContain(e => e.Message.Contains("Blank interface name"), expectedCount: 3);
        blanks.ShouldContain(e => e.Message.Contains("Blank base type name"), expectedCount: 3);
        blanks[0]
            .Message.ShouldBe("SpecValidationSpecs.cs:840: Blank interface name on 'hierarchy/implementing'.");
    }

    [Fact]
    public void ValidStringHierarchyAnchors_NonsenseNames_BuildWithoutError()
    {
        // No category check applies to a string, so the spellings item 21 refuses in typeof form all build: a
        // class named where an interface belongs, an interface named where a base class belongs, a dotless
        // name, and a constructed generic. Each names no declared type or the wrong one, so it matches
        // nothing — loud on a positive (always red), silent on a negative. That is the hatch's stated cost.
        Should.NotThrow(() => ArchModelBuilder.Build(new NonsenseStringHierarchyAnchorSpec()));
    }

    // ---- The member type anchors (GRAMMAR §5.7, §8 items 14–15 and 20). Blankness is the whole of what a
    //      string anchor is validated for here too, under labels that name which position was left empty. The
    //      closed-generic refusals items 14 and 20 carry are typeof-arm facts — nothing is inferred from a
    //      string's shape — so a constructed spelling builds and matches nothing. ----

    [Fact]
    public void BlankPattern_BlankReturnTypeName_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankReturnTypeNameSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1286: Blank return type name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankParameterTypeName_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankParameterTypeNameSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1296: Blank parameter type name on 'area/rule'.");
    }

    [Fact]
    public void ValidStringMemberTypeAnchors_ConstructedSpellingIsNotRefused()
    {
        // The spellings items 14 and 20 refuse in typeof form both build as strings: those checks read a
        // reflected type and a string carries none. Each names no definition, so it matches nothing — loud
        // when it is .Returning's sole anchor, silent beside one that matches. The hatch's stated cost.
        Should.NotThrow(() => ArchModelBuilder.Build(new NonsenseStringMemberTypeAnchorSpec()));
    }

    // ---- Project names (GRAMMAR §8 item 15). A project name carries no glob structure, so blank is the whole
    //      of its well-formedness and it reports through the shared Code.BlankPattern family under its own
    //      label. One SelectionPatterns arm covers the noun wherever it stands — subject, operand, Except
    //      payload, quarantined scope — and one ConstraintPatterns arm covers the verb. ----

    [Fact]
    public void BlankPattern_BlankProjectNames_AreReportedInOnePass()
    {
        // One blank per position — the noun as a subject, the noun as a target operand, and the verb's own
        // name — all three reported together, each steered to the rule that spells it.
        SpecValidationException ex = BuildExpectingFailure(new BlankProjectNameSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern, expectedCount: 3);
        ex.ShouldHaveError(Code.BlankPattern, "project/noun-subject")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:864: Blank project name on 'project/noun-subject'.");
        ex.ShouldHaveError(Code.BlankPattern, "project/noun-operand")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:865: Blank project name on 'project/noun-operand'.");
        ex.ShouldHaveError(Code.BlankPattern, "project/verb")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:866: Blank project name on 'project/verb'.");
    }

    // ---- The correspondence verb's name template (GRAMMAR §8 items 25–26). Two failures, deliberately under
    //      two codes: a blank template is an empty operand like any other, while a template with no {Name} in
    //      it is well-formed text that states a cardinality law instead of a correspondence one. ----

    [Fact]
    public void BlankPattern_BlankCounterpartTemplate_IsReported()
    {
        // Blankness routes through the shared CheckPattern walk, so the sentence is the family's, and the
        // label is the only thing that says which operand was left empty.
        SpecValidationException ex = BuildExpectingFailure(new BlankCounterpartTemplateSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:943: Blank counterpart name template on 'area/rule'.");
    }

    [Fact]
    public void CounterpartTemplateWithoutPlaceholder_ConstantName_IsReported()
    {
        // A template with no {Name} derives one fixed name for every subject, so the rule asks "does exactly
        // one IService exist" rather than "does each subject have its own counterpart" — green or red for the
        // whole subject set at once. The message names the template, says what the rule would actually mean,
        // and shows the spelling — which is also how a '{name}' typo is found, the match being ordinal.
        SpecValidationException ex = BuildExpectingFailure(new PlaceholderFreeCounterpartTemplateSpec());

        ex.ShouldHaveError(Code.CounterpartTemplateWithoutPlaceholder, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:953: The counterpart name template 'IService' on 'area/rule' contains no "
                      + "'{Name}' placeholder, so every subject derives the same fixed name — a cardinality claim, not a "
                      + "correspondence; use a template such as 'I{Name}' (substitution is case-sensitive).");
    }

    // ---- A rule's citation (GRAMMAR §8 items 5, 6 and 30). ----

    [Fact]
    public void RepeatedCall_CitationTwice_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new RepeatedCitationSpec());

        ex.ShouldHaveError(Code.RepeatedCall, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1319: Repeated .Citation(...) on 'area/rule'.");
    }

    [Fact]
    public void BlankProse_BlankCitation_IsReportedAsProseAndNotAsMalformed()
    {
        // A whitespace citation is an empty authored field, which item 5 already names; reporting it a second
        // time as a malformed URL would say the same slip twice in different words.
        SpecValidationException ex = BuildExpectingFailure(new BlankCitationSpec());

        ex.ShouldHaveError(Code.BlankProse, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1331: Blank Citation on 'area/rule'.");
        ex.Errors.ShouldNotContain(error => error.Code == Code.MalformedCitation);
    }

    [Theory]
    [InlineData("citation/page-title", 1342, "Reuse HttpClient")]
    [InlineData("citation/relative-path", 1346, "docs/httpclient.md")]
    [InlineData("citation/other-scheme", 1350, "ftp://example.com/guidance.txt")]
    public void MalformedCitation_NotAnAbsoluteHttpUrl_IsReported(string ruleId, int line, string citation)
    {
        // The three shapes a hand-written citation actually takes when it is wrong: the page's title pasted
        // instead of its address, a path into the repository, and a well-formed URI on a scheme no consuming
        // surface can follow. Each names the offending value, because the fix is to replace that text.
        SpecValidationException ex = BuildExpectingFailure(new MalformedCitationSpec());

        ex.ShouldHaveError(Code.MalformedCitation, ruleId)
            .Message
            .ShouldBe($"SpecValidationSpecs.cs:{line}: Malformed citation on '{ruleId}': '{citation}' is not an "
                      + "absolute http(s) URL.");
    }

    [Fact]
    public void ValidProjectNames_BuildWithoutError()
    {
        // Blankness is the whole check: a name is never held against the codebase here, because validation
        // runs before any workspace exists. A name no project carries is a rule that reds at check time — the
        // namespace-glob precedent — not a spec that refuses to build.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidProjectNamesSpec()));
    }
}
