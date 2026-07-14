namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Signals that a rule cannot be evaluated for a spec-authoring reason the checker turns into a
///     <see cref="ViolationKind.RuleError" /> (Failed) instead of crashing the run: a closed-generic
///     type noun (v1 edges are type-level), or an escape-hatch predicate that threw. Carries the
///     agent-facing <see cref="Exception.Message" /> verbatim into the violation detail.
/// </summary>
internal sealed class RuleEvaluationException(string message) : Exception(message);