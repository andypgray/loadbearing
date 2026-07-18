using System.Reflection;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;
using Code = Zphil.LoadBearing.Validation.SpecValidationErrorCode;

namespace Zphil.LoadBearing.Validation;

/// <summary>
///     Runs the whole GRAMMAR §8 catalog and collects <em>every</em> error in one pass (a
///     deliberate divergence from EF Core's fail-fast validator). ID checks run over the
///     post-desugar ID set; authored-field checks run over the original registrations, keyed to
///     the rule or scope the author wrote.
/// </summary>
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
        foreach (LayerNoun layer in arch.Layers)
            if (!seen.Add(layer.Name))
                errors.Add(new SpecValidationError(Code.DuplicateLayerName, null, $"Duplicate layer name '{layer.Name}'."));
    }

    private static void ValidateIds(Arch arch, List<SpecValidationError> errors)
    {
        var ruleIds = arch.Registrations.OfType<RuleRegistration>().Select(r => r.Id).ToList();
        var scopeIds = arch.Registrations.OfType<ScopeRegistration>().Select(s => s.Id).ToList();

        foreach (string id in ruleIds.Concat(scopeIds))
            if (!RuleIdSyntax.IsValid(id))
                errors.Add(new SpecValidationError(Code.MalformedId, id,
                    $"Malformed ID '{id}'; expected `area/rule-name` matching {RuleIdSyntax.Pattern}."));

        var extendsScope = new HashSet<string>(StringComparer.Ordinal);
        foreach (string ruleId in ruleIds)
        foreach (string scopeId in scopeIds)
            if (RuleIdSyntax.ExtendsScope(ruleId, scopeId))
            {
                errors.Add(new SpecValidationError(Code.IdExtendsScope, ruleId,
                    $"Rule ID '{ruleId}' extends scope '{scopeId}'; the '{scopeId}/…' namespace is reserved for its generated children."));
                extendsScope.Add(ruleId);
            }

        // Duplicate detection over the post-desugar ID set (§8 item 1). IDs already flagged as
        // extends-scope are excluded so they are reported once, by the more specific code.
        var postDesugar = new List<string>(ruleIds.Where(id => !extendsScope.Contains(id)));
        foreach (string scopeId in scopeIds)
        {
            postDesugar.Add(scopeId + "/containment");
            postDesugar.Add(scopeId + "/tripwire");
        }

        foreach (var group in postDesugar.GroupBy(id => id, StringComparer.Ordinal))
            if (group.Count() > 1)
                errors.Add(new SpecValidationError(Code.DuplicateId, group.Key, $"Duplicate rule ID '{group.Key}'."));
    }

    private static void ValidateRule(RuleRegistration rule, Arch arch, List<SpecValidationError> errors)
    {
        if (rule.Posture == null)
        {
            errors.Add(new SpecValidationError(Code.DanglingAnchor, rule.Id,
                $"Rule '{rule.Id}' has no posture; call .Enforce(...) or .Migrate(...)."));
            return;
        }

        CheckBecause(rule.Becauses, rule.Id, errors);
        CheckRepeated(rule.Fixes.Count, "Fix", rule.Id, errors);
        CheckRepeated(rule.Baselines.Count, "Baseline", rule.Id, errors);
        CheckRepeated(rule.Policies.Count, "WhileYoureThere", rule.Id, errors);

        foreach ((string label, string? value) in RuleProse(rule)) CheckProse(value, label, rule.Id, errors);

        CheckForeign(RuleSelections(rule), rule.Id, arch, errors);
        CheckMembers(rule, arch, errors);
        CheckMemberReturning(rule, errors);
    }

    private static void ValidateScope(ScopeRegistration scope, Arch arch, List<SpecValidationError> errors)
    {
        if (scope.Frozen == null)
        {
            errors.Add(new SpecValidationError(Code.DanglingAnchor, scope.Id,
                $"Scope '{scope.Id}' has no posture; call .Freeze(...)."));
            return;
        }

        CheckBecause(scope.Becauses, scope.Id, errors);
        CheckRepeated(scope.Dragons.Count, "Dragons", scope.Id, errors);
        CheckRepeated(scope.DragonsDocs.Count, "DragonsDoc", scope.Id, errors);
        CheckRepeated(scope.Baselines.Count, "Baseline", scope.Id, errors);

        if (scope.BoundaryOnlyViaCount > 1)
            errors.Add(new SpecValidationError(Code.RepeatedTrailer, scope.Id, $"Repeated trailer 'BoundaryOnlyVia' on '{scope.Id}'."));
        else if (scope.BoundaryOnlyViaCount == 1 && scope.Boundary.Count == 0)
            errors.Add(new SpecValidationError(Code.EmptyBoundary, scope.Id,
                $"BoundaryOnlyVia() on '{scope.Id}' names no types; omit the call for a hermetic freeze."));

        if (scope.Dragons.Count == 0 && scope.DragonsDocs.Count == 0)
            errors.Add(new SpecValidationError(Code.MissingDragons, scope.Id,
                $"Frozen scope '{scope.Id}' is missing .Dragons(...) or .DragonsDoc(...)."));

        foreach ((string label, string? value) in ScopeProse(scope)) CheckProse(value, label, scope.Id, errors);

        CheckForeign(ScopeSelections(scope), scope.Id, arch, errors);
    }

    private static void CheckBecause(List<string> becauses, string id, List<SpecValidationError> errors)
    {
        if (becauses.Count == 0) errors.Add(new SpecValidationError(Code.MissingBecause, id, $"'{id}' is missing a required .Because(...)."));

        CheckRepeated(becauses.Count, "Because", id, errors);
    }

    private static void CheckRepeated(int count, string trailer, string id, List<SpecValidationError> errors)
    {
        if (count > 1) errors.Add(new SpecValidationError(Code.RepeatedTrailer, id, $"Repeated trailer '{trailer}' on '{id}'."));
    }

    private static void CheckProse(string? value, string label, string id, List<SpecValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(new SpecValidationError(Code.BlankProse, id, $"Blank {label} on '{id}'."));
        else if (value!.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0) errors.Add(new SpecValidationError(Code.MultiLineProse, id, $"Multi-line {label} on '{id}'; prose fields are single-line."));
    }

    private static void CheckForeign(IEnumerable<Selection> selections, string id, Arch arch, List<SpecValidationError> errors)
    {
        foreach (Selection selection in selections)
            if (!ReferenceEquals(selection.Owner, arch))
            {
                errors.Add(new SpecValidationError(Code.ForeignSelection, id,
                    $"A selection used by '{id}' was minted on a different Arch instance; it is not registered with this model."));
                return;
            }
    }

    // GRAMMAR §8 items 11–13: the member-access verb's operands. A foreign member is reported once per
    // rule (mirroring CheckForeign's report-once-and-return); otherwise each member is checked for a
    // blank name and then, when named, that its anchor declares it.
    private static void CheckMembers(RuleRegistration rule, Arch arch, List<SpecValidationError> errors)
    {
        var members = rule.Constraint?.MemberOperands ?? Array.Empty<Member>();
        if (members.Count == 0) return;

        foreach (Member member in members)
            if (!ReferenceEquals(member.Owner, arch))
            {
                errors.Add(new SpecValidationError(Code.ForeignMember, rule.Id,
                    $"A member used by '{rule.Id}' was minted on a different Arch instance; it is not registered with this model."));
                return;
            }

        foreach (Member member in members) CheckMember(member, rule.Id, errors);
    }

    // GRAMMAR §8 item 14: a member `.Returning` anchor is definition-level, so a closed-generic anchor
    // (typeof(Task<int>)) is refused with guidance to the open definition (typeof(Task<>)). A non-generic
    // or open-generic anchor is accepted; only a MemberConstraint carries a ReturningAdjective at all.
    private static void CheckMemberReturning(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MemberConstraint memberConstraint) return;

        foreach (MemberAdjective adjective in memberConstraint.MemberSubject.Adjectives)
            if (adjective is ReturningAdjective returning)
                foreach (Type type in returning.Types)
                    if (type.IsGenericType && !type.IsGenericTypeDefinition)
                        errors.Add(new SpecValidationError(Code.MemberReturningClosedGeneric, rule.Id,
                            $"'{SafeFullDisplay(type)}' is a closed generic; .Returning matches definition-level — " +
                            $"use typeof({TypeofForm(GenericDefinition(type))}) (used by '{rule.Id}')."));
    }

    private static void CheckMember(Member member, string id, List<SpecValidationError> errors)
    {
        string display = SafeFullDisplay(member.DeclaringType);

        if (string.IsNullOrWhiteSpace(member.Name))
        {
            errors.Add(new SpecValidationError(Code.BlankMemberName, id,
                $"Blank member name on a member of '{display}' (used by '{id}')."));
            return;
        }

        Type anchor = GenericDefinition(member.DeclaringType);
        if (Declares(anchor, member.Name)) return;

        Type? declaringBase = FindDeclaringBase(anchor, member.Name);
        if (declaringBase != null)
        {
            errors.Add(new SpecValidationError(Code.MemberNotDeclared, id,
                $"'{display}' does not declare '{member.Name}'; it is declared on base type '{SafeFullDisplay(declaringBase)}' — " +
                $"use typeof({TypeofForm(declaringBase)}) (used by '{id}')."));
            return;
        }

        errors.Add(new SpecValidationError(Code.MemberNotDeclared, id,
            $"'{display}' does not declare a member named '{member.Name}' (used by '{id}')."));
    }

    // The first base type / interface (each normalized to its generic definition) that declares the
    // name, or null when nothing in the hierarchy declares it — GRAMMAR §8 item 12's base-type guidance.
    private static Type? FindDeclaringBase(Type anchor, string name)
    {
        if (anchor.IsInterface)
        {
            foreach (Type contract in anchor.GetInterfaces())
            {
                Type normalized = GenericDefinition(contract);
                if (Declares(normalized, name)) return normalized;
            }

            return null;
        }

        for (Type? baseType = anchor.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            Type normalized = GenericDefinition(baseType);
            if (Declares(normalized, name)) return normalized;
        }

        return null;
    }

    private static bool Declares(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static;
        return type.GetMember(name, flags).Length > 0;
    }

    private static Type GenericDefinition(Type type)
    {
        return type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
    }

    // The C#-writable typeof operand for a (normalized) type: non-generic → bare name (`Task`); generic
    // definition → name without arity plus empty type-argument brackets (`HandlerBase<>`, `Foo<,>`).
    private static string TypeofForm(Type type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`');
        string bare = tick < 0 ? name : name.Substring(0, tick);
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

        if (scope.Frozen != null)
            foreach ((string, string?) prose in SelectionProse(scope.Frozen))
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

        // For a member constraint, Subject is the underlying type selection (Subject => MemberSubject.Source),
        // so this also walks any type-side Where/Except used before the projection.
        foreach ((string, string?) prose in SelectionProse(constraint.Subject)) yield return prose;

        foreach (Selection operand in constraint.Operands)
        foreach ((string, string?) prose in SelectionProse(operand))
            yield return prose;
    }

    private static IEnumerable<(string Label, string? Value)> SelectionProse(Selection selection)
    {
        if (selection is UnionSelection union)
        {
            foreach (Selection member in union.Parts)
            foreach ((string, string?) prose in SelectionProse(member))
                yield return prose;

            yield break;
        }

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is WhereAdjective where)
                yield return ("description", where.Description);
            else if (adjective is ExceptAdjective except)
                foreach ((string, string?) prose in SelectionProse(except.Payload))
                    yield return prose;
    }

    private static IEnumerable<Selection> RuleSelections(RuleRegistration rule)
    {
        return rule.Constraint == null ? Enumerable.Empty<Selection>() : ConstraintSelections(rule.Constraint);
    }

    private static IEnumerable<Selection> ConstraintSelections(Constraint constraint)
    {
        foreach (Selection selection in ExpandSelection(constraint.Subject)) yield return selection;

        foreach (Selection operand in constraint.Operands)
        foreach (Selection selection in ExpandSelection(operand))
            yield return selection;
    }

    private static IEnumerable<Selection> ScopeSelections(ScopeRegistration scope)
    {
        return scope.Frozen == null ? Enumerable.Empty<Selection>() : ExpandSelection(scope.Frozen);
    }

    private static IEnumerable<Selection> ExpandSelection(Selection selection)
    {
        yield return selection;

        if (selection is UnionSelection union)
        {
            foreach (Selection member in union.Parts)
            foreach (Selection nested in ExpandSelection(member))
                yield return nested;

            yield break;
        }

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is ExceptAdjective except)
                foreach (Selection nested in ExpandSelection(except.Payload))
                    yield return nested;
    }
}