using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes the text LoadBearing manages inside <c>AGENTS.md</c> files: <see cref="RootBlock" /> for
///     the solution directory, <see cref="LayerCard" /> for a layer's own directory, and
///     <see cref="ScopeCard" /> or <see cref="CautionCard" /> for a scope's. Every method is a function
///     of the model alone, so the same model always renders the same text, and none of them reads or
///     writes a file. Line endings are always LF, whatever the host platform:
///     <c>ManagedBlock.Splice</c> converts them to the ones the target file already uses.
/// </summary>
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
    ///     The line that opens every managed block: a note that the text was generated by
    ///     <c>loadbearing render</c> from <paramref name="specName" /> and is not to be edited between the
    ///     markers. Pass the spec assembly's name, with no path and no extension, so the line reads the same
    ///     on every machine. It carries no timestamp and no tool version, so re-rendering an unchanged spec
    ///     leaves the file byte for byte as it was. <see cref="RootBlock" /> already begins with this line;
    ///     prepend it yourself when you splice cards into a file that has no root block.
    /// </summary>
    // The em dash is affordable here and is not in LawDiagramRenderer's caption: managed blocks land in
    // AGENTS.md files, which sit outside the prose budget ARCHITECTURE.md is inside. The two spellings
    // diverge for that reason, not by accident.
    public static string ProvenanceLine(string specName)
    {
        Guard.NotNullOrWhiteSpace(specName, nameof(specName));

        return $"*Generated by `loadbearing render` from `{specName}` — " +
               "do not edit between the markers; edit the spec and re-render.*";
    }

    /// <summary>
    ///     The managed block for the solution directory's <c>AGENTS.md</c>: the provenance line, a line
    ///     saying how to install the tool when the <c>loadbearing</c> command is missing, an
    ///     <c>## Architecture (LoadBearing)</c> heading, and a one-line glossary of the terms the spec's
    ///     rules use (a reference always, and use, construct, inject, catch, throw, expose or registered
    ///     only where some rule needs them), closed by a pointer to
    ///     <c>loadbearing explain &lt;rule-id&gt;</c>. Then, each section omitted when the spec has nothing
    ///     for it: the layers with what each is for, the Enforce rules, the Migrate rules with their
    ///     grandfathered-debt paragraph, and each quarantined scope's containment rule with its sanctioned
    ///     surface. A scope's dragons prose is not here: that belongs to the scope's own directory card.
    ///     Pass the spec assembly's name as <paramref name="specName" />.
    /// </summary>
    /// <remarks>
    ///     <paramref name="grandfatheredCounts" /> is optional. When it returns a count for a Migrate rule,
    ///     that rule's paragraph ends with "Grandfathered sites remaining: {n}."; left null, the default,
    ///     the block carries no counts and the burndown is reported by <c>loadbearing status</c> and
    ///     <c>loadbearing explain</c> instead.
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
    ///     A quarantined scope's card for its own directory's <c>AGENTS.md</c>: the scope heading, a lede
    ///     naming what the scope covers and warning the reader off spreading references into it, the dragons
    ///     prose (an inline paragraph, a linked document, or both, of which a quarantine always has at least
    ///     one), the containment rule with its reason, the sanctioned surface where the scope has one, and a
    ///     pointer to <c>loadbearing explain</c>. There is no provenance line: add
    ///     <see cref="ProvenanceLine" /> once per file, above the cards it holds. Pass the scope's
    ///     containment rule, whose scope has role <see cref="ScopeRole.Containment" />; anything else throws
    ///     <see cref="ArgumentException" />.
    /// </summary>
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
    ///     A cautioned scope's card for its own directory's <c>AGENTS.md</c>: the scope heading, a lede
    ///     naming what the scope covers and telling the reader the strangeness inside is load-bearing, the
    ///     dragons prose (an inline paragraph, a linked document, or both, of which a caution always has at
    ///     least one), the tripwire with its reason, and a pointer to <c>loadbearing explain</c>. It names
    ///     no boundary and never tells the reader to keep out: new callers of a cautioned scope are welcome,
    ///     and the strangeness is what wants reading first. There is no provenance line: add
    ///     <see cref="ProvenanceLine" /> once per file, above the cards it holds. Pass the scope's tripwire
    ///     rule, whose posture is <see cref="Posture.Caution" /> and whose scope has role
    ///     <see cref="ScopeRole.Tripwire" />; anything else throws <see cref="ArgumentException" />.
    /// </summary>
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
    ///     A layer's local-rules card for its own directory's <c>AGENTS.md</c>: a <c>## Layer</c> heading, a
    ///     lede naming the layer, then <paramref name="purpose" /> when the layer has one, then one bullet
    ///     per rule in <paramref name="rules" />, closed by a pointer to
    ///     <c>loadbearing explain &lt;rule-id&gt;</c>. Pass the rules the layer is the subject of, in the
    ///     order they should be read. A rule reads here exactly as it reads in <see cref="RootBlock" />, and
    ///     no card carries fix lines: <c>loadbearing explain</c> is where a fix is printed. There is no
    ///     provenance line: add <see cref="ProvenanceLine" /> once per file, above the cards it holds.
    /// </summary>
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
    // and it is written leading-space-first so a rule with no citation adds nothing to the bullet at all.
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
