using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;
using Code = Zphil.LoadBearing.Validation.SpecValidationErrorCode;

namespace Zphil.LoadBearing.Validation;

/// <summary>
///     Runs the whole GRAMMAR §8 catalog and collects <em>every</em> error in one pass (a
///     deliberate divergence from EF Core's fail-fast validator).
/// </summary>
/// <remarks>
///     ID checks run over the post-desugar ID set; authored-field checks run over the original
///     registrations, keyed to the rule or scope the author wrote. Every rule/scope/member-attributed
///     error carries the offending anchor's spec-source location (GRAMMAR §8) so all-errors-at-once
///     lands each one at a <c>file:line</c> jump target; spec-wide errors (duplicate layer name, layer
///     globs, layer definitions, layer purposes) stay location-free by design.
/// </remarks>
internal static class SpecValidator
{
    internal static IReadOnlyList<SpecValidationError> Validate(Arch arch)
    {
        var errors = new List<SpecValidationError>();
        ValidateLayers(arch, errors);
        ValidateIds(arch, errors);

        foreach (Registration registration in arch.Registrations)
            switch (registration)
            {
                case RuleRegistration rule:
                    ValidateRule(rule, arch, errors);
                    break;
                case ScopeRegistration scope:
                    ValidateScope(scope, arch, errors);
                    break;
            }

        return errors;
    }

    private static void ValidateLayers(Arch arch, List<SpecValidationError> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (LayerRegistration registration in arch.Layers)
        {
            LayerNoun layer = registration.Noun;
            if (!seen.Add(layer.Name))
                errors.Add(new SpecValidationError(Code.DuplicateLayerName, null, $"Duplicate layer name '{layer.Name}'."));

            // A layer's definition is validated here — its authoritative, use-independent home — so a bad
            // one is caught whether or not the layer is ever used as a subject (§8 items 10, 15–16, 19).
            // Like the duplicate-name error above, these are spec-wide (null ID, named by layer in the
            // message) and location-free: a layer name is a unique, trivially greppable string. A selection
            // definition takes every walk a rule's selections take, in the order a rule takes them.
            var subject = $"layer '{layer.Name}'";
            if (layer.Definition is { } definition)
            {
                CheckSelectionOperands([definition], null, arch, null, errors, subject);
                CheckFamilies(FamilyPositions(definition, FamilyPosition.LayerDefinition), null, null, errors, subject);
            }
            else
            {
                foreach (string glob in layer.Globs)
                    CheckPattern(glob, PatternKind.NamespacePattern, null, null, errors, subject);
            }

            // A purpose is prose like any other (§8 items 5–6) and is checked where the layer is declared, on the
            // same spec-wide terms as its globs: null ID, no location, named by layer.
            foreach (string purpose in registration.Purposes)
                CheckProse(purpose, "purpose", null, null, errors, subject);
            CheckRepeated(registration.Purposes.Count, "Purpose", null, null, errors, subject);
        }
    }

    private static void ValidateIds(Arch arch, List<SpecValidationError> errors)
    {
        List<RuleRegistration> rules = arch.Registrations.OfType<RuleRegistration>().ToList();
        List<ScopeRegistration> scopes = arch.Registrations.OfType<ScopeRegistration>().ToList();

        // Malformed ID — walked per registration (not over the flattened ID list) so the offending anchor's
        // location rides along.
        foreach (Registration registration in arch.Registrations)
            if (!RuleIdSyntax.IsValid(registration.Id))
                errors.Add(new SpecValidationError(Code.MalformedId, registration.Id,
                    $"Malformed ID '{registration.Id}'; expected `area/rule-name` matching {RuleIdSyntax.Pattern}.",
                    registration.Location));

        var extendsScope = new HashSet<string>(StringComparer.Ordinal);
        foreach (RuleRegistration rule in rules)
        foreach (ScopeRegistration scope in scopes)
            if (RuleIdSyntax.ExtendsScope(rule.Id, scope.Id))
            {
                errors.Add(new SpecValidationError(Code.IdExtendsScope, rule.Id,
                    $"Rule ID '{rule.Id}' extends scope '{scope.Id}'; the '{scope.Id}/…' namespace is reserved for its generated children.",
                    rule.Location));
                extendsScope.Add(rule.Id);
            }

        // Duplicate detection over the post-desugar ID set (§8 item 1). IDs already flagged as
        // extends-scope are excluded so they are reported once, by the more specific code. Each post-desugar
        // ID carries the location of the anchor that contributes it, so the collision renders at the first
        // authored occurrence of the ID (GroupBy preserves source order).
        var postDesugar = new List<(string Id, SpecSourceLocation? Location)>();
        foreach (RuleRegistration rule in rules)
            if (!extendsScope.Contains(rule.Id))
                postDesugar.Add((rule.Id, rule.Location));

        // Both children are reserved for every scope, whatever its posture — a caution mints only the
        // tripwire, but reserving the containment ID too is what lets a caution be promoted to a quarantine
        // later without the ID it grows into having been taken meanwhile.
        foreach (ScopeRegistration scope in scopes)
        {
            postDesugar.Add((scope.Id + "/containment", scope.Location));
            postDesugar.Add((scope.Id + "/tripwire", scope.Location));
        }

        foreach (IGrouping<string, (string Id, SpecSourceLocation? Location)>? group in postDesugar.GroupBy(entry => entry.Id, StringComparer.Ordinal))
            if (group.Count() > 1)
                errors.Add(new SpecValidationError(Code.DuplicateId, group.Key,
                    $"Duplicate rule ID '{group.Key}'.", group.First().Location));
    }

    private static void ValidateRule(RuleRegistration rule, Arch arch, List<SpecValidationError> errors)
    {
        if (rule.Posture == null)
        {
            errors.Add(new SpecValidationError(Code.DanglingAnchor, rule.Id,
                $"Rule '{rule.Id}' has no posture; call .Enforce(...) or .Migrate(...).", rule.Location));
            return;
        }

        if (rule.PostureCount > 1)
            errors.Add(new SpecValidationError(Code.RepeatedPosture, rule.Id,
                $"Rule '{rule.Id}' has more than one posture; call .Enforce(...) or .Migrate(...) exactly once.", rule.Location));

        CheckBecause(rule.Becauses, rule.Id, rule.Location, errors);
        CheckRepeated(rule.Fixes.Count, "Fix", rule.Id, rule.Location, errors);
        CheckRepeated(rule.Baselines.Count, "Baseline", rule.Id, rule.Location, errors);
        CheckRepeated(rule.Policies.Count, "WhileYoureThere", rule.Id, rule.Location, errors);

        foreach ((string label, string? value) in RuleProse(rule))
            CheckProse(value, label, rule.Id, rule.Location, errors);

        CheckForeign(RuleSelections(rule), rule.Id, arch, rule.Location, errors);
        CheckForeignProjects(rule, arch, errors);
        CheckProjectPatterns(rule, errors);
        CheckTargetFrameworks(rule, errors);
        CheckCounterpartTemplate(rule, errors);
        CheckMembers(rule, arch, errors);
        CheckMemberReturning(rule, errors);
        CheckMemberAcceptParameter(rule, errors);
        CheckHierarchyAnchors(rule, errors);
        CheckPatterns(RulePatterns(rule), rule.Id, rule.Location, errors);
        CheckLifetimes(RuleSelections(rule), rule.Id, rule.Location, errors);
        CheckFamilies(RuleFamilyPositions(rule), rule.Id, rule.Location, errors);
        CheckEachOtherSubject(rule, errors);
        CheckCircularReferencesSubject(rule, errors);
    }

    private static void ValidateScope(ScopeRegistration scope, Arch arch, List<SpecValidationError> errors)
    {
        // The posture field, not the selection: both verbs set the two together, so either would answer, but
        // "which posture is this scope" is the question the rest of this method asks and the one item 2 reports.
        if (scope.Posture == null)
        {
            errors.Add(new SpecValidationError(Code.DanglingAnchor, scope.Id,
                $"Scope '{scope.Id}' has no posture; call .Quarantine(...) or .Caution(...).", scope.Location));
            return;
        }

        if (scope.PostureCount > 1)
            errors.Add(new SpecValidationError(Code.RepeatedPosture, scope.Id,
                $"Scope '{scope.Id}' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.",
                scope.Location));

        CheckBecause(scope.Becauses, scope.Id, scope.Location, errors);
        CheckRepeated(scope.Dragons.Count, "Dragons", scope.Id, scope.Location, errors);
        CheckRepeated(scope.DragonsDocs.Count, "DragonsDoc", scope.Id, scope.Location, errors);
        CheckRepeated(scope.Baselines.Count, "Baseline", scope.Id, scope.Location, errors);

        if (scope.BoundaryOnlyViaCount > 1)
            errors.Add(new SpecValidationError(Code.RepeatedTrailer, scope.Id, $"Repeated trailer 'BoundaryOnlyVia' on '{scope.Id}'.", scope.Location));
        else if (scope is { BoundaryOnlyViaCount: 1, Boundary.Count: 0 })
            errors.Add(new SpecValidationError(Code.EmptyBoundary, scope.Id,
                $"BoundaryOnlyVia() on '{scope.Id}' names no types; omit the call for a hermetic quarantine.", scope.Location));

        // The message names the posture the scope was declared under, because the reader's next move is to
        // find that verb in the spec and add the clause beneath it.
        if (scope.Dragons.Count == 0 && scope.DragonsDocs.Count == 0)
            errors.Add(new SpecValidationError(Code.MissingDragons, scope.Id,
                scope.Posture == Posture.Caution
                    ? $"Cautioned scope '{scope.Id}' is missing .Dragons(...) or .DragonsDoc(...)."
                    : $"Quarantined scope '{scope.Id}' is missing .Dragons(...) or .DragonsDoc(...).",
                scope.Location));

        foreach ((string label, string? value) in ScopeProse(scope))
            CheckProse(value, label, scope.Id, scope.Location, errors);

        CheckSelectionOperands(ScopeOperands(scope), scope.Id, arch, scope.Location, errors);
        CheckFamilies(ScopeFamilyPositions(scope), scope.Id, scope.Location, errors);
    }

    private static void CheckBecause(List<string> becauses, string id, SpecSourceLocation? location, List<SpecValidationError> errors)
    {
        if (becauses.Count == 0) errors.Add(new SpecValidationError(Code.MissingBecause, id, $"'{id}' is missing a required .Because(...).", location));

        CheckRepeated(becauses.Count, "Because", id, location, errors);
    }

    // A layer trailer reports spec-wide (null ID, no location) and names the layer as its subject; every
    // rule and scope trailer quotes its ID, which is why the subject is derived unless supplied.
    private static void CheckRepeated(
        int count, string trailer, string? id, SpecSourceLocation? location, List<SpecValidationError> errors,
        string? subject = null)
    {
        if (count > 1) errors.Add(new SpecValidationError(Code.RepeatedTrailer, id, $"Repeated trailer '{trailer}' on {SubjectOf(id, subject)}.", location));
    }

    private static void CheckProse(
        string? value, string label, string? id, SpecSourceLocation? location, List<SpecValidationError> errors,
        string? subject = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            ReportBlank(Code.BlankProse, label, id, location, errors, subject);
        else if (value!.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0) errors.Add(new SpecValidationError(Code.MultiLineProse, id, $"Multi-line {label} on {SubjectOf(id, subject)}; prose fields are single-line.", location));
    }

    // The walks every selection-bearing anchor takes — a layer's definition and a scope's operands alike —
    // in the order a rule's selections take them, so a bad selection reports the same first error wherever
    // it sits.
    private static void CheckSelectionOperands(
        IEnumerable<Selection> operands, string? id, Arch arch, SpecSourceLocation? location,
        List<SpecValidationError> errors, string? subject = null)
    {
        List<Selection> roots = operands.ToList();
        CheckForeign(roots.SelectMany(SelectionWalk.ExpandSelection), id, arch, location, errors, subject);
        CheckPatterns(roots.SelectMany(SelectionPatterns), id, location, errors, subject);
        CheckLifetimes(roots.SelectMany(SelectionWalk.ExpandSelection), id, location, errors, subject);
    }

    private static void CheckPatterns(
        IEnumerable<(string Value, PatternKind Kind)> patterns, string? id, SpecSourceLocation? location,
        List<SpecValidationError> errors, string? subject = null)
    {
        foreach ((string value, PatternKind kind) in patterns) CheckPattern(value, kind, id, location, errors, subject);
    }

    // GRAMMAR §8 items 15–16. A blank glob or affix is BlankPattern (the shared catalog-wide code, one
    // message shape for every kind); a non-blank namespace glob is additionally run through
    // NamespacePattern.Validate for the dead-subtree-prefix check. Name globs and affixes carry no
    // dot-segment structure, so only the blank check applies — the matcher has no subtree operator to
    // strand a wildcard behind. Blank is handled here first so every kind shares one code; the
    // namespace well-formedness verdict itself stays in NamespacePattern.Validate (its self-contained
    // blank branch backstops any direct caller). The trailing subject is the "on {subject}" text where the
    // quoted ID would be wrong — a layer glob, which is spec-wide (null ID, no location) and named by layer
    // instead.
    private static void CheckPattern(
        string value, PatternKind kind, string? id, SpecSourceLocation? location, List<SpecValidationError> errors,
        string? subject = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ReportBlank(Code.BlankPattern, kind.Label, id, location, errors, subject);
            return;
        }

        if (!kind.IsNamespace) return;

        string? reason = NamespacePattern.Validate(value);
        if (reason is not null)
            errors.Add(new SpecValidationError(Code.UnanchoredSubtreePattern, id, $"The {kind.Label} '{value}' on {SubjectOf(id, subject)} {reason}.", location));
    }

    // The one blank-operand sentence — "Blank {label} on {subject}." — every stratum's blank check renders.
    // The catalog parts them by CODE, not by wording: they fail in different directions and each entry has
    // to be able to say so, while a reader who has met one blank-operand error has met them all.
    private static void ReportBlank(
        Code code, string label, string? id, SpecSourceLocation? location, List<SpecValidationError> errors,
        string? subject = null)
    {
        errors.Add(new SpecValidationError(code, id, $"Blank {label} on {SubjectOf(id, subject)}.", location));
    }

    // The "on {subject}" text of a message: the quoted ID, unless the caller names the subject outright —
    // which a layer does, because a layer error is spec-wide, with no ID and no location to quote.
    private static string SubjectOf(string? id, string? subject)
    {
        return subject ?? $"'{id}'";
    }

    // GRAMMAR §8 item 27: a family may stand only as a rule subject. One code and one sentence shape for
    // every position, with the position word varying — what the author has to move is the family, and where
    // it stands is the whole of what they need told.
    private static void CheckFamilies(
        IEnumerable<(Selection Selection, string Position)> candidates, string? id, SpecSourceLocation? location,
        List<SpecValidationError> errors, string? subject = null)
    {
        foreach ((Selection selection, string position) in candidates)
        {
            if (selection is UnionSelection || selection.Noun is not EachNoun) continue;

            errors.Add(new SpecValidationError(Code.FamilyMisplaced, id,
                $"A family (`arch.Each`) used as {position} by {SubjectOf(id, subject)}; "
                + "a family may stand only as a rule subject.", location));
        }
    }

    // GRAMMAR §8 item 28: the cross-cell ban's targets are the subject's own cells, so a subject with no
    // cells names nothing to forbid. The message names the verb the author meant, because over a plain
    // selection there is one and it is not a rewrite of the rule.
    private static void CheckEachOtherSubject(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MustNotReferenceEachOtherConstraint constraint) return;

        Selection subject = constraint.Subject!;
        if (subject is not UnionSelection && subject.Noun is EachNoun) return;

        errors.Add(new SpecValidationError(Code.EachOtherWithoutFamily, rule.Id,
            $"`MustNotReferenceEachOther` on '{rule.Id}' needs a family subject (`arch.Each`); "
            + "over a plain selection write `MustNotReference`.", rule.Location));
    }

    // GRAMMAR §8 item 29: the cycle gate needs a family of LAYERS. A plain selection has no cells to be the
    // nodes of a cell graph; a family of projects has cells the build already forbids a circle among, so the
    // law would hold by construction and the rule could never red. Two wordings, because the two shapes are
    // wrong for different reasons and each has its own answer.
    private static void CheckCircularReferencesSubject(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MustNotHaveCircularReferencesConstraint constraint) return;

        Selection subject = constraint.Subject!;
        if (subject is UnionSelection || subject.Noun is not EachNoun family)
        {
            errors.Add(new SpecValidationError(Code.CircularReferencesNeedLayerFamily, rule.Id,
                $"`MustNotHaveCircularReferences` on '{rule.Id}' needs a family of layers (`arch.Each`); "
                + "over a plain selection there are no layers to reference each other.", rule.Location));
            return;
        }

        if (family.Layers is not null) return;

        errors.Add(new SpecValidationError(Code.CircularReferencesNeedLayerFamily, rule.Id,
            $"`MustNotHaveCircularReferences` on '{rule.Id}' needs a family of layers; projects cannot have "
            + "circular references, so over a family of projects the law holds by construction. Write "
            + "`MustNotReferenceEachOther` or an ordering rule.", rule.Location));
    }

    // Every position a rule's selections stand in, item 27's way: the subject itself is the one legal one,
    // so only what it NESTS is walked, while each operand is walked from its own root. A member constraint's
    // Subject is the type selection its projection was taken from, which is a subject too — a family as the
    // source of a member projection is legal.
    private static IEnumerable<(Selection Selection, string Position)> RuleFamilyPositions(RuleRegistration rule)
    {
        if (rule.Constraint is not { } constraint) yield break;

        if (constraint.Subject is { } subject)
            foreach ((Selection, string) nested in NestedFamilyPositions(subject))
                yield return nested;

        foreach (Selection operand in constraint.Operands)
        foreach ((Selection, string) position in FamilyPositions(operand, FamilyPosition.Operand))
            yield return position;
    }

    // The scope's twin: the quarantined interior and each sanctioned-surface operand, each named by the
    // position a reader would go looking for it in.
    private static IEnumerable<(Selection Selection, string Position)> ScopeFamilyPositions(ScopeRegistration scope)
    {
        // Non-null past ValidateScope's dangling-anchor return, as ScopeOperands relies on too.
        foreach ((Selection, string) position in FamilyPositions(scope.Scoped!, FamilyPosition.ScopedSelection))
            yield return position;

        foreach (Selection facade in scope.Boundary)
        foreach ((Selection, string) position in FamilyPositions(facade, FamilyPosition.Boundary))
            yield return position;
    }

    // One root and everything it nests, each paired with the position word item 27 names it by.
    private static IEnumerable<(Selection Selection, string Position)> FamilyPositions(Selection root, string position)
    {
        yield return (root, position);

        foreach ((Selection, string) nested in NestedFamilyPositions(root)) yield return nested;
    }

    // What a selection nests, and nothing else: its union parts and its Except payloads, each of which
    // consumes a set and so refuses a family. A family's own cells are Layers by construction, so there is
    // no cell arm — the type system already refused a nested family.
    private static IEnumerable<(Selection Selection, string Position)> NestedFamilyPositions(Selection root)
    {
        if (root is UnionSelection union)
            foreach (Selection part in union.Parts)
            foreach ((Selection, string) nested in FamilyPositions(part, FamilyPosition.UnionOperand))
                yield return nested;

        foreach (SelectionAdjective adjective in root.Adjectives)
            if (adjective is ExceptAdjective except)
                foreach ((Selection, string) nested in FamilyPositions(except.Payload, FamilyPosition.ExceptPayload))
                    yield return nested;
    }

    /// <summary>The position words item 27's one sentence varies on (GRAMMAR §8).</summary>
    private static class FamilyPosition
    {
        internal const string Operand = "an operand";
        internal const string ExceptPayload = "an Except payload";
        internal const string UnionOperand = "a union operand";
        internal const string LayerDefinition = "a layer definition";
        internal const string ScopedSelection = "a scoped selection";
        internal const string Boundary = "a boundary";
    }

    // The foreign-Arch walk the three strata share (GRAMMAR §8 items 4, 13, 22): the first candidate some
    // other Arch minted is reported and the walk stops, because a spec assembled from two instances is one
    // mistake however many of its parts carry it. Each stratum supplies the noun its sentence opens with,
    // the code the catalog files it under, and where each candidate would land — a member steers to its own
    // arch.Member(...) call site, every other stratum to the consuming anchor's. Returns whether anything
    // was reported, which is what lets a caller skip the per-item checks a foreign owner makes meaningless.
    private static bool ReportFirstForeign(
        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates, Arch arch, Code code, string noun,
        string? id, List<SpecValidationError> errors, string? subject = null)
    {
        foreach ((Arch owner, SpecSourceLocation? location) in candidates)
        {
            if (ReferenceEquals(owner, arch)) continue;

            errors.Add(new SpecValidationError(code, id,
                $"{noun} used by {SubjectOf(id, subject)} was minted on a different Arch instance; it is not registered with this model.",
                location));
            return true;
        }

        return false;
    }

    private static void CheckForeign(
        IEnumerable<Selection> selections, string? id, Arch arch, SpecSourceLocation? location,
        List<SpecValidationError> errors, string? subject = null)
    {
        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates =
            selections.Select(selection => (selection.Owner, Location: location));

        ReportFirstForeign(candidates, arch, Code.ForeignSelection, "A selection", id, errors, subject);
    }

    // GRAMMAR §8 item 22: the project-stratum sibling of CheckForeign. A project selection carries its own
    // Arch (it has no underlying type selection to borrow one from), so it needs its own walk — the type-side
    // one reaches nothing on a project constraint, whose Subject is null. The walk includes the Except
    // payloads, which is the one way a project selection nests and therefore the one way a foreign one can
    // hide inside a local subject.
    private static void CheckForeignProjects(RuleRegistration rule, Arch arch, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        IEnumerable<ProjectSelection> selections = SelectionWalk.ConstraintProjectSelections(rule.Constraint);
        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates =
            selections.Select(selection => (selection.Owner, rule.Location));

        ReportFirstForeign(candidates, arch, Code.ForeignProjectSelection, "A project selection", rule.Id, errors);
    }

    // GRAMMAR §8 item 23: the blank check over a project subject's own operands — the .Named names and the
    // .Matching globs. Its own code rather than the shared BlankPattern because the two shapes fail in
    // opposite directions (a blank name matches nothing, a blank glob matches everything) and the catalog
    // entry has to be able to say so. The labels come off PatternKind all the same, so the noun a project
    // operand is named by cannot drift from the noun the type-side walk names the same thing by.
    private static void CheckProjectPatterns(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        foreach (ProjectSelection selection in SelectionWalk.ConstraintProjectSelections(rule.Constraint))
        foreach (ProjectAdjective adjective in selection.Adjectives)
            switch (adjective)
            {
                case ProjectNamedAdjective named:
                    foreach (string name in named.Names) CheckProjectPattern(name, PatternKind.ProjectName, rule, errors);

                    break;
                case ProjectMatchingAdjective matching:
                    foreach (string glob in matching.Globs) CheckProjectPattern(glob, PatternKind.ProjectNamePattern, rule, errors);

                    break;
            }
    }

    private static void CheckProjectPattern(
        string value, PatternKind kind, RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value)) return;

        ReportBlank(Code.BlankProjectPattern, kind.Label, rule.Id, rule.Location, errors);
    }

    // GRAMMAR §8 item 24: MustOnlyTarget's own operands. A blank moniker matches nothing, so it narrows the
    // allow-list silently — the rule stays green until a project targets the framework the author meant to
    // permit, which is exactly the class of slip the catalog exists to catch at build.
    private static void CheckTargetFrameworks(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MustOnlyTargetConstraint target) return;

        foreach (string framework in target.Frameworks)
            if (string.IsNullOrWhiteSpace(framework))
                ReportBlank(Code.BlankTargetFramework, "target framework", rule.Id, rule.Location, errors);
    }

    // GRAMMAR §8 item 26: MustHaveExactlyOneCounterpart's name template. A template with no {Name} in it is
    // a constant predicate — every subject derives the same fixed name, so the rule is a cardinality law
    // wearing a correspondence law's clothes, and it reds or greens the whole subject set together. The
    // match is ordinal, which is exactly what makes the '{name}' typo fail here rather than at check time.
    // A blank template is item 25's, which fires first and says something more specific.
    private static void CheckCounterpartTemplate(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MustHaveExactlyOneCounterpartConstraint counterpart) return;

        string template = counterpart.Template;
        if (string.IsNullOrWhiteSpace(template)) return;
        if (template.IndexOf("{Name}", StringComparison.Ordinal) >= 0) return;

        errors.Add(new SpecValidationError(Code.CounterpartTemplateWithoutPlaceholder, rule.Id,
            $"The counterpart name template '{template}' on '{rule.Id}' contains no '{{Name}}' placeholder, so every "
            + "subject derives the same fixed name — a cardinality claim, not a correspondence; use a template such "
            + "as 'I{Name}' (substitution is case-sensitive).", rule.Location));
    }

    // GRAMMAR §8 items 11–13: the member-access verb's operands. A foreign member is reported once per
    // rule and stops the pass; otherwise each member is checked for a blank name and then, when named,
    // that its anchor declares it. A member error renders at the member's own arch.Member(...) call site
    // when it has one, falling back to the consuming rule's anchor for a verb-minted member (which carries
    // no location — GRAMMAR §8, items 11–13/18).
    private static void CheckMembers(RuleRegistration rule, Arch arch, List<SpecValidationError> errors)
    {
        IReadOnlyList<Member> members = rule.Constraint?.MemberOperands ?? Array.Empty<Member>();
        if (members.Count == 0) return;

        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates =
            members.Select(member => (member.Owner, Location: member.Location ?? rule.Location));
        if (ReportFirstForeign(candidates, arch, Code.ForeignMember, "A member", rule.Id, errors)) return;

        foreach (Member member in members) CheckMember(member, rule.Id, rule.Location, errors);
    }

    // GRAMMAR §8 item 14: a member `.Returning` anchor is definition-level, so a closed-generic anchor
    // (typeof(Task<int>)) is refused with guidance to the open definition (typeof(Task<>)). A non-generic
    // or open-generic anchor is accepted; only a MemberConstraint carries a ReturningAdjective at all. The
    // anchor is the consuming rule's statement, so it renders at the rule's location.
    private static void CheckMemberReturning(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MemberConstraint memberConstraint) return;

        foreach (MemberAdjective adjective in memberConstraint.MemberSubject.Adjectives)
            if (adjective is ReturningAdjective returning)
                foreach (Type type in returning.Types)
                    if (Generics.IsConstructed(type))
                        errors.Add(new SpecValidationError(Code.MemberReturningClosedGeneric, rule.Id,
                            $"'{SafeFullDisplay(type)}' is a closed generic; .Returning matches definition-level — " +
                            $"use typeof({TypeofForm(Generics.Definition(type))}) (used by '{rule.Id}').", rule.Location));
    }

    // GRAMMAR §8 item 20: a MustAcceptParameter anchor is definition-level, so a closed-generic anchor
    // (typeof(IProgress<int>)) is refused with guidance to the open definition (typeof(IProgress<>)) — the
    // sibling of the item-14 .Returning refusal, with the verb named in the steer. A non-generic or
    // open-generic anchor is accepted; only MemberMustAcceptParameterConstraint carries the ParameterType. The
    // anchor is the consuming rule's statement, so it renders at the rule's location.
    private static void CheckMemberAcceptParameter(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MemberMustAcceptParameterConstraint accept) return;

        if (Generics.IsConstructed(accept.ParameterType))
            errors.Add(new SpecValidationError(Code.MemberAcceptParameterClosedGeneric, rule.Id,
                $"'{SafeFullDisplay(accept.ParameterType)}' is a closed generic; MustAcceptParameter matches definition-level — " +
                $"use typeof({TypeofForm(Generics.Definition(accept.ParameterType))}) (used by '{rule.Id}').", rule.Location));
    }

    // GRAMMAR §8 item 21, both polarities — Code.HierarchyAnchorWrongCategory carries the category rule per
    // verb and why a wrong-category anchor is a silent slip.
    // A string anchor carries no reflected type, so it is filtered out (TypedAnchors) rather than guessed
    // at — in every family, hierarchy and attribute alike: there is no category to read off an FQN,
    // extraction facts are the only authority on what a name names, and refusing a spelling the host cannot
    // load would break the escape hatch's whole point. The cost is stated in the catalog: a wrong string is
    // loud on a positive (always red) and silent on a negative.
    private static void CheckHierarchyAnchors(RuleRegistration rule, List<SpecValidationError> errors)
    {
        switch (rule.Constraint)
        {
            case MustImplementConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors([c.Anchor]), t => !t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is not an interface; MustImplement requires an interface anchor — use MustDeriveFrom for a base class");
                break;
            case MustNotImplementConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors(c.Anchors), t => !t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is not an interface; MustNotImplement requires an interface anchor — use MustNotDeriveFrom for a base class");
                break;
            case MustDeriveFromConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors([c.Anchor]), t => t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is an interface; MustDeriveFrom requires a non-interface anchor — use MustImplement for an interface");
                break;
            case MustNotDeriveFromConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors(c.Anchors), t => t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is an interface; MustNotDeriveFrom requires a non-interface anchor — use MustNotImplement for an interface");
                break;
            case MustBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, [c.Anchor], "MustBeAttributedWith");
                break;
            case MustNotBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, c.Anchors, "MustNotBeAttributedWith");
                break;

            // The member attribute verbs (GRAMMAR §5.7) carry the identical category rule and, through
            // CheckAttributeAnchors, the identical message — the verb name in the steer is the same word on
            // either side, so a member author reads the same sentence a type author does. Only the VERBS are
            // checked — a wrong-category member attribute ADJECTIVE stays unchecked, exactly like its
            // type-side twin: it empties the subject, which the fail-on-empty gate reds loudly, whereas the
            // always-passing MustNot verb is the silent slip this item exists to catch.
            case MemberMustBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, [c.Anchor], "MustBeAttributedWith");
                break;
            case MemberMustNotBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, c.Anchors, "MustNotBeAttributedWith");
                break;
        }
    }

    // The attribute-anchor half of item 21, shared by the four attribute verbs (type and member, both
    // polarities): one predicate and one message template with the caller's verb name in the steer.
    private static void CheckAttributeAnchors(
        RuleRegistration rule, List<SpecValidationError> errors, IReadOnlyList<TypeAnchor> anchors, string verbName)
    {
        CheckAnchorCategory(rule, errors, TypedAnchors(anchors), t => !t.IsSubclassOf(typeof(Attribute)),
            t => $"'{SafeFullDisplay(t)}' does not derive from System.Attribute; {verbName} requires an attribute anchor");
    }

    // Walks a hierarchy verb's anchors (one for a positive, the whole list for a negative), emitting the
    // shared Code.HierarchyAnchorWrongCategory with the caller's message core plus the "(used by '{id}')" tail
    // and the rule's spec-source location — the items-19/20 convention, no new location plumbing.
    private static void CheckAnchorCategory(
        RuleRegistration rule, List<SpecValidationError> errors, IReadOnlyList<Type> anchors,
        Func<Type, bool> invalid, Func<Type, string> message)
    {
        foreach (Type anchor in anchors)
            if (invalid(anchor))
                errors.Add(new SpecValidationError(Code.HierarchyAnchorWrongCategory, rule.Id,
                    $"{message(anchor)} (used by '{rule.Id}').", rule.Location));
    }

    // The reflected arm of an anchor list — the only anchors item 21 can judge. A string anchor names a
    // definition FQN and nothing more, so it is dropped here; the asymmetry is deliberate, and the
    // blank-name check (item 15, via RulePatterns) is the whole of what a string anchor is validated for.
    private static IReadOnlyList<Type> TypedAnchors(IReadOnlyList<TypeAnchor> anchors)
    {
        return anchors.Where(anchor => anchor.Type is not null).Select(anchor => anchor.Type!).ToList();
    }

    // GRAMMAR §8 item 19: an arch.Registered noun carrying a Lifetime value outside the defined set (e.g.
    // (Lifetime)7 via a cast) names no lifetime. Walked over every selection an anchor reaches — a rule's
    // subject, operands, Except payloads and union parts, a scope's scoped interior and sanctioned
    // surface — through the same expansion the foreign-Arch and pattern walks take, so a noun cannot be
    // checked in one position and unchecked in another. The union guard mirrors SelectionPatterns (a
    // UnionSelection has no single noun). Reported all-at-once, and the build throws before membership
    // resolution ever sees the bad value.
    private static void CheckLifetimes(
        IEnumerable<Selection> selections, string? id, SpecSourceLocation? location,
        List<SpecValidationError> errors, string? subject = null)
    {
        foreach (Selection selection in selections)
            if (selection is not UnionSelection && selection.Noun is RegisteredNoun { Lifetime: { } lifetime }
                                                && !Enum.IsDefined(typeof(Lifetime), lifetime))
                errors.Add(new SpecValidationError(Code.UndefinedLifetime, id,
                    $"'(Lifetime){(int)lifetime}' is not a defined Lifetime — " +
                    $"use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by {SubjectOf(id, subject)}).",
                    location));
    }

    private static void CheckMember(Member member, string id, SpecSourceLocation? ruleLocation, List<SpecValidationError> errors)
    {
        // The member's own arch.Member(...) call site steers the diagnostic when present; a verb-minted
        // member has none, so it attributes to the consuming rule's anchor.
        SpecSourceLocation? location = member.Location ?? ruleLocation;

        // A member minted from an unresolvable anchor expression (GRAMMAR §8, item 12's expression sibling):
        // the resolver stored the diagnostic core; report it before touching the anchor. A poisoned Member's
        // anchor accessors throw if read (fail closed), so this short-circuit is the only sanctioned path.
        if (member.PoisonError is not null)
        {
            errors.Add(new SpecValidationError(Code.MemberExpressionUnresolvable, id, $"{member.PoisonError} (used by '{id}').", location));
            return;
        }

        string display = SafeFullDisplay(member.DeclaringType);

        if (string.IsNullOrWhiteSpace(member.Name))
        {
            errors.Add(new SpecValidationError(Code.BlankMemberName, id,
                $"Blank member name on a member of '{display}' (used by '{id}').", location));
            return;
        }

        Type anchor = Generics.Definition(member.DeclaringType);
        if (Declares(anchor, member.Name)) return;

        Type? declaringBase = FindDeclaringBase(anchor, member.Name);
        if (declaringBase != null)
        {
            errors.Add(new SpecValidationError(Code.MemberNotDeclared, id,
                $"'{display}' does not declare '{member.Name}'; it is declared on base type '{SafeFullDisplay(declaringBase)}' — " +
                $"use typeof({TypeofForm(declaringBase)}) (used by '{id}').", location));
            return;
        }

        errors.Add(new SpecValidationError(Code.MemberNotDeclared, id,
            $"'{display}' does not declare a member named '{member.Name}' (used by '{id}').", location));
    }

    // The first base type / interface (each normalized to its generic definition) that declares the
    // name, or null when nothing in the hierarchy declares it — GRAMMAR §8 item 12's base-type guidance.
    private static Type? FindDeclaringBase(Type anchor, string name)
    {
        if (anchor.IsInterface)
        {
            foreach (Type contract in anchor.GetInterfaces())
            {
                Type normalized = Generics.Definition(contract);
                if (Declares(normalized, name)) return normalized;
            }

            return null;
        }

        for (Type? baseType = anchor.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            Type normalized = Generics.Definition(baseType);
            if (Declares(normalized, name)) return normalized;
        }

        return null;
    }

    private static bool Declares(Type type, string name)
    {
        return DeclaredMember.Of(type, name).Length > 0;
    }

    // The C#-writable typeof operand for a (normalized) type: non-generic → bare name (`Task`); generic
    // definition → name without arity plus empty type-argument brackets (`HandlerBase<>`, `Foo<,>`).
    private static string TypeofForm(Type type)
    {
        string bare = TypeName.StripArity(type.Name);
        if (!type.IsGenericType) return bare;

        int arity = type.GetGenericArguments().Length;
        return bare + "<" + new string(',', arity - 1) + ">";
    }

    // FullDisplay of the authored type; a pointer/by-ref/partially-open anchor has no source form, so
    // fall back to Type.ToString() rather than crash validation (GRAMMAR §8 item 12).
    private static string SafeFullDisplay(Type type)
    {
        try
        {
            return TypeName.FullDisplay(type);
        }
        catch (UnrepresentableTypeException)
        {
            return type.ToString();
        }
    }

    private static IEnumerable<(string Label, string? Value)> RuleProse(RuleRegistration rule)
    {
        foreach (string because in rule.Becauses) yield return ("Because", because);

        foreach (string fix in rule.Fixes) yield return ("Fix", fix);

        if (rule.Posture == Posture.Migrate) yield return ("Migrate from", rule.MigrateFrom);

        if (rule.Constraint != null)
            foreach ((string, string?) prose in ConstraintProse(rule.Constraint))
                yield return prose;
    }

    private static IEnumerable<(string Label, string? Value)> ScopeProse(ScopeRegistration scope)
    {
        foreach (string because in scope.Becauses) yield return ("Because", because);

        foreach (string dragons in scope.Dragons) yield return ("Dragons", dragons);

        foreach (string dragonsDoc in scope.DragonsDocs) yield return ("DragonsDoc", dragonsDoc);

        // Non-null past ValidateScope's dangling-anchor return: the posture verbs set the selection with the
        // posture.
        foreach ((string, string?) prose in SelectionProse(scope.Scoped!))
            yield return prose;
    }

    private static IEnumerable<(string Label, string? Value)> ConstraintProse(Constraint constraint)
    {
        if (constraint is MustConstraint must) yield return ("description", must.Description);

        // The member escape hatches (GRAMMAR §4.6, §8 item 5 via the extended walk): the member Must
        // description and any member Where descriptions on the member subject reach BlankProse/MultiLineProse.
        if (constraint is MemberMustConstraint memberMust) yield return ("description", memberMust.Description);

        if (constraint is MemberConstraint memberConstraint)
            foreach (MemberAdjective adjective in memberConstraint.MemberSubject.Adjectives)
                if (adjective is MemberWhereAdjective memberWhere)
                    yield return ("description", memberWhere.Description);

        // The project escape hatches (GRAMMAR §4.10, §8 item 5 via the same extended walk): the project Must
        // description and any project Where descriptions on the project subject, including those nested under
        // its Except payloads.
        if (constraint is ProjectMustConstraint projectMust) yield return ("description", projectMust.Description);

        foreach (ProjectSelection selection in SelectionWalk.ConstraintProjectSelections(constraint))
        foreach (ProjectAdjective adjective in selection.Adjectives)
            if (adjective is ProjectWhereAdjective projectWhere)
                yield return ("description", projectWhere.Description);

        // For a member constraint, Subject is the underlying type selection (Subject => MemberSubject.Source),
        // so this also walks any type-side Where/Except used before the projection. Null for a project
        // constraint, whose subject is not a type selection at all — the loop above is its walk.
        if (constraint.Subject is { } subject)
            foreach ((string, string?) prose in SelectionProse(subject))
                yield return prose;

        foreach (Selection operand in constraint.Operands)
        foreach ((string, string?) prose in SelectionProse(operand))
            yield return prose;
    }

    private static IEnumerable<(string Label, string? Value)> SelectionProse(Selection selection)
    {
        // A union's operands answer for its noun; its own adjective list is walked after, like any other
        // selection's — AnyOf(a, b).Where(p, "") must still reach the blank-prose check (§8 item 5).
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach ((string, string?) prose in SelectionProse(member))
                yield return prose;

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is WhereAdjective where)
                yield return ("description", where.Description);
            else if (adjective is ExceptAdjective except)
                foreach ((string, string?) prose in SelectionProse(except.Payload))
                    yield return prose;
    }

    private static IEnumerable<Selection> RuleSelections(RuleRegistration rule)
    {
        return rule.Constraint == null ? Enumerable.Empty<Selection>() : SelectionWalk.ConstraintSelections(rule.Constraint);
    }

    // The glob/affix walk (GRAMMAR §8 items 15–16), parallel to the prose and selection walks: every
    // glob and affix a rule carries, each tagged with the PatternKind that names it in the error and says
    // whether the full structural check applies (namespace globs) or only the blank check (every other
    // kind). A string attribute anchor rides this walk too — blank is the only well-formedness a
    // definition FQN has, so it joins the plain kinds under its own label.
    private static IEnumerable<(string Value, PatternKind Kind)> RulePatterns(RuleRegistration rule)
    {
        return rule.Constraint == null
            ? Enumerable.Empty<(string, PatternKind)>()
            : ConstraintPatterns(rule.Constraint);
    }

    // A scope's selections are the scoped interior and every operand of its sanctioned surface: the
    // boundary is spec-authored selection like any other, so the foreign-Arch, lifetime and pattern walks
    // must reach it. The desugared containment rule expands the same selections, but a scope whose spec is
    // invalid never reaches desugaring, so the error has to be found here.
    private static IEnumerable<Selection> ScopeOperands(ScopeRegistration scope)
    {
        // Non-null past ValidateScope's dangling-anchor return: the posture verbs set the selection with the
        // posture.
        yield return scope.Scoped!;

        foreach (Selection facade in scope.Boundary) yield return facade;
    }

    private static IEnumerable<(string Value, PatternKind Kind)> ConstraintPatterns(Constraint constraint)
    {
        // The verb's own glob/affix (the shape/naming verbs carry one; dependency and boolean verbs none).
        switch (constraint)
        {
            case MustResideInNamespaceConstraint c:
                yield return (c.Glob, PatternKind.NamespacePattern);
                break;
            case MustResideInProjectConstraint c:
                yield return (c.ProjectName, PatternKind.ProjectName);
                break;
            case MustHaveNameMatchingConstraint c:
                yield return (c.Glob, PatternKind.NamePattern);
                break;
            case MustHaveSuffixConstraint c:
                yield return (c.Suffix, PatternKind.Suffix);
                break;
            case MustHavePrefixConstraint c:
                yield return (c.Prefix, PatternKind.Prefix);
                break;
            case MustHaveExactlyOneCounterpartConstraint c:
                yield return (c.Template, PatternKind.CounterpartTemplate);
                break;
            case MemberMustHaveNameMatchingConstraint c:
                yield return (c.Glob, PatternKind.MemberNamePattern);
                break;
            case MemberMustHaveSuffixConstraint c:
                yield return (c.Suffix, PatternKind.MemberSuffix);
                break;
            case MemberMustHavePrefixConstraint c:
                yield return (c.Prefix, PatternKind.MemberPrefix);
                break;

            // The attribute verbs' string anchors (GRAMMAR §8 item 15, the "attribute name" label): a blank
            // name is a slip that would otherwise mint an anchor matching nothing, silently. Only the string
            // arm has a name to check; a typeof anchor yields nothing here.
            case MustBeAttributedWithConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns([c.Anchor], PatternKind.AttributeName)) yield return pattern;
                break;
            case MustNotBeAttributedWithConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns(c.Anchors, PatternKind.AttributeName)) yield return pattern;
                break;

            // The hierarchy verbs' string anchors, on the same item-15 terms under their own labels, so the
            // error names which kind of anchor was left blank ("Blank interface name on 'rule/id'.").
            case MustImplementConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns([c.Anchor], PatternKind.InterfaceName)) yield return pattern;
                break;
            case MustNotImplementConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns(c.Anchors, PatternKind.InterfaceName)) yield return pattern;
                break;
            case MustDeriveFromConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns([c.Anchor], PatternKind.BaseTypeName)) yield return pattern;
                break;
            case MustNotDeriveFromConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns(c.Anchors, PatternKind.BaseTypeName)) yield return pattern;
                break;

            // The member attribute verbs' string anchors, on the same terms and under the same label — blank
            // is the whole of a definition FQN's well-formedness on either side of the axis (item 15).
            case MemberMustBeAttributedWithConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns([c.Anchor], PatternKind.AttributeName)) yield return pattern;
                break;
            case MemberMustNotBeAttributedWithConstraint c:
                foreach ((string, PatternKind) pattern in AnchorNamePatterns(c.Anchors, PatternKind.AttributeName)) yield return pattern;
                break;
        }

        // The subject selection tree (for a member constraint this is the underlying type selection,
        // Subject => MemberSubject.Source) and the dependency-verb operands. A project constraint has no type
        // selection here; its own operands ride CheckProjectPatterns and CheckTargetFrameworks (items 23–24),
        // which need their own codes rather than this walk's shared BlankPattern.
        if (constraint.Subject is { } subject)
            foreach ((string, PatternKind) pattern in SelectionPatterns(subject))
                yield return pattern;

        foreach (Selection operand in constraint.Operands)
        foreach ((string, PatternKind) pattern in SelectionPatterns(operand))
            yield return pattern;

        // The member subject's own adjectives (name/affix) — off the type-side selection walk, reached
        // like CheckMemberReturning reaches the member .Returning anchor.
        if (constraint is MemberConstraint memberConstraint)
            foreach (MemberAdjective adjective in memberConstraint.MemberSubject.Adjectives)
            foreach ((string, PatternKind) pattern in MemberAdjectivePatterns(adjective))
                yield return pattern;
    }

    // The string arm of an anchor list, under one kind: an anchor spelled as a definition FQN yields its
    // name for the item-15 blank check, and a typeof anchor yields nothing. One projection for both anchor
    // shapes — the single-anchor verbs pass a one-element list — so every arm above stays one line.
    private static IEnumerable<(string Value, PatternKind Kind)> AnchorNamePatterns(IReadOnlyList<TypeAnchor> anchors, PatternKind kind)
    {
        foreach (TypeAnchor anchor in anchors)
            if (anchor.DefinitionFullName is { } name)
                yield return (name, kind);
    }

    private static IEnumerable<(string Value, PatternKind Kind)> SelectionPatterns(Selection selection)
    {
        // A NamespaceNoun carries a glob and a ProjectNoun a name, both checked here — this one arm covers
        // every position a noun can stand in (subject, operand, Except payload, quarantined scope), because
        // each position reaches this walk. A LayerNoun's globs are validated once in ValidateLayers (their
        // use-independent home), so they are not re-checked here. A UnionSelection has no single noun, so
        // its operands answer for it — and either way the adjective loop below runs, because a union
        // carries adjectives of its own (AnyOf(a, b).InNamespace("") must reach the blank-pattern check).
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach ((string, PatternKind) pattern in SelectionPatterns(member))
                yield return pattern;
        else if (selection.Noun is NamespaceNoun ns) yield return (ns.Glob, PatternKind.NamespacePattern);
        else if (selection.Noun is ProjectNoun project) yield return (project.Name, PatternKind.ProjectName);

        foreach (SelectionAdjective adjective in selection.Adjectives)
            switch (adjective)
            {
                case InNamespaceAdjective a:
                    yield return (a.Glob, PatternKind.NamespacePattern);
                    break;
                case WithNameMatchingAdjective a:
                    yield return (a.Glob, PatternKind.NamePattern);
                    break;
                case NamedAdjective a:
                    foreach (string name in a.Names) yield return (name, PatternKind.ExactTypeName);

                    break;
                case WithSuffixAdjective a:
                    yield return (a.Suffix, PatternKind.Suffix);
                    break;
                case WithPrefixAdjective a:
                    yield return (a.Prefix, PatternKind.Prefix);
                    break;
                case AttributedWithAdjective { Anchor.DefinitionFullName: { } attributeName }:
                    // The adjective's string anchor, on the same terms as the two verbs' (item 15).
                    yield return (attributeName, PatternKind.AttributeName);
                    break;
                case ImplementingAdjective { Anchor.DefinitionFullName: { } interfaceName }:
                    // The hierarchy adjectives' string anchors, likewise. Blankness is checked on an
                    // adjective even though item 21's category rule deliberately is not: a blank name is a
                    // spec-side slip either way, while a wrong CATEGORY on an adjective empties the subject
                    // and the fail-on-empty gate reds it loudly (see CheckHierarchyAnchors).
                    yield return (interfaceName, PatternKind.InterfaceName);
                    break;
                case DerivedFromAdjective { Anchor.DefinitionFullName: { } baseTypeName }:
                    yield return (baseTypeName, PatternKind.BaseTypeName);
                    break;
                case ExceptAdjective a:
                    foreach ((string, PatternKind) pattern in SelectionPatterns(a.Payload)) yield return pattern;
                    break;
            }
    }

    private static IEnumerable<(string Value, PatternKind Kind)> MemberAdjectivePatterns(MemberAdjective adjective)
    {
        switch (adjective)
        {
            case MemberWithNameMatchingAdjective a:
                yield return (a.Glob, PatternKind.MemberNamePattern);
                break;
            case MemberWithSuffixAdjective a:
                yield return (a.Suffix, PatternKind.MemberSuffix);
                break;
            case MemberWithPrefixAdjective a:
                yield return (a.Prefix, PatternKind.MemberPrefix);
                break;
            case MemberAttributedWithAdjective { Anchor.DefinitionFullName: { } attributeName }:
                // The member attribute adjective's string anchor (item 15). The category check deliberately
                // does not reach an adjective on either axis — see CheckHierarchyAnchors.
                yield return (attributeName, PatternKind.AttributeName);
                break;
        }
    }

    /// <summary>
    ///     What a glob, affix, project name or string anchor is: the label the error names it by ("Blank
    ///     interface name on 'rule/id'.") and whether it carries namespace structure, which is what decides
    ///     between the blank check alone and the full dead-subtree-prefix check.
    /// </summary>
    /// <remarks>
    ///     One instance per kind, so each label literal is written once and the two facts about a kind
    ///     cannot travel apart. The pattern walk is the main reader, but the project stratum's own blank
    ///     check reads labels from here too — its operands ride a separate walk under a separate code, and
    ///     the noun in the sentence is the one thing that must not fork with them.
    /// </remarks>
    private sealed class PatternKind(string label, bool isNamespace)
    {
        internal static readonly PatternKind NamespacePattern = new("namespace pattern", true);
        internal static readonly PatternKind NamePattern = new("name pattern", false);
        internal static readonly PatternKind ExactTypeName = new("type name", false);
        internal static readonly PatternKind Suffix = new("suffix", false);
        internal static readonly PatternKind Prefix = new("prefix", false);
        internal static readonly PatternKind MemberNamePattern = new("member name pattern", false);
        internal static readonly PatternKind MemberSuffix = new("member suffix", false);
        internal static readonly PatternKind MemberPrefix = new("member prefix", false);
        internal static readonly PatternKind AttributeName = new("attribute name", false);
        internal static readonly PatternKind InterfaceName = new("interface name", false);
        internal static readonly PatternKind BaseTypeName = new("base type name", false);
        internal static readonly PatternKind CounterpartTemplate = new("counterpart name template", false);
        internal static readonly PatternKind ProjectName = new("project name", false);
        internal static readonly PatternKind ProjectNamePattern = new("project name pattern", false);

        /// <summary>The noun the BlankPattern / UnanchoredSubtreePattern messages name this kind by.</summary>
        internal string Label { get; } = label;

        /// <summary>Whether the value is a namespace glob, and so subject to the structural check too.</summary>
        internal bool IsNamespace { get; } = isNamespace;
    }
}
