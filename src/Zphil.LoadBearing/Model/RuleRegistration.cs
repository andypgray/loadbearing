namespace Zphil.LoadBearing.Model;

/// <summary>
///     Mutable backing state for a rule anchor. Trailers append to lists so a repeated trailer
///     (<c>Because</c> twice, two <c>Baseline</c>s) is detectable by validation (§8 item 6). A null
///     <see cref="Posture" /> means the anchor was left dangling (§8 item 2).
/// </summary>
internal sealed class RuleRegistration(string id) : Registration(id)
{
    /// <summary>The posture, or null when no posture verb was called.</summary>
    internal Posture? Posture { get; set; }

    /// <summary>The checkable constraint: the <c>Enforce</c> constraint or the Migrate <c>to</c> target.</summary>
    internal Constraint? Constraint { get; set; }

    /// <summary>The descriptive <c>from</c> prose of a Migrate rule (the OLD pattern).</summary>
    internal string? MigrateFrom { get; set; }

    /// <summary>Every <c>Because</c> supplied (exactly one is valid).</summary>
    internal List<string> Becauses { get; } = [];

    /// <summary>Every <c>Fix</c> supplied (at most one is valid).</summary>
    internal List<string> Fixes { get; } = [];

    /// <summary>Every <c>Baseline</c> supplied (Migrate only; at most one is valid).</summary>
    internal List<string> Baselines { get; } = [];

    /// <summary>Every <c>WhileYoureThere</c> policy supplied (Migrate only; at most one is valid).</summary>
    internal List<MigrationPolicy> Policies { get; } = [];
}