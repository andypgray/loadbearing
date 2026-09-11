using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes the agent-context block bodies from the reified model — the second render target.
/// </summary>
/// <remarks>
///     Pure and codebase-independent: the root block is a function of
///     <c>(model, specName)</c> (plus an optional grandfathered-count provider), a scope card a function
///     of one rule — a quarantine's containment or a caution's tripwire. Output is LF-internal always
///     (never <c>Environment.NewLine</c>); the splicer applies the target file's line ending.
/// </remarks>
public static class AgentContextRenderer
{
    private const string Heading = "## Architecture (LoadBearing)";

    // The root block's second meta-line: the recovery for the reader whose checkout declares the tool but
    // whose machine lacks it, where a configured MCP server dying at launch reads as a config problem
    // rather than a missing install. Root block only, never the scoped cards. Unpinned, unlike the MCP
    // server's own recovery coda: a committed render has no running engine to make the same-engine
    // promise, so a pin here could only rot. Three tokens are load-bearing: `dotnet dnx`, never bare
    // `dnx` (on Windows the short form is a .cmd a POSIX shell cannot resolve); `--yes` (a cold machine
    // can block on a trust prompt); and a read verb, never `mcp` (a stdio server waits on stdin and
    // would hang silently).
    private const string InstallRecoveryLine =
        "*If the `loadbearing` command is missing (a configured MCP server dies without it), " +
        "install the tool with `dotnet tool install -g Zphil.LoadBearing.Cli`, or run any verb " +
        "without installing: `dotnet dnx Zphil.LoadBearing.Cli --yes -- check <solution>`.*";

    // The glossary composes from per-axis clauses, not 2^N whole-string constants: the "reference" clause is
    // always present, "use" iff a member-target rule exists, "construct" iff a ctor rule exists — each axis
    // gates independently, so a spec is never glossed a term it does not use (GRAMMAR §4.1/§4.5/§10). The
    // clauses join with "; " and the tail follows a ". " separator.
    private const string GlossaryReferenceClause = "reference = a source-level type reference";

    private const string GlossaryUseClause = "use = a source-level member access";

    private const string GlossaryConstructClause =
        "construct = a source-level object creation (`new`, including target-typed `new()`)";

    private const string GlossaryInjectClause =
        "inject = a source-level constructor-parameter dependency (primary constructors included)";

    private const string GlossaryCatchClause =
        "catch = a source-level `catch` clause (a bare `catch` counts as `System.Exception`)";

    private const string GlossaryThrowClause =
        "throw = a source-level `throw` of the thrown expression's type (bare rethrows `throw;` are not recorded)";

    private const string GlossaryExposeClause =
        "expose = a public signature position (return, parameter, or property/field/event type) on a public member of an externally visible type";

    // A separate, independently-gated glossary line (not an axis clause): rendered as ONE line whenever any
    // rule's subject or operands carry a Registered noun, and absent entirely otherwise — the same gating
    // discipline as the axis clauses (GRAMMAR §4.7/§10). The backticked method list is literal output.
    private const string GlossaryRegisteredLine =
        "registered = named in a source-level container registration " +
        "(`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/`AddDbContext`/`AddHttpClient<TClient>`); " +
        "registrations made by assembly scanning, factory internals, or framework defaults are not seen.";

    private const string GlossaryTail = "Expand any rule ID with `loadbearing explain <rule-id>`.";

    // The axis clauses in emission order, each beside the fact that earns it, so the pairing is stated once
    // instead of split across a row of booleans and a same-typed positional signature.
    // The two exception axes gate on the axis, not on one verb: any catch verb renders the catch clause and
    // any throw verb the throw clause, so a spec that swaps MustNotCatch for MustNotCatchUnfiltered or
    // MustNotSwallow (or MustOnlyThrow for MustNotThrow) renders byte-identically — the fact being glossed
    // is the same fact.
    private static readonly (Func<ArchRule, bool> Applies, string Clause)[] GlossaryAxes =
    [
        (rule => rule.Constraint?.MemberOperands.Count > 0, GlossaryUseClause),
        (rule => rule.Constraint is MustNotConstructConstraint, GlossaryConstructClause),
        (rule => rule.Constraint is MustNotInjectConstraint, GlossaryInjectClause),
        (rule => rule.Constraint is MustNotCatchConstraint or MustNotCatchUnfilteredConstraint or MustNotSwallowConstraint,
            GlossaryCatchClause),
        (rule => rule.Constraint is MustOnlyThrowConstraint or MustNotThrowConstraint, GlossaryThrowClause),
        (rule => rule.Constraint is MustNotExposeConstraint, GlossaryExposeClause)
    ];

    /// <summary>
    ///     The provenance/warning line — the first line inside every managed block. Names the
    ///     spec deterministically (assembly file name without extension) so the pin is
    ///     machine-independent; carries no timestamp or tool version (idempotence).
    /// </summary>
    /// <remarks>
    ///     The em-dash is free here and is not in <see cref="LawDiagramRenderer" />'s caption: managed
    ///     blocks land in <c>AGENTS.md</c> files, which sit outside the prose-budget gate that
    ///     <c>ARCHITECTURE.md</c> is inside. The two spellings diverge for that reason, not by accident.
    /// </remarks>
    public static string ProvenanceLine(string specName)
    {
        Guard.NotNullOrWhiteSpace(specName, nameof(specName));

        return $"*Generated by `loadbearing render` from `{specName}` — " +
               "do not edit between the markers; edit the spec and re-render.*";
    }

    /// <summary>
    ///     The root managed-block body: provenance, the install-recovery line, the H2 heading, the
    ///     glossary + drill-down pointer (GRAMMAR §4.1, once), then — each omitted when empty — the
    ///     module map (Layers), the Enforce laws (Rules), the Migrate counter-prior paragraphs
    ///     (Migrations), and the Quarantine containment laws (Quarantined scopes — the containment law +
    ///     sanctioned surface; dragons prose stays scoped).
    /// </summary>
    /// <remarks>
    ///     <paramref name="grandfatheredCounts" /> is an optional live-count provider: when it returns a
    ///     value for a Migrate rule, the "Grandfathered sites remaining: {n}." sentence is appended. The
    ///     default (<c>null</c>) renders no counts, so the burndown lives in <c>status</c>/<c>explain</c>
    ///     rather than in the block.
    /// </remarks>
    public static string RootBlock(
        ArchitectureModel model, string specName, Func<ArchRule, int?>? grandfatheredCounts = null)
    {
        Guard.NotNull(model, nameof(model));

        bool needsRegisteredGlossary = model.Rules.Any(rule => rule.Constraint is { } constraint && NeedsRegisteredGlossary(constraint));
        var sections = new List<string>
        {
            ProvenanceLine(specName),
            InstallRecoveryLine,
            Heading,
            GlossaryLine(model.Rules)
        };

        // The Registered glossary line gates independently of the axis clauses (a Registered noun can ride a
        // non-inject verb, and MustNotInject can ban a plain type), so it is its own paragraph (GRAMMAR §10).
        if (needsRegisteredGlossary) sections.Add(GlossaryRegisteredLine);

        if (model.Layers.Count > 0) sections.Add(LayersSection(model.Layers));

        List<ArchRule> enforceRules = model.Rules.Where(rule => rule.Posture == Posture.Enforce).ToList();
        if (enforceRules.Count > 0) sections.Add(RulesSection(enforceRules));

        List<ArchRule> migrateRules = model.Rules.Where(rule => rule.Posture == Posture.Migrate).ToList();
        if (migrateRules.Count > 0) sections.Add(MigrationsSection(migrateRules, grandfatheredCounts));

        List<ArchRule> containmentRules = model.Rules.Where(rule => rule.Scope is { Role: ScopeRole.Containment }).ToList();
        if (containmentRules.Count > 0) sections.Add(QuarantinedScopesSection(containmentRules));

        return string.Join("\n\n", sections);
    }

    // The glossary/drill-down line, composed from the always-on "reference" clause plus the axis clauses the
    // spec actually exercises, then the shared tail — so a spec that exercises no axis beyond references
    // renders the bare "reference." line and nothing more (GRAMMAR §4.1/§4.5/§10).
    private static string GlossaryLine(IReadOnlyList<ArchRule> rules)
    {
        IEnumerable<string> exercised = GlossaryAxes
            .Where(axis => rules.Any(axis.Applies))
            .Select(axis => axis.Clause);

        var clauses = new List<string> { GlossaryReferenceClause };
        clauses.AddRange(exercised);

        return string.Join("; ", clauses) + ". " + GlossaryTail;
    }

    // True when a rule renders the word "registered" (GRAMMAR §10) — out of the VERB, which spells it with no
    // noun to carry it, or out of a Registered noun in the subject or any operand. The noun half descends
    // through Except payloads and the internal Quarantine union, since a Registered noun in any of those still
    // renders the word in the block's prose and so must gate the glossary line. The descent is the shared
    // SelectionWalk, so the block glosses exactly the nouns validation sees; the union guard is
    // SpecValidator.CheckLifetimes's, and it is load-bearing because a UnionSelection has no noun to read.
    private static bool NeedsRegisteredGlossary(Constraint constraint)
    {
        if (constraint is MustBeRegisteredConstraint) return true;

        IEnumerable<Selection> selections = SelectionWalk.ConstraintSelections(constraint);
        return selections.Any(selection => SelectionWalk.NounOf(selection) is RegisteredNoun);
    }

    /// <summary>
    ///     A quarantined scope's context card (without the provenance line, which the splice pipeline adds
    ///     once per file): the scope heading, a lede naming what the scope covers, the containment law and
    ///     rationale, the load-bearing-weirdness dragons prose (inline <c>Dragons:</c> paragraph and/or a
    ///     linked <c>Dragons doc</c> bullet — one of the two is spec-guaranteed), the sanctioned surface
    ///     (omitted for a hermetic quarantine), and the <c>explain</c> pointer.
    /// </summary>
    /// <remarks>
    ///     This is the scoped, per-directory story the agents editing dragon territory read.
    /// </remarks>
    public static string ScopeCard(ArchRule containmentRule)
    {
        Guard.NotNull(containmentRule, nameof(containmentRule));
        if (containmentRule.Scope is not { Role: ScopeRole.Containment } quarantine)
            throw new ArgumentException("ScopeCard requires a Quarantine containment rule.", nameof(containmentRule));

        var bullets = new List<string>
        {
            $"- {ProseFormat.Backtick(containmentRule.Id)} — {containmentRule.Sentence} {containmentRule.Because}"
        };
        if (quarantine.Surface.Count > 0)
            bullets.Add($"- Sanctioned surface: {ProseFormat.JoinInventory(quarantine.Surface)}.");

        return ScopeCardBody(
            "quarantined", "do not spread references into it.",
            bullets, quarantine, containmentRule.Id);
    }

    /// <summary>
    ///     A cautioned scope's context card (without the provenance line, which the splice pipeline adds
    ///     once per file): the scope heading, a lede naming what the scope covers, the load-bearing-
    ///     weirdness dragons prose (inline <c>Dragons:</c> paragraph and/or a linked <c>Dragons doc</c>
    ///     bullet — one of the two is spec-guaranteed), the tripwire and its rationale, and the
    ///     <c>explain</c> pointer.
    /// </summary>
    /// <remarks>
    ///     The twin of <see cref="ScopeCard" /> for the posture with no containment law, so it names no
    ///     boundary and never tells the reader to keep out — a caution's whole point is that new callers
    ///     are welcome and the weirdness is what wants reading first.
    /// </remarks>
    public static string CautionCard(ArchRule tripwireRule)
    {
        Guard.NotNull(tripwireRule, nameof(tripwireRule));
        if (tripwireRule.Posture != Posture.Caution
            || tripwireRule.Scope is not { Role: ScopeRole.Tripwire } caution)
            throw new ArgumentException("CautionCard requires a Caution tripwire rule.", nameof(tripwireRule));

        var bullets = new List<string>
        {
            $"- {ProseFormat.Backtick(tripwireRule.Id)} — a change set touching this scope is flagged by " +
            $"{ProseFormat.Backtick("check --diff-base <ref>")}. {tripwireRule.Because}"
        };

        return ScopeCardBody(
            "cautioned",
            "the weirdness below is load-bearing; read it before you edit, and do not tidy it away.",
            bullets, caution, tripwireRule.Id);
    }

    // The body both scope cards share, so they agree on shape by construction rather than by copy: the
    // posture's heading and lede, its own opening bullets, then the parts every scope card carries.
    // The lede names what the scope covers because the card lands on a directory and `arch_context`
    // returns it for every sibling there — a single-type scope inside a directory of ordinary code would
    // otherwise claim the whole directory. It narrows the claim, not the placement.
    private static string ScopeCardBody(
        string posture, string warning, List<string> bullets, ScopeData scope, string ruleId)
    {
        // The linked long-form doc is a backticked solution-relative path (the spec stays the index),
        // not a rebased markdown link.
        if (scope.DragonsDoc is { } dragonsDoc) bullets.Add($"- Dragons doc: {ProseFormat.Backtick(dragonsDoc)}.");
        bullets.Add($"- Expand: {ProseFormat.Backtick($"loadbearing explain {ruleId}")}.");

        var heading = $"## {ProseFormat.Capitalize(posture)} scope {ProseFormat.Backtick(scope.ScopeId)}";
        string lede = $"This directory holds the {posture} {ProseFormat.Backtick(scope.ScopeId)} scope: " +
                      $"{SentenceRenderer.Reference(scope.Scoped)}. Here be dragons — {warning}";

        var sections = new List<string> { heading, lede };
        if (scope.Dragons is { } dragons) sections.Add($"Dragons: {dragons}");
        sections.Add(string.Join("\n", bullets));

        return string.Join("\n\n", sections);
    }

    /// <summary>
    ///     A layer's "local rules" context card, placed in the layer's directory (without
    ///     the provenance line, which the splice pipeline adds once per file): the layer heading, a
    ///     one-line lede — the directory sentence, then the layer's purpose when it has one, then the rules
    ///     cue — then one bullet per anchored rule, closed by a generic drill-down pointer.
    /// </summary>
    /// <remarks>
    ///     The bullets use the very same Rules/Migrations composer the root block uses, so a rule reads
    ///     identically in both places. No Fix lines (progressive disclosure; <c>explain</c> serves the
    ///     fix). This is the scoped, per-directory rule digest an agent editing the layer reads.
    /// </remarks>
    public static string LayerCard(string layerName, string? purpose, IReadOnlyList<ArchRule> rules)
    {
        Guard.NotNullOrWhiteSpace(layerName, nameof(layerName));
        Guard.NotNull(rules, nameof(rules));

        List<string> bullets = rules.Select(rule => RuleBullet(rule)).ToList();
        bullets.Add($"- Expand any rule above with {ProseFormat.Backtick("loadbearing explain <rule-id>")}.");

        var lede = $"This directory holds the {ProseFormat.Backtick(layerName)} layer.";
        if (purpose is not null) lede += $" {purpose}";
        lede += " Its architecture rules:";

        var sections = new List<string>
        {
            $"## Layer {ProseFormat.Backtick(layerName)}",
            lede,
            string.Join("\n", bullets)
        };

        return string.Join("\n\n", sections);
    }

    // Every section of the root block is an H3 heading over a bullet list, so the shape is written once and
    // each builder is left saying only what its bullets are.
    private static string Section(string heading, IEnumerable<string> bullets)
    {
        return "### " + heading + "\n" + string.Join("\n", bullets);
    }

    private static string LayersSection(IReadOnlyList<LayerDefinition> layers)
    {
        IEnumerable<string> bullets = layers.Select(layer => "- " + layer.DefinitionFragment);
        return Section("Layers", bullets);
    }

    private static string RulesSection(IReadOnlyList<ArchRule> rules)
    {
        IEnumerable<string> bullets = rules.Select(rule => RuleBullet(rule));
        return Section("Rules", bullets);
    }

    // The Migrate counter-prior section: one bullet per Migrate rule that names
    // the OLD pattern as grandfathered debt, states the target law, and renders the boy-scout policy —
    // so an agent meeting the old pattern in the files around its task does not infer it is house style.
    private static string MigrationsSection(IReadOnlyList<ArchRule> rules, Func<ArchRule, int?>? counts)
    {
        IEnumerable<string> bullets = rules.Select(rule => RuleBullet(rule, counts));
        return Section("Migrations", bullets);
    }

    // One rule's context bullet — the shared composer for the root Rules and Migrations sections and
    // the per-layer card, so a rule renders byte-identically wherever it appears. An Enforce rule
    // states its law sentence + rationale; a Migrate rule renders the full counter-prior paragraph
    // (OLD-pattern warning, target law, boy-scout policy, and the optional live grandfathered count).
    // No Fix line ever — progressive disclosure routes fixes to `explain`.
    private static string RuleBullet(ArchRule rule, Func<ArchRule, int?>? counts = null)
    {
        return rule.Posture == Posture.Migrate
            ? MigrationBullet(rule, counts)
            : $"- {ProseFormat.Backtick(rule.Id)} — {rule.Sentence} {rule.Because}{CitationClause(rule)}";
    }

    private static string MigrationBullet(ArchRule rule, Func<ArchRule, int?>? counts)
    {
        MigrateData migrate = rule.Migrate!;
        string bullet = $"- {ProseFormat.Backtick(rule.Id)} — Some existing code here still follows the OLD pattern: " +
                        $"{migrate.From} That is grandfathered debt, not house style. New code must follow: " +
                        $"{migrate.ToSentence} {rule.Because}{CitationClause(rule)} {PolicySentence(migrate.Policy)}";

        if (counts?.Invoke(rule) is { } remaining) bullet += $" Grandfathered sites remaining: {remaining}.";

        return bullet;
    }

    // The rule's citation, as a sentence of its own after the reason: a CommonMark angle-bracket autolink,
    // which is what keeps the following period out of the link target. It sits inside the bullet rather
    // than behind `explain` because the provenance of a quoted guidance rule is the claim the bullet makes,
    // and it is written leading-space-first so a rule with no citation renders the bullet byte for byte as
    // it did before the trailer existed.
    private static string CitationClause(ArchRule rule)
    {
        return rule.Citation is { } citation ? $" See <{citation}>." : string.Empty;
    }

    // The Quarantine containment section (GRAMMAR §7): one bullet per quarantined scope stating the
    // containment law + rationale + sanctioned surface, in the always-on root block because containment
    // binds code OUTSIDE the quarantined directory — those agents never see the per-directory scope card.
    // Dragons prose stays scoped-only (progressive disclosure) and prints in the scope card + explain.
    private static string QuarantinedScopesSection(IReadOnlyList<ArchRule> containmentRules)
    {
        IEnumerable<string> bullets = containmentRules.Select(QuarantinedScopeBullet);
        return Section("Quarantined scopes", bullets);
    }

    private static string QuarantinedScopeBullet(ArchRule rule)
    {
        ScopeData quarantine = rule.Scope!;
        var bullet = $"- {ProseFormat.Backtick(quarantine.ScopeId)} — {rule.Sentence} {rule.Because}";
        if (quarantine.Surface.Count > 0)
            bullet += $" Sanctioned surface: {ProseFormat.JoinInventory(quarantine.Surface)}.";
        return bullet;
    }

    private static string PolicySentence(MigrationPolicy policy)
    {
        return policy switch
        {
            MigrationPolicy.AlwaysMigrate =>
                "If your change touches a grandfathered site, migrate it as part of the change; never grow the debt.",
            MigrationPolicy.NeverMigrate =>
                "Do not migrate grandfathered sites in passing (a coordinated migration is planned); never grow the debt.",
            _ => "If you are already editing a grandfathered site and the migration is small, migrate it; " +
                 "otherwise do not grow the debt."
        };
    }
}
