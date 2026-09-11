# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`check --hook-event` names the Claude Code event the `--hook-json` document answers.**
  `PostToolUse` (the default), `Stop` or `SubagentStop`. Claude Code reads a hook's
  `additionalContext` only from a document naming the event it fired, so a turn-end wrapper handed
  the per-edit envelope would report its tripwire warnings to nobody — silently, since nothing
  downstream of a hook says that its context went nowhere. The flag needs `--hook-json` beside it and
  refuses an event outside the three.

- **The library packages ship their XML documentation.** Every public type and member across the
  packages has a doc comment, and until now none of it left the repository: no project wrote the
  documentation file, so a consumer's editor showed nothing for the fluent surface or the adapter.
  `Zphil.LoadBearing`, `.Roslyn` and `.Xunit` now carry the file beside the assembly, and the build
  holds the comments complete and well-formed rather than a sweep doing it: in the shipping
  projects, a public member without a comment, a malformed comment, or a `cref` that no longer
  resolves is a compile error.

- **Every page a rule cites is held to a live answer.** The URLs `.Citation(uri)` renders into the
  committed context blocks are HEADed by a doc-hygiene gate, and one that answers 404 or 410 fails
  the suite naming the file, the line and the rule behind it. The rendered blocks are the authority
  rather than the spec sources, so the gate reads committed bytes and loads no workspace. It reaches
  the network, so it is opt-in behind `LOADBEARING_LINK_CHECK`: unset, it reports itself skipped
  with that reason, and a page it could not reach on a given run is a skip naming that page rather
  than a red. CI opts in on its Linux leg.

- **A rule can cite its source: `.Citation(uri)` on Enforce and Migrate rules.** The canonical
  page a rationale rests on gets a field of its own, beside the `Because` that used to carry it
  as a trailing URL. It is a noun trailer like `Fix` and `Purpose`, optional, and at most one per
  rule; a scope has none, because a scope documents itself through `Dragons`/`DragonsDoc`. The
  context bullet renders it as a sentence after the reason, `See <url>.`, in angle brackets so
  the period stays out of the link, and for a Migrate rule between the reason and the boy-scout
  policy, so the policy still ends the paragraph. `explain` and a failed rule's `check` block
  print a `citation:` line after `because:`; `check --json` carries `citation` after `fix`,
  elided at index grain with the rest of the prose. SARIF publishes it twice, as the descriptor's
  `helpUri` and inside `help`: GitHub code scanning does not read `helpUri`, and `help.markdown`
  is what it displays, so a link that rode in `helpUri` alone would be invisible where a reader
  stands over the alert. `help` is the fix and the page joined, in both registers — plain in
  `help.text`, angle-bracketed in `help.markdown` — which also fills the `help.text` GitHub marks
  required for a rule carrying no fix of its own. The spec build refuses a value that is not an absolute `http`/`https` URL, so a pasted page
  title or a repository path fails the build rather than reaching a reader as a dead link; blank
  and multi-line values report as prose, and a second call as a repeated trailer. A rule that
  cites nothing renders every block, dump and SARIF descriptor byte for byte as before.

- **`.Returning` and `MustAcceptParameter` anchor by string, and `MustAcceptParameter<T>()`.**
  `web.Methods.Returning("Microsoft.AspNetCore.Mvc.IActionResult").MustHaveSuffix("Async")` and
  `.Methods.MustAcceptParameter("System.Threading.CancellationToken")` take the type definition's
  fully-qualified name on the same terms as every other string anchor: it matches any construction
  of that definition, needs no assembly load, and renders byte-identically to the `typeof` form,
  so which form a spec chose is invisible to its sentences. The two member type positions were the
  last anchor positions without the string arm, and the spec-load failure for a shared-framework
  type ("every typeof() anchor position carries a string overload") now tells the truth for them
  too. One call is all-`typeof` or all-string, never a mix, so a return-type list that reaches for
  a framework type is spelled all-string, the open generic with its declared type-parameter names:
  `.Returning("System.Threading.Tasks.Task<TResult>", "Microsoft.AspNetCore.Mvc.IActionResult")`.
  The cost is the one every string anchor carries, and it lands differently on the two verbs: a
  typo'd or constructed spelling names nothing, so as the sole `.Returning` anchor it empties the
  subject and the rule reds, beside an anchor that still matches it is inert and the rule stays
  green, and on `MustAcceptParameter` it is always red. Blank names are refused at spec build
  under the "return type name" and "parameter type name" labels. `MustAcceptParameter<T>()` is
  the generic sugar the attribute and hierarchy verbs already carry; `.Returning` keeps no `<T>`
  twin, because its main use is the open generic. In passing, `.Returning`'s anchor list now
  widens colliding simple names by their trailing namespace segments like every other anchor
  list ("returning `A.Result` or `B.Result`"); no committed render moves.

- **A family of layers may reference each other, but not in a circle:
  `MustNotHaveCircularReferences()`.** `arch.Each(model, checking, rendering)
  .MustNotHaveCircularReferences()` renders "Each of the Model, Checking and Rendering layers must
  not have circular references with the others." — the law between the cross-cell ban, which
  forbids every reference between the cells, and the ordering rules, which state an order arrow by
  arrow: the law for peers with no intended order, and the first one to write on an estate whose
  order nobody has stated yet. The cell graph has an arrow from one cell to another for every owned
  reference crossing between them, and a violation is every type pair on every arrow inside a
  strongly connected component of that graph — the same `(source, target)` identity every
  reference verb keys on, so the baseline file, the ratchet, the human report, SARIF and `explain`
  are untouched; the one addition is a JSON-only `detail` naming the circle ("circular references
  among the Reporting and Invoicing layers", the cells in declaration order). A broken circle
  leaves every pair on it stale for `status` to surface and `baseline --accept-reductions` to
  retire, which is how this repository paid its own (see Changed). The verb is nullary and takes
  a family of layers only: a plain subject and a family of projects fail spec build under the new
  `CircularReferencesNeedLayerFamily` error, the second because MSBuild already forbids circular
  project references, and a rule that cannot go red is a false promise. It is the fourth reader of
  a family's cells, after the `MustOnly*` self-allowance, the cross-cell ban and the inbound leaf;
  the `derive_spec` recipe names it beside the family bullet.

- **Rules over a family: `arch.Each`, the per-cell self and two leaf verbs.**
  `arch.Each(host, adapter, pack).MustNotReferenceEachOther()` says no layer in the family reaches
  another, and `arch.Each(arch.Projects.Matching("Nop.Plugin.*")).MustNotReferenceEachOther()` says
  the same over every project a selection names — one rule, one ID, one baseline, one board row,
  one test row, where until now the only honest spelling was one rule per cell (28 for
  nopCommerce's plugins) or the law went unwritten. A family is a selection whose noun carries a
  partition into cells: declared layers, or the projects a project selection names at check time.
  To every verb but four it is the union of its cells. The `MustOnly*` reference verbs read
  "self" per cell — the whole layer or project the type sits in, as declared, so a module's
  `Contracts` cone excluded from the subject still counts as its own — which is what lets
  Meridian.Operations' three `internals` rules become one:
  `arch.Each(dispatch, tracking, invoicing).Except(<the three Contracts cones>)
  .MustOnlyBeReferencedByItself()`, rendering "Types in each of the Dispatch, Tracking and
  Invoicing layers, except …, must be referenced only by their own layer."
  `MustOnlyBeReferencedByItself()` is the inbound twin of `MustOnlyReferenceItself()` and works on
  a plain subject too ("must be referenced only by itself", or "by themselves" once an adjective
  puts the subject in the types voice); `MustNotReferenceEachOther()` needs a family. A bare
  family speaks collectively ("Each of the Host, Adapter and Pack layers must not
  reference the others."), a type sitting in two layer cells fails the rule with a rule error
  naming both, and an empty cell fails it naming the cell. Two spec-build errors arrive with it:
  `FamilyMisplaced` (a family anywhere but subject position) and `EachOtherWithoutFamily`.
  Multi-declarer attribution rides free: a project cell names its types at that project.

- **A layer can be defined by a selection: `arch.Layer(name, Selection)`.**
  `arch.Layer("Core", arch.Project("MyApp.Core"))` names an assembly, and
  `arch.Layer("Model", core.InNamespace("MyApp.Core.Model.*"))` names a cone inside another layer.
  Until now a layer was namespace globs and nothing else, and a layer is the one noun a spec can
  name: the module-map row, the `Purpose`, the collective voice ("The Domain layer must not …"),
  the per-directory card and the drawing's label all hang on that name. An estate whose unit of
  architecture is the assembly had no nameable noun for it — `arch.Project(...)` checks the same
  law and earns no row, no card and no purpose — and a layer whose honest extent is one cone minus
  a nested one could not be declared at all, leaving `.Except` on every rule while the row still
  claimed the whole cone. A layer is transparent to its definition: it names exactly what the
  definition names, at the projects the definition names them at, so a project-defined layer gives
  the same verdicts as the bare project noun on every verb, multi-declarer attribution included. Its
  own adjectives apply after. The glob form is untouched in behaviour and in bytes, and is not
  desugared to a union: a glob matching nothing is still a scan that found nothing rather than an
  empty operand that fails the rule. The definition is validated where the layer is declared, with
  every walk a rule's selections take — foreign `Arch`, blank operands, lifetimes, `Except` payloads
  — reported spec-wide and named by layer, with no new error codes. The row spells the definition:
  a glob layer renders exactly what it rendered before, a bare noun renders its locative without the
  head ("**Core** — project `MyApp.Core`", "**Kernel** — projects `A` or `B`"), and anything else
  renders its reference phrase ("**Model** — types in the Core layer in `MyApp.Core.Model.*`"). On
  the law drawing a layer defined as one project collapses with a rule naming that project onto one
  node under the layer's name, a refinement is drawn inside the layer it refines, and an
  adjective-free union of places is the box its operands sit in. `ProjectSelection` is not a
  `Selection`, so `arch.Layer("X", arch.Projects.Named("X"))` does not compile: a layer is a set of
  types. This repository's own five assembly-shaped layers are now their projects and its three
  inner layers are cones inside Core.

- **A scope posture without containment: `.Caution(selection)`.**
  `arch.Scope("domain/retry-budget").Caution(arch.Types.Named("RetryPolicy")).Dragons("…").Because("…")`
  puts a dragons card on code that new callers are welcome to reach. Until now a card existed only
  where a containment law did: `IScopeBuilder` had one verb, so "read this before you touch it" could
  not be said truthfully about code that is legitimately referenced from everywhere — a hermetic
  `Quarantine` with a `Baseline` grandfathered every caller as debt and failed the next one, and a
  boundary of `arch.Types` rendered a law true by being empty. A caution desugars to `{id}/tripwire`
  alone, under a fourth posture, `Caution`, the first with no red state: with `check --diff-base <ref>`
  a change set touching the scope draws a warning in the caution's own voice ("read the dragons before
  editing: loadbearing explain {id}/tripwire"), the human report prints `dragons:` beneath a fired
  tripwire of either posture, and no reference into the scope is ever a violation. `render` and
  `arch_context` place the card on the scope's directory, after a layer card where one exists;
  `explain` opens `{id}/tripwire (caution/tripwire)`; `status` prints the diff-aware skip line and no
  ratchet; `check --json` carries the warning as `cautionedScopeTouched`; SARIF declares the tripwire
  at warning level with `posture: caution`; the drawing lists it under "Not drawn in full" tagged
  `(Caution)`. The root block does not change: a caution is not a rule over real code and adds no
  line to the board. `ICautionedScope` offers `Dragons`, `DragonsDoc` and `Because` and nothing else;
  the spec-build checks widen by wording, so a scope with no posture or with both names both verbs,
  and a caution missing both `Dragons` and `DragonsDoc` fails the build as a quarantine does. On the
  xUnit adapter, which has no diff context, a caution's tripwire is a permanent skip. This
  repository's own spec cautions its `Model` namespace, where the fragments every renderer assembles
  are declared, and the card merges into the directory's layer card.

- **A layer can say what it is for: `.Purpose(prose)` on the layer definition.**
  `arch.Layer("Domain", "MyApp.Domain.*").Purpose("Domain holds the order and customer model.")`
  renders the module-map row as "**Domain** — `MyApp.Domain.*`. Domain holds the order and
  customer model." and, where a rule anchors on the layer, opens its directory card with "This
  directory holds the `Domain` layer. Domain holds the order and customer model. Its
  architecture rules:". Until now the row was a name and its globs, the card went from the
  directory straight to the rules, and the sentence saying what a layer is for had nowhere to
  go but the opening clause of a rule's `Because`, where it stayed true after the rule was
  deleted, or a hand-written README paragraph beside a block that could not carry it. This
  repository's own eight layers and the three layered examples now carry one each. The purpose
  is authored prose on the same terms as `Because`: one line, validated at spec build (blank,
  multi-line, or a second call fails the build, spec-wide and named by layer), rendered
  verbatim, and checked by nothing. A layer without one renders exactly what it rendered
  before, and a purpose alone places no card: a card is local law and still needs an anchored
  rule. `LayerDefinition` gains a `Purpose` property, and `AgentContextRenderer.LayerCard`
  takes the purpose as a second parameter.

- **A quarantine's sanctioned surface can be named without loading it.**
  `BoundaryOnlyVia` now takes selections beside types, so
  `.BoundaryOnlyVia(arch.Types.Named("CodeFormatHelper"))` states a boundary the spec assembly
  cannot compile against — an `internal` facade, or one in a project the spec does not reference
  — and a namespace or layer states one that is a whole region. It was the last set-valued
  position with no no-load spelling, and the cost of that showed up as law that says the wrong
  thing: a `typeof` anchor the spec host could not load left an adopter grandfathering their
  sanctioned entry point as debt, so the rendered rule called the sanctioned surface debt to burn
  down while the dragons prose beside it said the opposite. Because the formula is indifferent to
  whether the surface lies inside the scope or outside it, the same spelling states a sanctioned
  *consumer*. `BoundaryOnlyVia(typeof(IFacade))` is unchanged and now reads as the sugar it
  always was — identical model, identical sentence, identical `explain` — and the scope card and
  `explain` render one pre-computed surface, so the two can no longer word it differently. The
  drawing is the one place that narrows: a region facade is a doubled box inside the scope's box
  as a type is, while a name is not a place, so its containment rule joins the compact list under
  the fence instead of drawing a box.

- **The exemption idiom has a noun: `arch.Types.Named(name, …)`.**
  `host.Except(arch.Types.Named("McpServerCommand"))` renders "Types in the Host layer, except
  types named `McpServerCommand`, must not use …": the exact, ordinal simple name, or-joined for
  several ("types named `A` or `B`"). The only spelling before was a wildcard-free glob,
  `WithNameMatching("McpServerCommand")`, which matched the same set and rendered "types whose
  name matches `McpServerCommand`" — a pattern in the sentence where the author meant a name. It
  was the most common exemption shape in the corpus: nine sites across this repository's own spec
  and two of the examples, every one now `Named`, and this repository's
  `exceptions/no-swallowed-broad-catches` now lists its seven sanctioned handlers in the sentence
  instead of behind a `Where` description. The fragment says *named* rather than rendering the
  bare backticked name a `typeof` does, because a simple name reaches every type carrying it in
  every namespace and project, and the sentence states the set the checker uses.
  `WithNameMatching` stays the glob form beside it, and a blank name fails spec build.

- **`Except` takes a list, and `typeof`.** `.Except(a, b)` is `.Except(arch.AnyOf(a, b))` (one
  union, rendered ", except types in `A.*` or `B.*`"), and `.Except(typeof(Constraint))` is
  `.Except(arch.Type<Constraint>())` with the `typeof` written for you: the `(first, params more)`
  pair and the `Type` sugar every dependency verb already carries, now on the one adjective
  position that lacked them. One operand is the payload it always was, byte for byte.

- **A leaf of the reference graph has a verb of its own: `MustOnlyReferenceItself()`.**
  `tracking.MustOnlyReferenceItself()` renders "The Tracking layer must reference only itself
  (external packages are not constrained by this rule)", which is the shape a module graph's leaf
  is left with once the allow-list default below gives it nothing to name. Nullary, because the
  empty list is the law: the subject is the whole of what the rule permits. It draws no arrow —
  there is no second place to point one at — so a diagram lists it under the fence, exactly where
  its explicitly self-listing predecessor landed. A subject that speaks in the types voice takes
  the plural: "Types in `MyApp.Tracking.*` named `*Service` must reference only themselves
  (external packages are not constrained by this rule)".

- **The rendered root block now names the install route.** The managed `AGENTS.md` block carries a
  second meta-line directly after the provenance sentence: if the `loadbearing` command is missing
  (a configured MCP server dies without it), install the tool with
  `dotnet tool install -g Zphil.LoadBearing.Cli`, or run any verb without installing:
  `dotnet dnx Zphil.LoadBearing.Cli --yes -- check <solution>`. The line exists for the reader a
  dead MCP server leaves behind: a checkout can commit its MCP wiring but not the tool, so the
  first session on a fresh clone meets a configured server that dies at launch. That failure reads
  as a configuration problem rather than a missing install, and the committed block is the one
  surface such a session reads without being pointed at it. The line is unpinned, unlike the live
  server's recovery coda: a committed render has no running engine behind it, so there is no
  same-engine promise for a version to back, and a pinned version in a committed file only rots.
  Re-rendering updates the root block; scoped cards are unchanged.

- **A ratcheted correspondence law: `MustHaveExactlyOneCounterpart`.** "Every service has exactly
  one `I{Name}` contract" was sayable only as prose; now
  `arch.Types.WithSuffix("Service").MustHaveExactlyOneCounterpart(among:
  arch.Types.OfKind(TypeKind.Interface), named: "I{Name}")` renders "must have exactly one
  counterpart named `I{Name}` among interfaces" — the template backticked and unsubstituted, the
  law stated as the pattern. Per subject, every `{Name}` is replaced by the type's simple name
  (ordinal, arity-free, nested types by leaf name) and exactly one type in `among:` must carry
  the result: a service with no contract is red, and so is one with two claimants. Both arms key
  the subject alone, so the rule ratchets under `Migrate` from day one — counterparts are
  evidence, and a grandfathered subject stays blessed when its arm flips — while the arms point
  where the edit goes: an absence at the subject's declaration, an ambiguity at the counterparts
  that collide. Validation refuses a blank template and one with no `{Name}` placeholder (a
  constant derived name is a cardinality claim, not a correspondence — and the ordinal match is
  what makes the `{name}` typo fail at spec build). One `among:` selection by design: authors
  union candidate homes with `arch.AnyOf`, whose or-join states the reading. The violated
  fixture carries the first committed per-subject baseline —
  `layering/services-behind-contracts` blesses `OrderService` and reds `InvoiceService` at its
  own `file:line`.

- **`check --hook-json`: a tripwire warning reaches the agent.** A Quarantine tripwire is the one
  rule that reports by warning, and a warning never moves the exit code — so for as long as the
  hook recipe simply exited 0 on a clean check, every tripwire warning it was handed was
  discarded. `--hook-json` renders the run for a Claude Code `PostToolUse` hook instead: a clean
  check that warned writes its report as
  `{"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":"…"}}`, which is the
  one exit-0 output the client turns into a transcript system message; a clean check with nothing
  to say writes nothing at all; a red rule prints the report it always printed, byte for byte, so
  the wrapper still has something to block with. The escaping lives here rather than in the
  wrappers because a multi-line report inside a JSON string is the whole job and POSIX `sh` has no
  JSON. All four committed wrappers (`hooks/`, `examples/Meridian/hooks/`) pass the flag and
  pass stdout through; the exit-code contract — 0 proceed, 1 → 2 block, anything else → 1 — is
  unchanged. `--json` and `--hook-json` both own stdout, so passing both is refused. The Meridian
  storyboard gains a fifth beat walking the loop with the tripwire armed: an agent fixing a real
  bug inside the quarantined clearance engine, the warning it draws, and the dragons it is sent to
  read.

### Changed

- **The agent hook fires when the turn ends, not after every edit.** The recommended wiring is now a
  `Stop` hook, with a `SubagentStop` twin for workers, and the wrappers check the working tree rather
  than one tool's payload. A red rule refuses the stop with the report as the reason, so the agent
  keeps working and fixes it in the same turn; a tripwire warning on a clean tree continues the turn
  once as context. Measured over six weeks of this repository's own sessions, the per-edit shape was
  answering a question the same turn went on to change 94% of the time, cost a median 46 seconds per
  code edit and delivered one real block; it never fired at all in a quarter of the turns that
  changed code, because those edits went through a shell or a language server, where a tool matcher
  sees nothing. Reading the tree covers all of them. A stop whose tree has not changed since the last
  clean verdict skips the check outright, so a question-and-answer turn costs nothing, and a red
  verdict is never skipped. Consecutive continuations are capped per prompt (three, over
  `LOADBEARING_HOOK_MAX_ROUNDS`), after which the hook reports and lets the turn end rather than
  looping. The wrappers still honour a `PostToolUse` payload, so an existing per-edit wiring keeps
  working; the recipe no longer includes it, nor the second `mcp_tool` leg, whose output never
  reached the agent at all.

- **A comment-only edit no longer re-extracts its project, on either hook leg.** The warm MCP
  server and the persisted CLI cache both used to re-walk a whole project whenever any byte of one
  of its files changed, because a fragment's sites carry line numbers and a comment can move them.
  Each changed document is now compared by shape: its tokens, its directives and the generated-source
  banner, never its comments or whitespace. When the shape is unchanged the stored sites are moved
  through a line map instead, so red sites, grandfathered sites and every context render report the
  new lines, and the verdict is the one a cold run gives. Anything else still re-walks: a string
  literal, a `#pragma`, a `#region`, an `#if`, a banner, or a line that split so its tokens no
  longer sit together. The persisted cache stores each document's shape beside its hash, so its
  schema moves and an existing cache file is rebuilt once. Measured on this repository's own hook
  with a comment-only edit, alternating the two builds in one sitting: the CLI leg went from about
  40 s to 3 s and the warm MCP server from 17–21 s to under 1 s, while a cache hit and a cold run
  cost what they did before.

- **This repository's own ratchet is paid off, and `mcp/env-through-seam` is law.** The rule banned
  every reference to `System.Environment` from the MCP infrastructure, but its counter-prior prose,
  its reason and its fix were all about environment-variable reads going through the `IEnvironment`
  seam, whose only member is `GetVariable`. The real environment reads were migrated in August; what
  stayed on the baseline was a folder lookup and a process exit, two calls the seam cannot take — so
  the boy-scout policy was sending an agent to migrate a site to a seam with nowhere to put it, and
  the ratchet could never reach zero. The law is now the four environment-variable members it was
  always about (`GetEnvironmentVariable`, `GetEnvironmentVariables`, `ExpandEnvironmentVariables`,
  `SetEnvironmentVariable`), which has no violations, so the rule is `Enforce` and
  `arch/baselines/mcp/env-through-seam.json` is deleted. The self-check still reports 37 rules, 35
  passed and 2 skipped tripwires; the managed block loses its Migrations section, and the law diagram
  loses the grandfathered arrow along with the legend row that explained it. The README's `As SARIF`
  and `check --json` fences are re-cut from the Meridian example's `data-access/no-inline-sql`, which
  still has twelve sites to work off, because showing a ratchet needs live debt.

- **The self-spec reads in areas, cites its pages, and renames two rules into the area they belong
  to.** Rules are declared in reading order — layering, the API front doors, the model nodes,
  packaging, DI, the CLI, the MCP server, Roslyn, the adapter, exceptions, static state, naming, then
  the two scopes — which is the order the managed block and every scoped card render in, so both read
  as a document rather than as the order the rules happened to be written. `packs/depends-on-core-only`
  is now `layering/pack-depends-on-core-only` and `xunit/leaf-adapter` is `layering/adapter-is-a-leaf`:
  both state a layering law and neither had a baseline to move. The adapter rule also drops Host and
  Pack from its target list, which `layering/leaves-independent` already holds off it, so it names only
  what sits below the adapter. Nine rules gained a `.Citation` — the MCP transport and cancellation
  specs, the framework design guidelines for exceptions and interface names, the NuGet lock-file page,
  the DI guidelines, and the two async pages — so the rendered bullet, `explain`, the check block and
  the SARIF descriptor all carry the page each reason rests on. Reviewer-facing mechanics moved out of
  four `Because` sentences into a comment above the rule they belong to, the class doc's verb ledger
  is a list rather than one unbroken paragraph, and its anchor-doctrine paragraph is gone: since
  `SpecExclusion` drops the spec project from the checked universe, no expression anchor in a spec can
  mint an edge the checker sees.

- **The pack's three async rules say what the TAP page says.** `naming/async-suffix` and
  `async/accept-cancellation` name `ValueTask` and `ValueTask<TResult>` beside `Task` and
  `Task<TResult>`: the page they cite fixes the `Async` suffix and the `CancellationToken`
  parameter for an awaitable return, and a `ValueTask` is one. Both also narrow their subject with
  `.Authored()`, so pointing either at a web tier no longer reds every compiled Razor view for a
  convention its generator does not follow — the rule reaches the code someone can rename.
  `async/no-sync-over-async` drops its `Task.GetAwaiter` and `Task<TResult>.GetAwaiter` anchors and
  gains `ValueTask<TResult>.Result`, `ValueTaskAwaiter.GetResult` and
  `ValueTaskAwaiter<TResult>.GetResult`: `GetAwaiter` alone blocks nothing, and banning it beside
  `GetResult` counted one blocking line as two baseline entries. The Interchange example's baseline
  retires that duplicate and carries three entries for its three blocking lines. Rule IDs, postures
  and citations are unchanged; every managed block quoting the three rules re-renders, and a
  baseline keyed by member ID is unaffected in both directions, since `.Authored()` can only shrink
  a subject and `ValueTask` only grow it.

- **The pack's catch rule bans a swallowed catch, not every catch.** `DotNetGuidance.NoGeneralCatch`
  declares `MustNotSwallow(typeof(Exception))` where it declared `MustNotCatch`, so a
  `catch (Exception) when (…)` and a catch that ends in `throw` are lawful rather than violations.
  That is what the page it cites says — it asks you not to catch base `Exception` "unless you intend
  to rethrow" — and this was the one pack rule claiming more than its canon. Its reason no longer
  names the Meridian dispatcher's poll loop, a consumer's fact sitting in text no consumer can
  override; it names the top-level handler in the abstract instead. The default `Fix` gains the two
  remedies the verb admits. The rule ID, the posture, the seam parameter and the citation are
  unchanged, and the Interchange example stays green on the same `DerivedFrom<BackgroundService>()`
  exemption. The rendered sentence reads "must not swallow" rather than "must not catch", so every
  managed block quoting the rule re-renders.

- **A `Where` and an `Except` on one subject render the `Where` first.** Whichever order they were
  written, the exception now canonicalizes last. `.Except(arch.Type<SqlConnection>()).Where(pred,
  "whose name contains a digit")` used to read "Types in `MyApp.*`, except `SqlConnection` whose
  name contains a digit must be sealed" — the description landing on the exception, and the comma
  that should close the parenthetical with nowhere to go. It now reads "Types in `MyApp.*` whose
  name contains a digit, except `SqlConnection`, must be sealed." The checker is untouched:
  exclusion and filtering commute, so the sentence names the set it always named. No published
  spec carries both clauses on one subject, so no managed block moves.

- **Every rule in the .NET guidance pack cites its page through `Citation`, and its reason ends
  as a sentence.** The pack's nine reasons carried the Microsoft URL as their last token, joined
  by an em-dash, because there was nowhere else to put it. Each reason now closes with a full
  stop and the URL rides in the rule's `Citation`, so the rendered bullet reads as prose and ends
  in "See <url>." rather than trailing off into an address. Three rules in the Interchange example
  moved the same way. Two of the reasons reached the URL through a second em-dash and now read as
  one clause. `DotNetGuidance`'s methods take the page beside the reason; a consumer that
  overrides a `Fix` is unaffected, and no rule ID, sentence or baseline path moves. Every managed
  `AGENTS.md` block quoting these rules re-renders.

- **Breaking: the third migration policy is `MigrationPolicy.NeverMigrate`.** It was
  `NeverExpand`, which named the clause every policy renders ("never grow the debt") rather than
  the thing it decides: leave grandfathered sites alone in passing, because a coordinated
  migration is planned. The policies now answer the question `.WhileYoureThere` asks, as
  imperatives beside each other: `MigrateIfSmall`, `AlwaysMigrate`, `NeverMigrate`. The rendered
  sentence is unchanged, so no managed block moves; `explain` prints the new name on its
  `policy:` line. No alias or shim: `explain` prints the member through `Enum.ToString`, which is
  unspecified across two members sharing one value.
- **Breaking: `PathComparison` is no longer public, and it has left `Zphil.LoadBearing.Rendering`.**
  The per-OS path-segment comparison helper was a rendering type in name only: the checker's diff
  context borrowed it, which closed a circle between the two readers of the model that the new
  circular-references rule over this repository's own layers then measured and paid. It now lives
  with Core's internal helpers, visible to the shipping packages and the test project alone. It
  was consumed nowhere outside this repository; a caller that reached it should compare path
  segments with `StringComparison.OrdinalIgnoreCase` on Windows and macOS and `Ordinal` elsewhere,
  which is all it ever did.

- **A rule over a family of layers places its bullet on every cell layer's card.** A layer's
  per-directory card lists the rules anchored on that layer; a family-of-layers subject is
  anchored on each of its cells, so the one sentence appears on each cell's card. Nothing else
  about card placement moves — a union subject still places no card, and a family of projects
  places none.

- **`LayerDefinition.Globs` is the namespace region a layer names, which can now be none.** For the
  glob form it is the declared list, exactly as before. For a layer defined by a selection it is the
  namespace region that definition names — a namespace noun, or `arch.Types` narrowed by one
  `InNamespace` — and empty for every other definition, a project among them. Nothing else on
  `LayerDefinition` moves, and `DefinitionFragment` still carries the rendered row for any shape.

- **Breaking: the scope payload is named for what it now is.** `QuarantineData` is `ScopeData`,
  `QuarantineRole` is `ScopeRole`, `ArchRule.Quarantine` is `ArchRule.Scope`, and
  `ScopePlacement.ContainmentRule` is `ScopePlacement.Rule`. With a second scope posture,
  `rule.Quarantine is { Role: Tripwire }` was the wrong word at every reader of the payload, and the
  property is public on the hosting surface. No alias or shim. Machine-readable `posture` values gain
  `"caution"` (check and status JSON, SARIF rule properties) and check JSON's warning `kind` gains
  `cautionedScopeTouched`; both are additive, so no schema version moves. The quarantine message,
  skip reason, rule ids and clause names are unchanged.

- **A check warning is a SARIF result, and a tripwire declares itself a warning.** The SARIF
  renderer walked violations and grandfathered entries only, and gave every rule
  `defaultConfiguration.level = "error"` — so a tripwire uploaded to code scanning as an
  error-level rule that could never report, and the touches it did find reached the service
  through no channel at all. A warning now renders as a `warning`-level result on its rule, at the
  file it names (whole-file: the finding is that the file changed, so there is no region) or with
  no location where it names none, and a tripwire's descriptor declares `warning` beside it.
  Violations and grandfathered results are untouched.

- **The hook and CI recipes say to arm the tripwire.** `hooks/README.md` gains a CI section and the
  derive recipe a sentence: pass `--diff-base <the pull request's base ref>` in CI, or every
  quarantined scope's tripwire is skipped rather than clean and the scope fenced because it is
  dangerous to edit is the one thing the pipeline never mentions.

- **The Except clause closes.** "Types in the Host layer, except types named `SpecLoadContext`,
  must not use `AssemblyLoadContext.LoadFromAssemblyPath()`" — the comma after the exception is
  new. The clause is a parenthetical, and it opened with a comma and never closed, so "except [X
  must not use Y]" parsed as a clause and the exception read as the subject of the law. The
  sentence composer now closes it wherever text follows: before the verb, before a member
  subject's own clauses ("Methods of types in `MyApp.*`, except `SqlConnection`, returning `Task`
  must be named `*Async`"), before the final joiner of a list whose previous item ends open ("must
  not reference types in `MyApp.Legacy.*`, except `SqlConnection`, or `SqlCommand`") and before a
  verb phrase's tail; a sentence-final period, or a bracketed tail, closes it as before. Every
  `check` line, `--json` sentence, SARIF message and `explain` line reads the same way.
  Re-rendering updates every block whose subject carries an `Except`.

- **The ratchet now measures sites.** A baseline entry records how many sites it grandfathers,
  and a grandfathered pair that gains a site is red — every site of the pair listed, with a
  `grown:` trailer naming the count it exceeded. Before, an entry keyed a (source, target) pair
  and every site of the pair rode under it, so a second old-pattern site written inside an
  already-grandfathered type passed: new code in the old pattern was red only where the
  surrounding type was clean. The count is a measure beside the identity — `siteCount` on edge
  entries, out of entry equality (a grown pair is one entry that grew, not a stale entry beside
  a new violation) and in the digest (a hand-edited count is refused like any other edit);
  subject entries carry none. The file format is v2 (`"schemaVersion": 2`, digest preamble
  `loadbearing-baseline-digest-v2`) and every write composes it. v1 files are read exactly as
  before, their entries grandfathering the whole pair as they always did, and
  `loadbearing status` names them uncounted until `baseline --accept-reductions` records the
  counts — the same run that lowers a count whose sites have gone. Growth is accepted one way
  only: `baseline --add` on the existing entry re-records the observed count under its
  mandatory `--because`. `status` reports remaining sites beside remaining pairs, `check --json`
  carries `grandfatheredSiteCount` on a grown violation and `shrunk` / `uncounted` on the
  baseline, `status --json` gains `remainingSites`, and SARIF reports a grown pair's sites at
  error level as `updated`. GRAMMAR §4.3 states the measure.

- **`MustOnlyReference` and `MustOnlyBeReferencedBy` allow the rule's own subject.**
  `application.MustOnlyReference(domain)` permits an Application-to-Application reference and
  renders "must reference only the Domain layer". Saying that took
  `MustOnlyReference(application, domain)` before, and a sentence that spent a clause telling the
  reader a layer may talk to itself; every rule of this shape in the examples paid it. "Self" is
  the **refined** subject, the membership the rule ranges over after `Except`, so
  `arch.Types.WithSuffix("Controller").MustOnlyReference(domain)` stays a law rather than becoming
  a tautology: a controller may reach the Domain layer or another controller, and nothing else.
  The subject is read as one more allow entry, which is also what decides the co-declared case — a
  project-headed subject allows an intra-copy edge at the project it names the type at and nowhere
  else, the loosening mirror of 0.7.0's tightening on those same edges. The change can only turn a
  red rule green, never the reverse. Naming the subject explicitly stays legal and renders as
  written; a baseline entry covering an edge that is now allowed goes stale, and
  `loadbearing status` reports it with the rest. GRAMMAR §4.1 states the rule.

- **The Migrate paragraph no longer claims a majority.** The managed block's counter-prior bullet
  opened with "Most existing code here follows the OLD pattern", a fixed claim the reading model
  can measure and, late in a burndown, falsify; on this repository's own `mcp/env-through-seam`
  most of the subject is migrated already. It now opens "Some existing code here still follows the
  OLD pattern", true at any point of a migration. The rest of the paragraph — the debt sentence,
  the target law, the rationale and the boy-scout policy — is unchanged, and the magnitude stays
  where it is measured, in `loadbearing status`. Re-rendering updates the root block and any layer
  card carrying a Migrate rule.

- **A quarantine card's lede names what the scope covers.** The card file lands on a directory and
  `arch_context` hands it to anyone editing a sibling there, so "This directory holds the quarantined
  `demurrage/engine` scope." claimed the whole directory on behalf of a scope that may cover a single
  type in it. The lede now states the scoped selection the way every other reference position does —
  "…scope: the Demurrage layer." — which is what a caution card's lede has said since it was written.
  Both ledes now come from one composer, so neither posture can drift from the other. The three
  committed cards and the two walkthrough quotes re-render; no other line of any card moves, and
  placement is unchanged.

- **The `check` and `status` summary lines inflect their counts.** A run over one rule read
  "Checked 1 rules", and a run that drew one warning read "1 warnings". Both tails now read their
  nouns through the same inflection the per-rule lines already used, so a count of one reads
  "Checked 1 rule" and "(1 violation, 1 warning)". Only the singular case moves: every quoted
  `check` tail in the examples stands as it was, except the one warned run in the Meridian hooks
  storyboard.

### Fixed

- **`baseline --accept-reductions` and `--init` no longer write a file they put nothing into.** For a
  rule with no captured section, `--accept-reductions` says to run `--init` first and then wrote an
  empty, section-less baseline file in the same breath: noise in the tree, under a "wrote" line that
  contradicted the sentence above it. `--init` did the same for a rule it could not capture at all.

## [0.7.0] - 2026-08-26

### Added

- **A rule can take a project as its subject: `arch.Projects` and the packaging verbs.** The
  spec's third subject stratum, beside types and members: `.Named`, `.Matching` and `.Packable`
  select projects as build artifacts, and four verbs state the packaging laws —
  `MustOnlyTarget` (strict: a project's declared frameworks are a closed set, so there is no
  remainder for a caveat to disclaim), `MustReferenceNoPackages` (zero-arity: the empty list is
  the law it states; its sentence carries the honesty boundary, declared references only),
  `MustLockPackages`, and `MustNotBePackable`, with `.Must` over `IProjectInfo` as the escape
  hatch. The facts are evaluated, never parsed from the csproj XML: the lock policy a
  repository declares once in a shared props file and the `IsPackable` default nothing declares
  are both facts the raw file does not carry. A violation's site is whichever declaration
  actually won the evaluation — regularly a props file above the project — falling back to the
  project file where the defect is an absence, and a fact no load path could evaluate passes
  rather than reds: a rule firing on a project nothing evaluated would be reporting the load's
  own gaps as architecture violations. Identity is `project:{name}`, riding baselines
  unchanged, so every posture composes and a TFM migration is an ordinary ratcheted burndown.
- Four new rules over this repository's real code: `packaging/core-netstandard-only` (Core
  targets `netstandard2.0` alone — the one TFM a net48 spec project and the net10 host can
  both load), `packaging/core-carries-nothing` (the shipped package description's
  zero-dependency claim, checked for the first time), `packaging/shipping-locks-restore`
  (packable `Zphil.*` projects lock restore), and `packaging/only-the-four-ship` (exactly the
  four shipping packages are packable — the law the release pipeline held only by counting
  nupkgs). The six-lock-files arithmetic pin retires with them: fixtures fall out by not being
  subjects rather than by counting.
- **A fourth grain, `index`, below `skeleton` — the ladder's floor and the narrowing menu.**
  `--index` on `check` and `graph`, `index` on `arch_check` and `arch_graph`. The survey keeps every
  project's name, solution membership and type count; the report keeps every rule's ID, posture,
  status, baseline, warnings and violation count. What it drops is everything that scales with the
  codebase rather than with the solution or the spec, so a caller who lands here by degrading is
  holding exactly the list `--projects` and `--rules` globs pick from — and `explain` expands any of
  those rule IDs whole. Measured on a 56-project solution, the survey falls from 266,710 characters
  at `skeleton` to 6,258; on another, the report falls from 100,364 to 5,186.
- **`workspaceDiagnostics` elides at `index` too, to `workspaceDiagnosticCount`.** MSBuild's own
  words about the load are one entry per project per framework per complaint, so they scale with
  neither the spec nor the codebase but with whatever the load ran into — and they used to ride
  every rung untouched. On a bed whose package-audit feed happened to be unreachable that array was
  98% of the survey and 96% of the report *at every grain*, which no amount of coarsening elsewhere
  could fix. Every trust stamp still survives the floor rung: the actionable half of that stream is
  already keyed in `failedProjects` and `restoreFailedProjects`, and `modelIncomplete` still says
  the verdict was reached against a partial model. Only the raw text goes, and only at the floor.

### Changed

- **`Zphil.LoadBearing.Xunit` now requires `xunit.v3` 4.0.0, which runs on Microsoft.Testing
  Platform rather than VSTest.** The authoring pair the adapter references (`xunit.v3.assert`,
  `xunit.v3.extensibility.core`) moves to 4.0.0, and that raises the floor for consumers: a test
  project on `xunit.v3` 3.x hits a package-downgrade error rather than a subtle mismatch. Because
  4.0.0 ships MTP 2.x, which the .NET 10 SDK declines to drive through the VSTest target at all,
  what the version bump forces on a consumer is a change of runner: name it in `global.json`
  (`{ "test": { "runner": "Microsoft.Testing.Platform" } }`) and drop `Microsoft.NET.Test.Sdk` and
  `xunit.runner.visualstudio`, whose only purpose was the VSTest adapter. The adapter's own surface
  is unchanged — same `ArchRuleTests<TSpec>`, same overrides, same failure text — and filter
  expressions carry over, since `xunit.v3` accepts the VSTest filter syntax. Two behaviours that
  scripts read do move: a failing run exits 2 where VSTest exited 1, and a filter matching nothing
  exits 8 where VSTest reported success. The adapter README carries the consumer-side shape, and
  the `Meridian.Quoting` example is the worked copy of it.
- **Dependencies bumped:** `Basic.CompilerLog.Util` 0.9.53 → 0.9.56, the four `Microsoft.Build`
  engine packages 18.8.2 → 18.9.6 (unifying with `Microsoft.NET.StringTools`, already there), and
  `ModelContextProtocol` 2.1.0 → 2.2.0. The `github/codeql-action` pins move to v4.37.8 together,
  as the grouped update they are.

### Fixed

- **The unbound server's recovery now names a command the session reading it can run.** A server
  that cannot resolve a solution leads its reply with the CLI that answers from the same model, and
  that reply is read overwhelmingly by sessions launched from the MCP registry manifest — the one
  wiring that is unbound by construction, because the manifest has nowhere to put a solution. Those
  sessions had no `loadbearing` command: `dnx` runs the package without installing the tool, so the
  single remedy on offer was a command not found. Both spellings are named now, the installed one
  first and `dotnet dnx Zphil.LoadBearing.Cli@<version> --yes --` for the session that lacks it,
  pinned to the build answering so the promise of the same engine and the same verdicts holds.
  `dotnet dnx` rather than bare `dnx`, because on Windows the short form is a `.cmd` and a POSIX
  shell resolves a bare name to `.exe` alone. The discovery refusal quoted above the recovery
  stopped naming a runner at all, since argument order is what its parenthetical is for, and the
  `derive_spec` recipe now names the prefix once for the same population.

- **A reference between two types a linked source file compiles into several projects no longer reds
  a ban aimed at another declarer.** 0.6.0 made `arch.Project` membership N-way, so a project noun
  reaches every declarer of a shared file; the cure landed on selection and not on what an edge
  counts against. Where both endpoints were co-declared — a `NativeProviderLoader.cs` linked into
  three provider assemblies, a linked `AssemblyInfo`, a glob-linked shared file — `check` still
  attributed the edge to every project the *source* was declared by, so
  `mkl.MustNotReference(cuda, openBlas)` fired on a reference MKL's copy made to MKL's own copy. The
  report contradicted itself in the same document: its advisory note said each declarer's reference
  to its own compiled-in copy counts against that declarer alone, `graph` rendered no such project
  edge, and the rule red anyway — a red an adopter could not fix from the spec surface.
  An edge is now read per declarer of its source, each instance reaching that declarer's own copy of
  the target where it compiles one and the target's first declarer otherwise; the subject bounds
  which instances a rule owns and the operand decides the far end at each. `graph` reads the same
  instances, so the survey and the verdict cannot part company again. GRAMMAR §4.1 states the rule.
- **`graph` renders the outward edges of a shared file from every project that compiles it.** The
  survey read such an edge at the source's first declarer alone, so a shared type referencing a type
  only one project declares was rendered once and hidden for every other declarer. The mirror of the
  fault above, under-reporting where the checker over-attributed.
- **`MustOnlyReference` and `MustOnlyBeReferencedBy` are stricter on a shared file's own edges.**
  An edge between two co-declared types is allowed only by an entry naming the compiling project, as
  §4.1's no-implicit-self-allowance sentence has always said and the code did not. Before, an entry
  naming any declarer allowed it, so a spec that reads green today can red — the fix, not a
  regression, but it is the one place where this release can turn a passing rule red.
- **A large solution no longer exhausts the grain ladder and gets a cut document.** On a 56-project
  solution `arch_graph`'s coarsest survey was 66,593 characters against a 62,500-character client
  budget, and on another `arch_check`'s was 67,909 — every rung overran, so the response came back
  truncated: corrupt JSON under a footer naming a knob whose argument values were in the very
  document it had just withheld. Both tools now degrade to `index` instead, which grows with the
  project and rule counts rather than with the codebase — and, with the diagnostic stream eliding
  there too, both of those solutions now answer whole where they were previously cut.
- **Neither truncation footer offers to page the document out to a file any more, and the
  `derive_spec` recipe no longer suggests it either.** Both said, in effect, *redirect the CLI to a
  file and slice it there* — while the served server instructions say to narrow and never page. An
  agent handed the cut survey quoted the footer back as its reason for leaving the tool surface
  entirely and reading the whole document through the shell. Advice at the moment of failure
  outweighs a line in a system prompt, so the two must not disagree; the footers keep the knob they
  name, its CLI twin, and `arch_explain`.

## [0.6.1] - 2026-08-22

### Fixed

- **The README's count of the rules governing this repository is held to the board it renders.**
  It read `Eighteen rules` from 2026-08-01 through four published releases while the spec grew to
  thirty, and it sits in the sentence promising the page is spec-derived, so the claim that the
  page cannot rot was the part rotting. It now reads thirty, the second mention two screens down
  states no number of its own, and a new doc-hygiene gate holds any such claim to the rule bullets
  the committed `AGENTS.md` managed block carries. The board is the authority rather than the spec
  source: counting `arch.Rule(` misses the two the `DotNetGuidance` pack contributes and scores a
  quarantined scope as nothing. It is also not `check`'s count, which is thirty-one once that scope
  desugars into its two children.
- **The README no longer presents `context` as a command-line verb.** It listed `context` beside
  `check`, `status` and `graph` and wrote `context --path`, but there is no such verb: the surface
  is the `arch_context` MCP tool, as `loadbearing --help`'s seven verbs already said. Both mentions
  now name the tool. The behaviour described was right; only the surface was misnamed.
- **The `derive_spec` recipe teaches the whole shipped vocabulary again.** Six verbs had shipped
  without the recipe ever naming them — `MustNotSwallow` on the exception axis, and 0.6.0's
  `MustBelongTo`, `MustResideInProject`, `MustBeRegistered`, `MustBeGetOnly` and `MustBeReadonly` —
  along with the member-side attribute vocabulary and the `.ThatAreStatic()` adjective. Each is
  taught now, beside its axis; the exception-verb tallies in the semantics notes read five and
  three to match; and the closing section routes to GRAMMAR.md, which the recipe had never named,
  so the condensed reference finally says where the full grammar lives. A new test holds every
  shipped `Constraint`-returning verb name to the recipe, so the next verb cannot ship untaught
  unless a registry entry says why it is withheld.
- **The `derive_spec` troubleshooting list catches up with the incomplete-model refusals.** The
  two refusals — projects that failed to load, and projects whose NuGet packages did not
  resolve — are the ones a broken tree actually produces, and the list had entries for
  neither. Both are catalogued now, each with its own remedy. The load-diagnostics entry also
  stops offering a locked-mode `NU1004` as its usual cause: since the restore refusal learned to
  blame projects itself, that shape lands there instead, and what remains on the diagnostics arm
  is load problems that blame no project in particular.

## [0.6.0] - 2026-08-22

### Added

- **`render`, `explain` and `baseline --add` now answer from the extraction cache, and take
  `--no-cache`.** Only `check`, `status` and `graph` could reach it before, so on a 35-project
  solution the other verbs paid a full extraction every time — roughly 105 seconds where a cache hit
  takes 5. The old rule sorted verbs by what they do with the model; the rule now is what they read
  *absence* as. A verb that reads presence — a violation it saw, a rule it found, a card it can
  place — is at worst wrong in a way the next run corrects. `baseline --init` and
  `baseline --accept-reductions` read absence as evidence: they turn "not in the model" into "no
  longer happening" and write that into a file that outlives the run. Those two modes therefore
  extract cache-free whatever the flag says, which is the same reasoning that already makes them
  refuse a partial load or a solution filter — a stale cache is a third route to the same
  smaller-than-real model, and the only one of the three that raises nothing for those refusals to
  fire on. `baseline --add` records one violation the run did see, so it rides the cache like the
  rest. Output is unchanged on every path: a hit replays the recorded diagnostics and merges the
  same fragments, so a cached run is byte-identical to a cold one.

- **The survey now counts how much of a project a generator wrote.** `arch.Project("Nop.Web")` is
  the obvious way to name a web tier, and on a real one it takes 803 compiled Razor views into its
  subject beside a handful of hand-written types, so naming and suffix rules report against code
  nobody can fix. `generated` now qualifies the type count on each project and each namespace in
  `graph --json` and in the human survey, where a wholly generated namespace reads `all generated`
  — the shape that must never become a layer glob. The key is absent at zero, so `schemaVersion`
  stays 1 and a solution with no generator output has the survey it had before. `.Authored()` also
  works on these types for the first time: detection used to read `[GeneratedCode]` and nothing
  else, and a compiled view carries a banner and no such attribute, so all 803 reported as authored
  and the documented cure narrowed away none of them. A type is now generated when the attribute
  sits on it or on a containing type, or when *every* file declaring it is generator output — a
  source-generated document, or a file led by an `<auto-generated>` banner. Every file, not any:
  `[GeneratedRegex]` writes the author's own partial class into a banner-carrying file beside
  wholly generated ones, and an any-file rule would put that class beyond the rules written for it.
  Nothing is inferred from a file path. `schemaVersion` does not move — the fact is widened, not
  added — but the extraction cache's own schema does, so the first run after upgrading is cold.

- **A check report now says when a rule is aimed at generated code.** A rule whose subject
  contained generator output carries `subjectTypes` and `subjectGeneratedTypes` in `check --json`,
  and a `subject: 804 types, 803 generated` line in the human report, for passing and failing rules
  alike: a green rule aimed at code nobody wrote is as misaimed as a red one and has less to draw
  attention to it. Both numbers, because "803 of 804" and "803 of 90,000" are different findings.
  It states a fact and gives no advice — `.Authored()` is often the right narrowing and sometimes
  wrong, since a generated JSON context genuinely must not write to stdout. The pair is measured on
  the subject set the verbs range over, so it extinguishes itself: narrow the rule and both keys
  disappear. It is not a warning, and nothing about it reaches SARIF, which has no result location
  to hang a subject count on.

- **Every document now states the projects it could not read.** LoadBearing surveys and checks C#
  projects only, and until now a solution holding an `.fsproj`, a `.vbproj` or a `.sqlproj`
  produced a shorter `projects` array with nothing to say about the difference — so a spec derived
  from the survey could silently fail to reach a shipped product surface, and a clean `check` over a
  polyglot solution read as a clean solution. `unsupportedProjects` now carries each declared
  project this product cannot read, with its reason, in `graph`, `check` and `status` `--json`, in
  the matching MCP documents, and as a fourth SARIF notification. It is read from the solution file
  rather than the workspace, so it is complete where the loaded solution is not: `.fsproj` is the
  only non-C# kind that reaches a workspace at all. It never gates — a project this product cannot
  read makes the model smaller than the solution, not wrong about it, so it takes
  `uncheckedProjects`' posture rather than `failedProjects`'. The key is absent for an all-C#
  solution, so every schema version is unchanged and an all-C# document is byte-identical to the
  one it was before. The human `graph` survey gains a matching section reading `(none)` when there
  is nothing to report, and `check` and `status` gain a one-line stamp above their answers.

- **The survey now names the type names a referenced assembly also supplies.** `shadowedTypes`
  carries the name, the project declaring it, the assemblies supplying it, and the projects whose
  references reach the assembly's type instead, so a rule author can see before writing a rule that
  the name means two things. The key is absent when a solution has none, so `schemaVersion` stays 1
  and a healthy solution's survey is byte-identical to the one it was before. At skeleton grain it
  elides to `shadowedTypeCount`, beside the multiply-declared rows it sits with. The human survey
  gains a matching section, reading `(none)` when there is nothing to report.

- **The survey now names the types that more than one project declares, and which project's facts
  they follow.** `multiplyDeclaredTypes` carries the type, every project declaring it, and the one
  whose facts and project attribution it follows, so a rule author can see before writing a subject
  which project's compilation a rule over that type answers from. `check` has reported the
  same fact as a per-type advisory note since the merge notes shipped; this is the same content in
  the document where subjects are actually planned. The key is absent
  when a solution has none, so `schemaVersion` stays 1 and a healthy solution's survey is
  byte-identical to the one it was before. At skeleton grain it elides to
  `multiplyDeclaredTypeCount` the way the external-reference rows elide, because its length scales
  with the codebase rather than the schema.
  The human survey gains a section beside the observed references, reading `(none)` when there is
  nothing to report and one line per type when there is.

- **Both documents now state a multi-targeting project's frameworks, and which one's facts its
  shared types carry.** A multi-targeting `.csproj` compiles once per framework, and extraction
  takes every compilation: types, references and solution membership union losslessly under the
  one project name. The exception is a type more than one framework declares — it carries one
  framework's edges, members and hierarchy: the first extracted, in ordinal order of the framework
  name, never the csproj's declaration order, so anything a losing framework's `#if` guards is not
  in the model and a rule about that type answers from the winning compilation alone. The survey's
  project row gains `targetFrameworks`, every framework in that order, and `factsFollow`, the
  winner — present only when the frameworks actually share a type, because a project whose
  frameworks share nothing displaced nothing and every type keeps its own framework's facts. The
  pair rides the row the way `generated` does, surviving every grain rung, and the human survey's
  project line gains a `targets net10.0, netstandard2.0 (shared types from net10.0)` clause.
  `check --json` gains the matching top-level stamp, `multiTargetedProjects`, beside
  `unsupportedProjects` and with its posture: every rule still ran and answered, over one
  framework's view, so it scopes the verdict and never touches `modelIncomplete`. It is the keyed
  form of the advisory sentence `check` already prints; the sentence is unchanged, and the stamp
  is wider, naming every project that compiled more than once rather than only those where two
  frameworks collapsed a type. Both keys are absent for a single-framework project, so every
  schema version is unchanged and a single-framework solution's documents are byte-identical to
  the ones before.

- **Three membership verbs close the ungoverned-remainder hole: `MustBelongTo`,
  `MustResideInProject` and `MustBeRegistered`.** A type added outside every declared layer used
  to be silently lawless — no rule swept it, no card covered it, and `check` stayed green.
  `subject.MustBelongTo(domain, web)` renders "must belong to the Domain layer or the Web layer"
  and reds every subject type no membership names; this repository's own spec now proves its five
  shipping projects carry no ungoverned remainder. `MustResideInProject("MyApp.Web")` is the
  residence twin: any declarer of a multiply-declared type satisfies it, so the verb agrees with
  `arch.Project`. `MustBeRegistered()` demands a type's registration in the DI container, with
  exactly `arch.Registered()`'s membership — and a registration the extraction cannot see
  (assembly scanning, keyed overloads, raw `ServiceDescriptor`) reds a correctly registered type,
  so an estate that registers by convention should not take the verb. Violations are per-subject
  shape verdicts carrying the type's declaration sites as evidence, and a blank project name now
  refuses at spec build on the noun and the verb alike, closing `arch.Project("")`'s silent
  acceptance.

- **Two member mutability verbs open the state axis: `MustBeGetOnly` and `MustBeReadonly`.**
  A rule whose real subject is "the members something can write to" had no way to say so. The
  model carried a member's name, kind, accessibility and async-ness and nothing at all about
  whether it could be assigned, so "value objects are immutable" and "no static mutable state"
  were inexpressible even through the escape hatch, which can only read facts the model has.
  `domain.Properties.MustBeGetOnly()` renders "Properties of the Domain layer must be get-only"
  and reds every property declaring a setter; `core.Fields.ThatAreStatic().MustBeReadonly()`
  renders "Static fields of the Core layer must be readonly", the new `.ThatAreStatic()` member
  adjective narrowing the subject to the statics. This repository's own spec now holds every node
  a spec author can name get-only, and its writable statics down to seven named locations.
  Two semantic decisions, both taken rather than fallen into: **an `init`-only setter reds under
  `MustBeGetOnly`** — get-only is a claim about the declaration, not about when the write is
  allowed to happen, so a positional record's generated `{ get; init; }` fails the verb — and
  **a `const` field passes `MustBeReadonly`**, const being readonly's superset, so the law asks
  for the weakest thing that closes the hole. Both are honest about their reach: they read the
  shape of a declaration, not deep immutability, so a get-only property whose type is itself
  mutable still hands the caller something it can write through, and `HasSetter` is
  accessibility-blind — a `private set` is a setter. Nothing about identity or document format
  moves: a member violation keys the member's own `DocumentationCommentId` exactly as before, so
  existing baselines keep matching and every `--json` and SARIF schema version is unchanged. The
  extraction cache's own schema does move, 24 to 25, which degrades to a clean miss — the first
  run after upgrading is cold, and nothing else about it is observable. `Selection.Properties`
  and `Selection.Fields` now return `PropertySelection` and `FieldSelection` rather than
  `MemberSelection`: every existing chain compiles unchanged, both types belonging to the same
  hierarchy, but a more derived return type is a binary-breaking change, so a spec assembly built
  against an earlier package needs a recompile rather than an edit.

### Changed

- **`arch.Project` now reaches every declarer of a type compiled into several projects, and an
  edge into such a type counts against the project that compiled the copy.** Where one source
  file is compiled into N projects — a `<Compile Include>` link, shared source, a polyfill — the
  model keeps one node attributed to the first declarer, and rule evaluation used to read that
  attribution as the whole truth: a subject anchored on any other declarer missed the type, and a
  project referencing its own compiled-in copy read as a cross-project edge into the first
  declarer. On the field test's MathNet leg that forced three `.Except` workarounds onto real
  `MustNotReference` rules, excepting a namespace to silence edges no project file declares.
  Membership is now N-way — `arch.Project` naming any declarer selects the type, as subject,
  target, member subject, or union operand — and an edge into such a type counts against the
  intersection of the two endpoints' declarer sets when it is nonempty (the source compiled its
  own copy: intra-project), the first declarer otherwise. The same rule covers a project-headed
  subject of `MustNotBeReferencedBy` and `MustOnlyBeReferencedBy`, and an edge's source counts
  for every declarer. Non-project operands — `typeof`, `arch.Namespace`, `arch.Types`,
  `arch.Layer`, `arch.Registered`, `MustNotUse`'s member anchors — stay attribution-insensitive,
  so a `typeof` ban still reds on a project's own copy; `MustOnlyReference` stays strict, so an
  intra-copy edge needs an allow entry naming the compiling project or a non-project entry
  containing the type; `.Except` subtracts by node identity, removing a co-declared node from
  every declarer's selection. The check report's per-type advisory note states the consequence
  ("selections include it too, and each declarer's reference to its own compiled-in copy counts
  against that declarer alone"). A solution with no linked source checks byte-identically — no
  schema moves, and the extraction cache is untouched. A solution with linked source sees
  verdicts move: baseline entries that covered phantom self-edges go stale (symbol IDs name a
  name, not a node, so every other committed entry still resolves — the normal stale mechanism
  prunes), previously-missed types come into view for project-anchored subjects, and
  `MustNotBeReferencedBy` over a co-declaring project can newly red on the shared type's own
  edges — fix or re-baseline as with any widened rule.

- **A shared project is no longer reported as a language this product cannot read.**
  `unsupportedProjects` stamped one reason, `not a C# project`, onto every entry, while the two
  places that produce the set each knew more: the solution parser tests the extension, and the
  binlog replay reads whether a compiler call was C#. That single sentence was wrong about the
  `.shproj`. A shared project is language-neutral — a container whose `.projitems` files compile
  into every project that imports it — so its code is very often C#, and where an importing project
  is in the model that code is in the model with it. Each producer now carries its classification
  through to the surfaces, and a shared project reads `a shared project, compiled into the projects
  that import it` wherever the key or its human twin appears: `graph`, `check` and `status`, in
  `--json` and in the terminal, and in `check --sarif`'s coverage notification. The wire
  shape is unchanged — still `{project, reason}`, no new key — so every document holding no
  `.shproj` is byte-identical to the one before, and only the extraction cache's own schema moves,
  which makes the first run after upgrading cold. The sentence above the entries drops its
  hardcoded cause with them: the human stamp now reads `1 project the solution declares was not
  surveyed` and the survey's section is headed `Projects the solution declares that this survey does
  not cover`, leaving each entry to say why for itself.

- **The xUnit adapter's completeness test now states unsupported projects too.**
  `Workspace_LoadedCompletely` reported an incomplete model and a narrowing filter, and said nothing
  about a solution declaring projects no extractor reaches — so a polyglot solution's rule tests went
  green with no coverage statement anywhere, while `check` and `status` both stamped one. It now
  skips carrying the projects and their reasons, exactly as it does under a filter: the rule verdicts
  are real and keep reporting, but a test by that name cannot claim the whole solution. This is a
  behaviour change for a consumer whose solution declares an `.fsproj`, a `.vbproj` or a shared
  project — a test that passed now skips, carrying the reason. A solution that is both filtered and
  polyglot states both causes.

### Fixed

- **An unbound MCP server now leads with a recovery the reader can actually perform.** A server that
  cannot resolve a solution starts anyway and says why, and the two remedies it named (put the
  solution in the client config's `args`, or set `LOADBEARING_SOLUTION_PATH` in its `env`) are both
  edits an agent inside that session cannot make. The one it can make, the CLI with the solution
  named, appeared only inside the quoted discovery error. On a multi-solution repository wired from
  the registry manifest that is exactly how a session reads it: the agent drove the CLI throughout
  and called none of the five advertised tools. The banner now names the CLI first and the config
  edits after it, as the fix for the next session rather than this one, and every tool call's error
  result carries the same sentence, because the banner is delivered once at a handshake nothing
  re-reads. Errors bypass the response truncator, so no budget can trim the advice away. The tools
  still take no solution argument and the server is still bound at start: what changed is which
  recovery is legible from inside a session that cannot rebind it.
- **The registry manifest and the README now say where a registry-wired repository binds its
  solution.** A config generated from `.mcp/server.json` passes no solution argument, so a
  repository the walk-up cannot resolve gets an unbound server and a session that works from the
  CLI, and nothing in either document said so: the README's existing routing to the installed tool
  keys on a repository's SDK pin, which is a different condition. Both now state that such a
  repository is bound at install time, in the config's `args` or through
  `LOADBEARING_SOLUTION_PATH`. The manifest still serves the repositories that can use it and the
  prose still carries the boundary; the boundary is now written where an installer meets it.
- **The derive recipe now expects `dotnet sln add` to rewrite more of a solution than the one project
  it adds.** The CLI unions its default platforms into a `.sln`'s configuration list and writes the
  project-configuration table out complete, so adding one spec project to a dozen-project solution
  inserts a hundred-plus lines — which, against a recipe that closes by asking for one reviewable
  diff, read as damage. The repair that reading invites is worse than the diff: revert the rewrite,
  hand-write an entry mapping only `Debug|Any CPU` and `Release|Any CPU`, and the project registers
  while having no mapping under any other combination the solution declares. A solution build under
  one of those skips it behind an `MSB4121` warning and still exits 0, so the build stays green
  while `check` reads a stale spec assembly, or none at all. The scaffold step now states that the
  rewrite is legitimate and says not to hand-trim it, and the closing step names the solution file
  as often the largest hunk in the single diff it asks for.
- **A solution whose C# projects all restored is no longer refused because a project in another
  language did not.** The restore detector walked every project the workspace loaded, including the
  `.fsproj` that reaches it, and blamed one whose packages are missing — but those packages cannot
  affect a model that never contained the project, so the run refused a healthy polyglot solution
  and named a project no `dotnet restore` of the C# estate would fix. Both structural detectors now
  skip projects this product cannot read. A visible consequence: an unrestored polyglot solution's
  refusal now names only its C# projects, which is a smaller and more accurate list than before.

- **A stand-in declared under a package's namespace no longer steals product code's references to
  the package.** Extraction unified types by fully-qualified name, and a source declaration beat a
  referenced assembly's type of that name everywhere — so a test project declaring
  `AvalonDock.DockingManager` as a mock, the ordinary test idiom, took every product reference to
  the real `AvalonDock.DockingManager` with it. On ILSpy that made a product-must-not-reference-tests
  rule report two violations against a test assembly no product project has a reference path to, one
  of them in generated XAML, and an accept-all `baseline --init` froze both as day-zero debt. A
  reference now resolves to whichever type the referencing compilation actually bound: the model
  carries both, the declaration and a shallow external attributed to the supplying assembly, and the
  split is decided by assembly identity rather than by which project happened to declare the name
  first. Every unknown fails open, so a fragment that cannot name its own assembly changes nothing.
  The reference the product makes is kept rather than dropped, which means a rule aimed at the
  package's own type still sees it, and the hierarchy axis resolves the same way, so a type deriving
  from the package no longer reads as deriving from the mock. `check` reports the split as an
  advisory note, one line per declaring project. A name a solution declares that nothing outside it
  supplies is unaffected, as is a name compiled into several projects from one file — that stays one
  node, and `arch.Project` on the other declarers still misses it.
  The extraction cache's schema moves with the new fact, so the first run after upgrading rebuilds it.

- **The survey no longer reports a project as referencing another when it compiles that project's
  source itself.** Where one file is compiled into several projects (a linked `<Compile Include>`,
  shared source, a polyfill), extraction attributes its types to the first declaring project, and
  everything downstream reads that single attribution as the truth. A project reaching its own
  compiled-in copy therefore resolved to a node stamped with another project's name, so `graph`,
  `graph --json`, `arch_graph` and `render --diagram` all drew an edge no project file declares.
  Measured on Math.NET Numerics, whose native-provider loader is linked into all three provider
  projects: two invented edges, and a spec drafted from that survey carried three `.Except` clauses
  whose only job was to work around them. A reference into a type the referencing project declares
  itself is now dropped, and only that. The survey groups one entry per type pair, so a genuine
  reference between the same two projects survives with its count untouched, and the attribution
  behind the suppression is stated rather than left silent (see `multiplyDeclaredTypes` above). Rule
  evaluation still resolves selections by single attribution, so a rule anchored on a project that
  loses the attribution is unaffected by this change.

- **A check rule now states its violation count at every grain, and a violation its site count.** A
  per-rule entry in `check --json` carried either `violationCount` or `violations[]`, never both —
  the count only where the grain had elided the array — and a violation the same for `siteCount`
  and `sites[]`. Nothing said the keys stood in for each other, so the ordinary defensive read,
  `rule.get('violationCount', 0)`, answered 0 for a rule whose status is `failed`:
  absent-means-zero, silently wrong in the unsafe direction, against the very key the derive
  recipe tells a consumer to script on. Both counts are now written unconditionally, each ahead of
  the array it summarizes, so a document a reader truncates inside the bulk has already delivered
  the number. `schemaVersion` holds at 3 — a key addition, not a change in what any verdict means —
  and a skeleton report is byte-identical to before; full and overview reports grow by the counts.

## [0.5.0] - 2026-08-17

### Added

- **`graph` now says which of the projects it surveys the solution actually declares.** A workspace
  loads every project a `ProjectReference` reaches, which is a wider set than the solution file names:
  a spec project's references drag its contract library — and, in an example repository, the
  LoadBearing packages themselves — in as passengers, and until now nothing on any surface told them
  apart from the codebase under law. Each project in the survey carries `solutionMember`, the text
  roster annotates the undeclared with `(not a solution member)`, and `arch_graph` inherits both. The
  survey keeps every project either way — investigating a passenger is exactly what the survey is
  for, and a fence that hid them would leave a reader wondering why the counts disagreed with their
  solution. The label is threaded onto the model at extraction rather than joined on afterwards,
  because a cache hit replays a model with no live workspace to join against, and the extraction cache
  schema is bumped so a manifest written by an earlier build rebuilds rather than replaying an
  unlabeled model as a labeled one — a one-off local rebuild, on disposable derived data. Membership
  that cannot be read labels nothing: an unowned solution format or a parse failure leaves the key
  absent, which is why absent means unknown and never `false`. `schemaVersion` stays 1 and the key is
  optional, so a consumer of a solution whose membership reads cleanly sees one new key and nothing
  else moves.

- **An over-budget check report now comes back whole at a coarser grain instead of cut in half.**
  `check --overview` elides each violation's sites, reporting them as `siteCount`; `check --skeleton`
  elides the violations themselves, reporting them as `violationCount` and keeping every rule with its
  verdict, prose, baseline and warnings. The `arch_check` MCP tool takes both as parameters and, over
  the client's `MAX_MCP_OUTPUT_TOKENS` budget, walks down the ladder on its own until a whole document
  fits — byte-identical to what that grain's own flag writes, so a reader can trust the `grain` stamp
  rather than diffing two reports. `arch_graph` has degraded this way since 0.4.0; check is the tool
  agents are told to call before finishing work and its bulk driver is an uncapped per-site dump, so it
  is the response most likely to overrun on a large migration — and a report cut mid-array is corrupt
  JSON that costs a client the whole tool surface, not merely some detail. What survives every rung is
  chosen rather than incidental: rule prose scales with the rule count, which is authored and small,
  while sites scale with the codebase, which is what actually overruns a channel — so the coarsest
  report is still a verdict a reader can act on. `schemaVersion` stays 3 and the three new keys are
  absent at full grain, so a consumer that never asks for a coarser report sees the same bytes it
  always did. Underneath, the ladder stopped being graph's private loop: both verbs now offer their
  rungs to one `IResponseFitter` that lives beside the truncator, the budget is one service read per
  call, and the transport concern that used to be threaded through a CLI request record — with every
  parse path passing a `null` it had to explain — is gone.

- **A run narrowed by a solution filter now says so on every surface.** `check`, `status` and `graph` stamp which declared projects the filtered run never checked, and the JSON documents carry them as `uncheckedProjects` beside `failedProjects` — absent when nothing was narrowed, so unfiltered output does not move a byte. SARIF carries one warning-level tool notification, and the `arch_check`, `arch_status` and `arch_graph` MCP tools inherit the slot, which the stdout stamp could never reach. The set is measured from the load rather than read from the filter: a selection whose transitive project references pull the rest of the solution in narrows nothing and says nothing. `context` writes the same caveat above its answer. `baseline --init`, `baseline --accept-reductions` and `render` refuse under a narrowing filter — exit 2, nothing written: a baseline captured through a filter signs off debt in projects it never measured, `--accept-reductions` would delete real entries, and rendered files would silently drop every card from an unchecked project. `baseline --add` keeps working. In the xUnit adapter, rule cases keep their verdicts — a narrowed universe is a smaller true answer — and `Workspace_LoadedCompletely` reports as skipped, naming what was not checked, rather than pass under a name the filtered run cannot vouch for. A filtered run anchors every convention-relative path at the solution the filter references rather than at the filter's own directory — committed baselines, render targets, `context --path`, diff resolution, and the stamped project paths, which read solution-relative rather than `../`-prefixed — so a filter that narrows nothing answers exactly as its solution does, instead of missing the committed baseline and failing rules that are green over the whole solution. And a rule all of whose findings are the empty-selection defaults skips under a narrowing filter, with one line naming the filter and the unchecked count, rather than reding as a typo'd spec: a filter that erases a rule's whole subject no longer turns a green spec red, while unfiltered runs keep the fail-closed empty-selection defaults exactly as they were.

- **The four example solutions now ship their architecture as a committed drawing.** `Meridian`,
  `Meridian.Quoting`, `Meridian.Operations` and `Meridian.Interchange` each carry an `ARCHITECTURE.md`
  holding the pair this repository's own root file does: a codebase survey drawn from what the code
  does, beside a law fence drawn from what its spec forbids. CI renders all four and folds them into
  the same single zero diff across `examples/` that already gates the agent-context cards, so a
  drawing cannot go stale without the build saying so, and a shape gate over the committed files runs
  on every OS because that examples job is ubuntu-only. Every render line carries
  `--diagram-only "Meridian*"`: the survey fence extracts with no exclusions so that it agrees with
  `graph`, so unscoped each example would draw the LoadBearing projects its spec references under a
  caption reading "Projects in this solution". What the four are *for* differs, which is the point of
  shipping all of them. Meridian puts three postures on one page as shapes — a solid ban, two dotted
  grandfathered arrows into `SqlConnection` and `SqlCommand`, and a quarantine box entered only
  through its doubled facades. Meridian.Quoting is the tidiest law in the set with the emptiest
  fence, because seven of its nine rules are about a name, a shape or an attribute rather than a
  direction. Meridian.Operations is a two-node survey beside a ten-node law: its modules are
  namespace subtrees inside one project, so MSBuild cannot see them and the law fence *is* the module
  map. Meridian.Interchange is the honest-limits page, where eleven of twelve rules are canonical
  .NET guidance with nothing to draw and the list under the fence is the law.

### Changed

- **Breaking: each project's root namespace now holds only the types callers name.** A spec author writes
  `using Zphil.LoadBearing;` and dots, and what came back was everything the assembly happened to declare —
  the model the host builds from a spec, and the intermediates a fluent chain only passes through — none of
  which an author ever spells. The root now holds the authoring surface and nothing else: the entry point and
  the spec interface, the nouns and enums written as arguments, the five static classes carrying the verbs,
  and `IRuleBuilder`, `IScopeBuilder`, `Member` and `Selection`, which authors do write in type position.
  Everything else moved to `Zphil.LoadBearing.Fluent` (`IEnforceRule`, `IMigrateRule`, `IQuarantinedScope`,
  `MemberSelection`, `MethodSelection`, `NamespacePattern`, `TypeNamePattern`) or `Zphil.LoadBearing.Hosting`
  (`ArchRule`, `ArchitectureModel`, `ArchModelBuilder`, `LayerDefinition`, `MigrateData`, `QuarantineData`).
  **Most specs compile unchanged**: member resolution on a return type ignores using directives, so a chain
  keeps flowing through types nobody imports, and the extension-method holders stayed in the root precisely
  because extension lookup is the one thing that does need the declaring namespace in scope. A spec that
  names a moved type in a helper signature adds one using. `Zphil.LoadBearing.Roslyn` and
  `Zphil.LoadBearing.Cli` got the same treatment — neither is a referenced surface, so the move costs nothing
  at the boundary and buys a project root that shows the seam instead of hiding it among the machinery behind
  it. Three new self-enforced rules hold all three roots to their lists, so the next arrival reds where it is
  made rather than accumulating quietly.

- **Breaking: a spec assembly built against 0.4.0 or earlier must be rebuilt against this version.** A
  compiled spec emits a type reference per chain, and `IEnforceRule`, `IMigrateRule` and `IQuarantinedScope`
  are the return types of `.Enforce`, `.Migrate` and `.Quarantine` — so every posture chain in an older spec
  DLL now names a type that is no longer where the reference says it is, and resolution fails when the host
  loads it. The pinned `AssemblyVersion` is what lets a spec built against one release run on another; it
  cannot carry a type across a namespace. Rebuilding the spec project is the entire fix, and the failure the
  host reports already names the member that could not be found and what to do about it.

- **Breaking: `RuleBaseline.Contains` is gone — `TryMatch` answers the same question and hands back the
  entry.** Membership was answerable two ways, and only one of them could return what it had matched. The
  shorter probe gave a bare yes, and with it went the `because` a baseline stores beside a grandfathered
  entry — the attribution a report carries so that a ratcheted violation says why it is being carried.
  Nothing at the call site looked lossy, which is what makes this worth removing rather than documenting:
  the cheaper-looking of two spellings was the one that dropped evidence. `TryMatch(entry, out _)` is what
  `Contains(entry)` was, so a caller that genuinely wanted the boolean changes one word. This is public
  surface on the contract package every spec project references, which is why it is called out as a break
  at all; it is not front-door surface, and a spec author never had reason to name it — a baseline is read
  by the host, not written into the sentences an author writes.

- **The check report now leads with its verdict.** `check --json` and `arch_check` serialize `summary` and
  every trust stamp — `modelIncomplete`, `failedProjects`, `restoreFailedProjects`, `uncheckedProjects` —
  above `rules`, which used to carry all of them below it. A reader with a response budget cuts at the last
  newline that fits, and past the grain ladder's coarsest rung that cut lands inside `rules`, the bulk of the
  document: the roll-up and every caveat that stops a report reading as plain green were the first things
  lost, and check only overruns when it is red-heavy — exactly when the verdict matters most. Other
  harnesses' limits are not knowable from here, so most-important-first is the right shape regardless. The
  stamps precede `summary` so a caveat never arrives after the counts it invalidates, and
  `workspaceDiagnostics` stays last on purpose: it is MSBuild's evidence rather than the verdict, it has no
  ceiling, and the actionable half of it is already hoisted into `failedProjects` and
  `restoreFailedProjects`. Key order and nothing else — `schemaVersion` stays 3, nulls stay omitted, and a
  clean document differs from the one before it only in the sequence of its keys.

- **`render --diagram`'s survey fence now draws the projects the solution declares, so its caption is
  literally true under any flags.** The fence extracts with no exclusions, which is what keeps it
  agreeing with `graph` — but a workspace also loads whatever a `ProjectReference` reaches, so an
  unscoped drawing of any codebase with a spec project put that spec's contract library on a page
  captioned "Projects in this solution". The remedy was a scope glob on every render line, which meant
  the taught recipe carried a flag whose job was correctness rather than legibility, and an adopter
  who left it off got a wrong drawing with nothing to tell them so. `--diagram-only` and
  `--diagram-exclude` now narrow *within* the declared set and are what they read as: legibility knobs
  for a solution with more projects than a diagram can carry. There is no render-side way back in —
  `graph` is where a passenger is investigated, and it labels every loaded project. Membership that
  could not be read filters nothing, so an unparseable solution file degrades to the old drawing
  rather than to an empty one; an all-passenger survey keeps the existing `(no projects in scope)`
  placeholder. The four example solutions drop `--diagram-only "Meridian*"` from their render lines
  and their documented redraw commands, and every committed drawing — theirs and this repository's —
  re-renders byte-identical, which the zero-diff gates prove rather than assert.

- **The string anchor is now stated where an author is reading, and demonstrated where an adopter is
  looking.** The shared-framework load failure already offered `.DerivedFrom("…ControllerBase")` as its
  remedy, but `derive_spec`'s authoring reference — the section that page calls canonical — listed only
  the `typeof` and generic forms, so an agent reading it top to bottom learned the hatch existed only by
  also reading the error list. The adjectives block now carries the string overload beside each `typeof`
  form, with a paragraph on when to reach for it and the note that a generic `<T>` twin needs the same
  compile-time reference a `typeof` does. The Meridian example's `naming/controllers` rule moves from a
  namespace pattern to `arch.Types.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")`: its spec
  project is a plain class library with no `<FrameworkReference>`, so it is exactly the position that
  cannot write the `typeof` — and the rule now says what it means (a request handler is a `ControllerBase`
  deriver, not a folder) while selecting the identical eight controllers. GRAMMAR §5.2's
  "the hierarchy adjectives never match an external type" said no position, and reads naturally as the
  claim that a shared-framework anchor is pointless; it now names the **subject** position and states that
  a declared type's base chain and interface closure are walked straight through metadata, so an external
  anchor matches — including through an intermediate external base.

- **A warm MCP server resolves the spec once per workspace load rather than once per tool call.** The
  server holds a loaded workspace across calls, but spec resolution ran again on every one of them —
  re-walking the discovery convention, re-reading the project, re-probing the built output — to arrive at
  the answer the call before it had already reached. It is resolved once per load now and reused until
  that load is replaced, and the paths the resolution walk has already resolved are carried rather than
  derived a second time downstream. No answer moves: it is the same walk against the same disk,
  reconciled when the workspace is, so a rebuilt spec is picked up exactly as it was before. What this
  buys is felt where the cost was paid — a per-edit `arch_check` hook, which calls a warm server on every
  write and had been re-deriving a settled answer each time.

### Fixed

- **An agent hook now checks the working tree the edit landed in, not the tree the session happens to
  be sitting in.** Claude Code fires a `PostToolUse` hook from the session's directory and names the
  project root in `CLAUDE_PROJECT_DIR`, and neither has to be where the edit went. An agent working
  inside a linked worktree, and a session in the main checkout writing into one by absolute path,
  both bought a full `loadbearing check` of a tree their edit never touched and read back a verdict
  about the wrong code, on every matching write. Each wrapper now resolves the tree that contains the
  edited file and returns early when that is not the tree `CLAUDE_PROJECT_DIR` names, reading the
  file path out of the payload it already parses for the code filter. Neither cheaper test can stand
  in for the git one. Path containment cannot, because Claude Code puts a worktree *under* the
  project directory, so by path it reads as inside; and comparing the two `.git` directories cannot,
  because git spells them absolutely from the repository root and relatively from a subdirectory of
  it, which makes that comparison answer differently depending on where the session sits.
  `rev-parse --show-toplevel` is absolute, identical from any depth, and names a linked worktree's
  own root, so one comparison settles it. Path containment still runs first, before any process is
  spawned, and is what catches an edit to a file in no repository at all. Every remaining fallback
  runs the check rather than skipping it: an unset `CLAUDE_PROJECT_DIR`, a relative payload path, a
  path neither side can resolve to a tree. The guard lives in the contract region every committed
  wrapper shares, so an adopting repository copies it along with the exit-code mapping.

- **A rule every operand of which was skipped is now listed under the law fence instead of vanishing
  from the page.** The drawing omits the self-arrow an `only`-verb produces when it names its own
  subject, because that is the permission for a place to reference itself and no reader needs an
  arrow to believe it. A rule whose *sole* operand was that subject therefore drew nothing and was
  never listed either, so a real law left no trace anywhere on the page. The compact list now counts
  the edges a rule actually contributed and names any rule that contributed none, which restores the
  guarantee the whole artifact rests on: every rule is drawn or listed, never dropped. Four of
  `Meridian.Operations`' nine rules are that shape, including the one saying tracking is the leaf of
  the module graph.

- **A failed NuGet restore no longer turns a red rule green.** Measured on a purpose-built bed with one
  tree, one spec and the feed as the only variable, with no rebuild between the runs: restored, `check`
  exited 1 on a rule forbidding a package namespace; with the feed unreachable it exited **0**, and the
  rule reported itself inert — its target selection matched no types. `graph --json` settled which half was
  wrong: the external edge the violation rests on was in the restored run's survey and absent from the
  broken one. The violation was not missed; the edge it rests on was never extracted. Nothing detected it,
  because a project whose restore failed *loads completely* — full document set, both output paths — so the
  structural load predicate has nothing to blame and the run reads as a clean solution. It is now read off
  the one place that records it without a message match: `project.assets.json`, which a failed restore does
  write, with an empty `libraries` section and the failure in its `logs` array as a `code`/`level` pair.
  Both fields are invariant, so a project whose assets file carries a `level: Error` entry is a project
  whose restore failed in any UI language — a file read, not a diagnostic parse, and so compatible with the
  gate having stopped reading diagnostics altogether. Those projects feed the same fail-closed gate as a
  failed load and take the same `--allow-workspace-diagnostics` opt-out, but keep their own slot and their
  own wording throughout: they *did* load, so naming them among "projects that failed to load" would be
  false, and the remedy is `dotnet restore` plus the NuGet error behind it rather than `dotnet build`.
  `check`, `status`, `graph`, `render`, `baseline`, `context` and the xUnit adapter each state it in their
  own terms; a run with both causes writes the load block first, then the restore block, since a project
  that never loaded is more fundamentally broken than one that loaded without its packages. The three JSON
  documents gain a `restoreFailedProjects` slot beside `failedProjects` — solution-relative, forward-slashed
  and omitted when empty, so every schema version and every clean document is unchanged — and SARIF gains a
  structured notification for it *and* for `failedProjects`, which had none either, so the one surface that
  can name a cause no longer names half of one. The NuGet audit family (NU19xx) is carved out by `code`,
  the exact match that was impossible on Roslyn's diagnostic stream: an advisory promoted to an error by
  `TreatWarningsAsErrors` is an external, time-varying input, and refusing a complete model on one is the
  defect issue #19 was about. A solution that was **never restored at all** is caught by the same gate, for
  the same reason and in the same slot: it has no assets file to read, and absence asserts nothing on its own
  — a non-SDK-style .NET Framework project, which this product exists to support, never writes one — but it
  asserts "the restore never ran" for an SDK-style project, which writes one every time. Whether a project is
  SDK-style is read off its own `.csproj` (an `Sdk` attribute, an `Sdk` element, or an `Import` carrying one),
  and that discriminator was measured before it was allowed to gate: every project in this repository and its
  fixture trees, classified and paired against whether an assets file is really on disk, agreeing everywhere
  except the beds deliberately left unrestored. The refusals say "NuGet packages did not resolve", which is
  true whether the restore ran and failed or never ran, and point at the warnings above conditionally,
  because a restore that never ran wrote no NuGet logs for the SDK to replay. What remains out of scope is
  narrower and stated where it is claimed: a non-SDK-style project using `PackageReference` that was never
  restored, whose absent assets file cannot be told from a `packages.config` project's.

- **A spec whose `typeof()` anchor lives in a .NET shared framework is told so, instead of being sent to a
  build setting that cannot work.** The failure names an assembly, and the message asked the reader to
  classify it: "if it is a package" → add `CopyLocalLockFileAssemblies`; "if it is a .NET Framework
  assembly" → use a namespace pattern. A .NET *shared* framework — an ASP.NET Core `ControllerBase`, a
  `Microsoft.WindowsDesktop.App` type — is a third world that sentence had no word for, and it reads as a
  package, which is the one answer that cannot help: measured both ways, the load fails identically with
  `CopyLocalLockFileAssemblies` on and off, because a `<FrameworkReference>` contributes no entry to the
  spec's dependency manifest and no file to its output, so the property has nothing to copy. The remedy is
  now chosen by structure already on disk rather than by the reader: the spec's own `.deps.json` is the
  manifest the resolver just failed to resolve through, so an assembly it names is a package asset and
  staging it is the fix, while one it does not name cannot be staged by anything and wants a string anchor
  — `.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")`, which renders byte-identically to the
  `typeof()` form and needs no assembly load — or a namespace pattern. A manifest that cannot be read falls
  back to the previous both-worlds wording rather than guessing. The discovery-time variant of the same
  failure, which aggregates several loader errors and so cannot honestly branch per assembly, now names both
  worlds instead of asserting the packaging one.
- **A directory holding both a solution and a filter over it resolves to the solution.** Discovery treated `.sln`, `.slnx` and `.slnf` as equal candidates, so the common layout of a filter beside its solution refused as ambiguous. A `.slnf` now drops out of the candidate set wherever a `.sln` or `.slnx` stands beside it; several full solutions still refuse as ambiguous, and the message names only the surviving candidates.
- **A NuGet restore warning no longer refuses a solution whose rules all pass — in any language.** The
  fail-closed gate decided "did the model fail to build" by matching the text of MSBuild's project-load
  messages, and that was never something text could answer: Roslyn reports every project-load log item as a
  failure with the code stripped, so a fatal evaluation error and an ordinary restore warning arrive
  indistinguishable. Two measurements made it concrete. `NU1510` — NuGet's advice that a package the shared
  framework now carries could be dropped from a csproj, which .NET 10 emits for a large and growing set —
  exited 2 in the same run that evaluated the solution's one rule and passed it. And the same audit-fetch
  failure that exits 0 in English exited 2 under a German toolchain, carrying no advisory URL, no code, and
  none of the phrases the matcher knew. The gate now reads which projects failed to load off the loaded
  solution's own structure: a `.csproj` the solution declares that produced no project, or a project whose
  evaluation produced neither an output path nor an intermediate assembly path. No message is an input to it,
  so no wording and no locale can move it, and the two shapes above render as the warnings they are and exit
  0. Every refusal now names the projects rather than pointing at a wall of diagnostics, and `check`,
  `status` and `graph` carry them as `failedProjects` beside `modelIncomplete` — the evidence an MCP client
  had no way to recover from the diagnostics array. What this deliberately does not change: a solution that
  was never restored still loads completely and raises no diagnostic, exactly as before — measured, not
  assumed, and caught since by the restore predicate rather than by anything this bullet changed;
  a solution filter's dropped members are not treated as failures (they now ride
  `uncheckedProjects` rather than going unreported); and a real load failure riding
  alongside an advisory still fails closed.
- **A workspace that failed to load no longer reports itself as a missing spec project.** A broken
  `--locked-mode` restore leaves the spec project's reference to the contract library unresolved, so
  convention discovery matched nothing and reported that in its own terms — "no solution project references
  Zphil.LoadBearing.dll. Pass `--spec` to name one" — sending the reader off to write an argument that cannot
  help, because an unresolved reference stays unresolved whichever project you name. Zero candidates now
  splits on how the workspace loaded. A clean load keeps that sentence byte for byte and adds how many C#
  projects were considered, since a count far short of the solution's is the real finding. A load that failed
  says so instead and points at repairing the restore and the build, quoting up to three projects that failed
  to load, or — when nothing failed but the load still reported problems, which is exactly the locked-mode
  shape — up to three of those diagnostics. The diagnostic arm deliberately skips NuGet advisories: three
  freshly published ones would otherwise fill the quote and push the one actionable line past its end.
- **`check` finds the spec assembly under any output layout the SDK produces, including two that refused a
  fully built solution.** A parent `Directory.Build.props` carrying
  `<OutputPath>bin\$(Configuration)\</OutputPath>` is imported before the SDK defaults `Configuration`, so the
  path MSBuild evaluates is a flat `bin\` that no build ever writes to — and the fallback meant to cover a
  missing output counted a fixed number of directories up from that path, climbed past the project entirely,
  and reported "no built output" in every configuration. `<UseArtifactsOutput>true</UseArtifactsOutput>` built
  `-c Release` only failed the same arithmetic from the other side, re-appending the literal `debug` segment
  as though it were a target framework. The fallback is now a bounded search: it anchors inside the output
  root the evaluated path already sits in — `bin` or `artifacts`, starting at the deepest directory of that
  chain that exists and widening toward the root only while it finds nothing, because the workspace load's own
  design-time build creates the evaluated directory, empty, before resolution ever looks — looks for the
  assembly by name below it, and prefers the shallowest match, then the most recently written. It also cannot answer with an intermediate (`obj`-side) assembly, whose metadata-only reference
  twins fail deep inside `Define()` rather than at load: the project's own intermediate path is read from the
  workspace and recorded in the cache, so the refusal holds identically on a cold run and a cache hit, and
  holds without the rule ever naming `obj`. What it deliberately costs: a repository that renames its output
  root keeps the primary resolution and loses the cross-configuration fallback it incidentally had, which is
  the price of a search that can never climb to a repository or a drive root.
- **A project that multi-targets is one project again, everywhere.** Roslyn appends a `(tfm)`
  discriminator when one csproj yields several projects, and that name leaked into everything
  downstream: convention discovery refused a multi-target spec project as "Multiple spec projects
  found" while listing two names that are one csproj — neither typeable as `--spec` — and
  `arch.Project("Foo")` selected the empty set because every node carried `Foo(net10.0)`, turning
  a correct spec into a false red. The name is now normalized where the solution is produced,
  exactly when several projects share one project file, so discovery counts one candidate per
  csproj, selections mean what they say, and a cache hit replays the same built output a cold run
  chose from every framework's evaluated path. What deliberately remains: types declared by
  several frameworks take their facts from one framework, and the model note naming the winning
  framework stays, so a rule that passes because of the other framework's shape is never silent.
- **A flag spelled as a string is now coerced, instead of coming back as a server bug.**
  `{"overview": "true"}` is the likeliest mis-spelling of an MCP flag and was the one shape the
  forgiving-input layer did not cover: Web JSON defaults read numbers from strings but never
  booleans, so the call returned a byte-position deserializer message — and, since that exception
  is not one the error mapper recognises as user input, the server logged its own user error as an
  unexpected one. `arch_graph`'s three flags now take `"true"`, `"False"` and `" TRUE "`, plus the
  one-element array (`[true]`) their string siblings already forgave, and refuse everything else —
  `"1"`, `"yes"`, a number, `null` — naming what they were sent rather than a byte offset. Those
  last spellings are refused rather than guessed at: a caller who meant true has a word for it, and
  the alternative is a coercer inventing a value nobody can watch it invent. The advertised schema is
  unchanged, and a test now derives the covered set from the tool surface itself, so the next
  parameter of a type nothing coerces fails the build rather than drifting silently.
- **The derive recipe now warns that a spec namespace ending in `Arch` shadows the type the spec is
  written against.** The `arch/` folder convention makes `MyApp.Arch` the natural name to reach for, and
  inside a namespace whose last segment is `Arch` the simple name `Arch` binds to that namespace rather
  than to the `Arch` type `Define` takes — so the spec does not compile, and neither error that follows
  can say why. Both are the compiler's: a `CS0118` that `Arch` is a namespace used like a type, beside a
  `CS0535` that the class does not implement `Define(Arch)`. The recipe now says to name the project so
  its last segment is not `Arch` where the scaffold is introduced, and carries both errors verbatim among
  the ones a reader may see. Qualifying `Zphil.LoadBearing.Arch` in the signature is noted there and not
  recommended: it compiles, and leaves the shadow armed for every file the spec grows.
- **The docs no longer imply that installing the .NET 10 SDK is enough for `dnx` to resolve.**
  `dnx` is a .NET 10 SDK command, and `dotnet` picks an SDK per directory from the nearest
  `global.json`, so inside a repository pinning an SDK below 10 the command does not exist
  however much .NET 10 is installed beside it — and a registry client launching there reports
  a server that failed to start, with the reason on stderr alone. README and SECURITY.md now
  state the precondition where the recipe appears and route pinned repositories to the
  installed `loadbearing` tool, which needs the SDK installed on the machine rather than
  selected in the working directory. `.mcp/server.json` stays as it is on purpose: `dnx` is
  the launch shape the MCP registry defines for NuGet packages, and no manifest field can
  express a global-tool install or override a consumer repository's SDK pin, so the manifest
  serves the repositories that can use it and the prose carries the boundary.
- **The MCP registry lists this server now, instead of answering empty while nuget.org advertised it.**
  The manifest, the readme marker and the namespace have all been in place since before 0.4.0, but
  listing a release at `registry.modelcontextprotocol.io` was a manual step that had never been run — so
  a client searching the registry for the server found nothing, while the same server sat on nuget.org
  with an MCP configuration ready to generate from it. A second release job closes that: once the
  packages are live, it lists the released version. Ownership is proven twice with no credential stored
  anywhere — the job's GitHub OIDC token buys publish rights on the namespace, the same id-token
  mechanism the NuGet login already uses, and the registry independently fetches the package readme from
  nuget.org and requires the `mcp-name` marker it carries. That second check is why the job waits on the
  marker rather than on the package: the readme is served only once indexing has run, measurably later
  than the flat container has the bytes — about eighteen minutes at 0.3.1 — and publishing before then
  fails validation. The publisher binary is pinned by version and digest, the standing every other
  third-party step in the release has, and the job needs the release itself, so a NuGet push that failed
  can never be advertised as a listing.

## [0.4.0] - 2026-08-09

### Added

- **Both querying surfaces can narrow, so a big solution answers whole.** `graph` takes two
  grain flags — `--overview` (every project, edge and external row kept, namespace inventories
  elided) and `--skeleton` (coarser still: the structural spine of projects and their edges,
  with the external rows elided too and reported as `externalEdgeCount`) — plus
  `--projects <globs>`, which narrows the subject rather than the detail (a scoped survey whose
  edges keep both directions, so an inbound edge still names its outside source); `check` takes
  `--rules <globs>`, which filters what runs rather than what is shown, and the summary counts
  the subset. The MCP twins are `overview`/`skeleton`/`projects` on `arch_graph` and `rules` on
  `arch_check`. Globs match whole names (`*` spans `/`, so `legacy/billing` does not reach
  `legacy/billing/containment`), and a glob that matches nothing refuses loudly, listing what
  is available, rather than answering about an empty scope as if it were the solution. The new
  JSON fields (`grain`, `projectsScope`, `rulesFilter`, `externalEdgeCount`) are omitted when
  unused, so every schemaVersion is unchanged and an unnarrowed document is byte-identical.

### Changed

- **`render` now fails closed on an incomplete model, closing the one-answer sweep.** It was
  the last verb that would consume a partial model and exit 0, and the two things that go wrong
  there are silent: a scope or layer card whose project failed to load resolves no directory
  and is dropped from the committed files rather than written wrong, and `--diagram` draws the
  very survey `graph` refuses to print. Render now refuses with exit 2 after the load warnings
  and before the first byte hits disk, with the same `--allow-workspace-diagnostics` opt-out as
  the other verbs.
- **`explain` and `arch_context` stop discarding load failures.** Neither gates. `explain` took
  no error writer at all, so a load failure vanished; it now echoes the warnings to stderr on
  the workspace path and still answers, because its answer comes from the spec and cannot be
  made wrong by a project that did not load (a built-DLL `--spec` never opens the workspace and
  stays silent). `arch_context` resolves card placement from the extracted codebase, so a
  partial load could answer "no architecture scope covers this path" about a path inside a
  project that did not load: a false all-clear precisely where the spec had something to say.
  Its answer now opens with a caveat block naming the load failures, inline, on the only
  channel the tool has.
- **`arch_graph` coarsens its own grain instead of returning cut JSON.** A survey bigger than
  the client's response budget used to be truncated mid-array — unparseable, with a footer
  suggesting the results were merely incomplete — which in practice sent agents away from the
  MCP surface to guess at CLI schemas. Now the tool re-renders the same extraction one rung
  coarser when the document would overflow, and keeps going while it still would: full →
  overview → skeleton, each byte-identical to that grain's own flag, one extraction however far
  it walks. One rung was not enough, and a real solution is what showed it: on a 34-project
  codebase the full survey is ~147,000 characters and the overview it degrades to is still
  ~82,000, over any default budget, so stopping there handed the truncator exactly the document
  this behaviour exists to avoid. The default budget when the client sets no `MAX_MCP_OUTPUT_TOKENS`
  is 62,500 characters rather than 25,000. Truncation stays the backstop for a survey too big
  even at skeleton grain, and its footer now names the knob that would actually help there —
  `projects`, the subject, since the grain ladder is spent — with `rules` for `arch_check` and
  nothing for the three tools that have no knob to name. A complete document at coarser grain
  beats a cut document at full grain.
- **The MCP server's instructions now fit the client's render window.** Claude Code puts a
  server's `initialize` instructions into the session system prompt whole only up to 2,048
  characters and silently cuts the rest, which had left everything after the tool list — the
  `derive_spec` pointer and every cross-cutting rule — invisible in every session. The
  instructions are rewritten routing-first at 1,971 characters: what each tool is for and when
  to reach for it, the JSON shape, and the partial-model contract, with parameter reference
  left to the tool descriptions, which are fetched on demand. A pinned test fails the build if
  the text ever crosses the window again, and the `arch_check`/`arch_status` descriptions now
  carry the partial-model stamp (`workspaceDiagnostics` + `modelIncomplete`) so that contract
  survives on the per-call channel too.
- `baseline --init` and `--accept-reductions` now name the rules that are failing with no baseline
  to capture — the Enforce reds a baseline cannot grandfather — instead of reporting only the
  ratchet work, or "nothing to do" on a spec with no ratcheted rules at all. The command already
  knew both facts at that moment; now it says the second one: each failing rule with its violation
  count, and that the red survives until fixed at the source — in the code, in the rule, or by
  re-posturing the debt as Migrate or Quarantine. The `derive_spec` recipe stops promising an
  unconditionally green re-check to match: the handoff now has two honest endings — green when
  every violating rule was ratcheted, or red with every remaining rule named as chosen work. Exit
  codes are unchanged; the command still reports rather than gates. Without this, a newcomer
  running the handoff steps on a long-lived codebase could land on dozens of violations with
  nothing saying whether red was expected or their own mistake.
- The warm MCP server reuses its merged codebase and loaded spec between calls when nothing has
  changed on disk, keyed on the fragment-set version and the spec DLL's stamp, so a per-edit
  `arch_check` hook stops re-merging every fragment and re-executing `Define()` on an unchanged
  tree.

### Fixed

- **The xUnit adapter no longer turns a project that fails to load into a green test run.** The
  adapter never passed the loader a diagnostic log, which is the only way load failures leave
  it, so they were dropped: the run measured a codebase missing whole projects, no surface said
  so, and the green landed in a CI report. The adapter now gives `check`'s answer in test dress:
  a new `Workspace_LoadedCompletely` test fails carrying the diagnostics inline, every rule case
  skips rather than report a verdict that was never reached, and a
  `protected virtual bool AllowWorkspaceDiagnostics` override opts into the partial model,
  flipping the named test to a skip so its name never asserts something false.
- **A registry launch that cannot find a solution now starts and says why, instead of dying during
  `initialize`.** The manifest's `dnx` entry passes the bare `mcp` verb with no solution argument, so
  the server walks up from its working directory. That resolves nothing where the solution sits under
  `src/`, and refuses as ambiguous where several sit side by side at the root, which between them
  covers most real repositories. Both refusals named a fix and neither reached a client: the process
  was gone before there was a tool call whose result could carry the message, and the walk-up's reason
  went to a stderr channel the MCP surface discards by construction. An argument that does not resolve
  is still fatal, because `mcp --bogus` must not start a server that could only repeat that error. A
  launch with no argument is the documented-optional path, so its walk-up failing now records the
  reason and starts the server unbound, announcing it on the two channels a client reads: a banner
  above the `initialize` instructions, and every tool call's error result. The refusals themselves now
  name the solution argument ahead of `LOADBEARING_SOLUTION_PATH`, since in an MCP client config the
  server's `args` array is the fix a reader can apply where they are reading, and the "nothing
  anywhere" arm lists the solutions it found one level down. Naming `src\Storefront.sln` turns a dead
  end into one copy-paste. The README and the registry manifest say the same thing now: the argument
  is optional, and most repositories should pass it.
- **The rule pack is no longer presented as something a reader can install.**
  `Zphil.LoadBearing.Packs.DotNet` is unpackable by design: it is the demonstrator that a pack is an
  ordinary class library, and keeping it out of the release keeps `dotnet pack` at the four nupkgs the
  workflows count. The public material had drifted from that, recommending a thing nobody outside a
  clone could obtain. The README's rule-pack paragraph and both example READMEs now say the pack ships
  in this repository rather than as a package to install; the adoption walkthrough says its
  `src/`-relative `ProjectReference` resolves in a source checkout and nowhere else, and that on your
  own solution the pack is one you write; and the self-spec's `packs/depends-on-core-only` reason,
  which renders into the committed `AGENTS.md` files, no longer claims the pack "ships as its own
  package".
- **The adoption walkthrough's reset step left the sandbox holding five projects, not the four it
  claimed.** `dotnet sln add` follows the spec's project references and adds both the LoadBearing
  contract library and the rule pack, which the walkthrough states a few paragraphs earlier and its
  own captured output shows. The "Try it yourself" removal dropped only the contract library, so the
  `dotnet sln list` count a reader was told to expect could not be the count they got. It removes both
  now.
- **The published spec scaffold is valid on a repository using central package management.** The
  recipe's csproj carried its version inline on the PackageReference, which under a
  `Directory.Packages.props` is NU1008 — an error whose text is NuGet's and points nowhere near the
  tool that produced the csproj. The scaffold now opts itself out with
  `ManagePackageVersionsCentrally=false`, a per-project property evaluated after the implicit
  central import, so the spec keeps the version pin that belongs to it and the adopter's central
  file is never touched. The recipe carries NU1008 verbatim among the errors a reader may see, and
  CI scaffolds and builds the published shape on both kinds of repository.
- **The published MCP manifest can no longer advertise a version that does not exist.**
  `.mcp/server.json` is the file nuget.org reads to generate a client's MCP configuration, and its
  version is bumped when a release is *prepared* rather than when one happens. So a branch pushed
  without the tag behind it left every registry client following that manifest asking `dnx` for a
  package version that answers 404 — which is what happened at 0.3.0, and it stood until the next
  release replaced it. CI now holds the property a reader actually depends on: a version this
  repository advertises is either already on nuget.org or carries the tag that publishes it.
- The MSBuild-selection note rides the documents now, not stderr alone. It was appended at write time,
  which made it the one line the MCP tools lost when they pass a null error writer, and "which MSBuild
  opened it" is the next question after any load failure. Composing it into the list both renderers
  read puts it in `workspaceDiagnostics` beside the failures it explains, and in SARIF. Gating is
  unchanged: every caller still gates on the source's own diagnostics, so an informational line cannot
  mark a run incomplete.

## [0.3.1] - 2026-08-05

Pre-alpha. The first release to carry the 0.3.0 changes: 0.3.0 was never published to NuGet, so
0.3.1 is the version that follows 0.2.0 on the registry and the one an MCP client resolves from
`.mcp/server.json`. On top of that, the three fixes below — two of which made the tool refuse a
solution it should have checked, and one of which crashed it outright.

### Changed

- **A partially-loaded workspace now gets one answer instead of four.** `check` has failed closed on
  a project that would not load since the gate existed; the other verbs each answered differently.
  `baseline` rendered the warnings, wrote a baseline from the partial model, and exited 0 — the worst
  of the four, because a baseline is the team's signature on its debt, `--init` captures "zero debt"
  for rules whose subjects live in projects that did not load, and `--accept-reductions` cannot tell
  a violation that stopped from a project that stopped loading. `status` reported a burndown counted
  low and exited 0. `graph` surveyed a map that was silently missing whole projects. All three now
  fail closed on `check`'s terms — exit 2, and the same `--allow-workspace-diagnostics` opt-out — and
  `baseline` refuses before any mode writes a byte. NuGet-audit advisories still never gate, on every
  verb. `render` remains deliberately ungated.
- `graph` refuses before extraction rather than after, and its refusal names the projects that failed
  inline, says which MSBuild opened them, and gives the fix in both dialects. It is the verb that
  needs no spec — a stranger's first command on an unfamiliar codebase — so it is the one whose
  refusal has to explain itself on whichever surface asked.
- The JSON documents now carry the verdict the MCP surface used to discard. `status --json` gains
  `workspaceDiagnostics` (which `check --json` already had), and both plus `graph --json` gain
  `modelIncomplete`, set when a project failed to load whether or not the run opted into the partial
  model. `arch_check` and `arch_status` computed the fail-closed gate and threw it away; they now
  return it as data, because a surface with no exit code needs the verdict in the document. All three
  fields are omitted when the workspace loaded cleanly, so every schemaVersion is unchanged and a
  clean document is byte-identical.
- `arch_graph` takes an optional `allowWorkspaceDiagnostics` parameter, the MCP twin of the CLI flag.

### Fixed

- **`graph` no longer crashes on a solution that has not been built.** Extraction mints a record for
  every type a compilation mentions, including ones the compiler could not resolve — and a
  partially-loaded workspace produces plenty, reaching extraction through base types, interfaces and
  attribute classes. Two of the three facts minted from such a symbol already tolerated it; the third,
  accessibility, threw. So the first command a stranger runs on an unbuilt solution answered
  with an internal invariant violation naming a symbol nobody wrote, ~two minutes in. Accessibility is
  now total on the external path (falling back to public — an unresolved external is not a rule
  subject, and a type reached across an assembly boundary is visibly-public surface) while the member
  inventory keeps the hard invariant GRAMMAR §4.6 actually underwrites. The crash was reachable from
  every extracting verb, `check` included, where it beat `check`'s own refusal to the punch.
- MSBuild selection is now reported instead of merely decided. Choosing the MSBuild everything
  downstream depends on had four outcomes and no observer: two of them silently degraded to
  `MSBuildLocator.RegisterDefaults()`, and a third — taking a Visual Studio outside the tested
  VS 2019/2022 set because nothing inside it was installed — was not even describable, since the
  selection read the same either way. `MsBuildBootstrap` now publishes what it chose and why, and
  the five verbs that render workspace diagnostics (`check`, `status`, `graph`, `baseline`,
  `render`) print it on stderr beside them, naming `LOADBEARING_VS_INSTALL_PATH` as the override.
  Quiet runs stay quiet — the line appears only when something already failed to load. For a tool
  whose job is analysing other people's legacy .NET, "which MSBuild did you pick, and why" should
  not be unanswerable.
- `LOADBEARING_VS_INSTALL_PATH`, the escape hatch the code has always offered, is documented for
  the first time: the README's .NET Framework section now says what the tool actually looks for
  rather than "Visual Studio or Build Tools installed".
- CI gained a `windows-2022` leg. `windows-latest` is now the VS-2026-only image, which carries no
  VS 2022 and no Build Tools 17, so the .NET Framework legacy tests had no VS 17 machine anywhere in
  the matrix.
- The non-SDK-style .NET Framework fixture no longer fails to load under CI. `ContinuousIntegrationBuild`
  was set at the repo root under `$(CI)`, and it implies `DeterministicSourcePaths`, which requires
  `SourceRoot` items that a project in the 2003 MSBuild XML namespace has no SourceLink to provide.
  Fixture solutions are staged inside the repo tree and built there mid-test-run, so they inherited it
  and their design-time build failed with "SourceRoot items must include at least one top-level (not
  nested) item" — a workspace-load diagnostic, so `check` fell to the fail-closed exit 2 on the CI legs
  only. The property is now scoped to `src/`, where the shipping projects that need deterministic paths
  and stable SourceLink live, exactly as the lock-file policy already was and for the same documented
  reason. Package determinism and SourceLink are unaffected.

## [0.3.0] - 2026-08-01

Pre-alpha. Five new verbs across the exception and attribute axes, a canonical rule pack a spec
can compose from instead of writing the same rules again, string anchors that need no package
reference, two committed diagrams, a .NET Framework envelope — and one breaking rename: the
containment posture sheds a false friend.

### Added

- This repository's own spec now declares layers, which is what the module map, the per-directory
  layer cards, and the MCP `arch_context` layer answers are all driven by. Eight of them — Core,
  Model, Checking, Rendering, Extraction, Host, Adapter and Pack — with seven rule subjects and one
  union re-anchored onto them, so the rendered sentences read in layer voice ("The Adapter layer
  must not be referenced by the Core, Extraction, Host or Pack layers"). Six per-directory cards are
  now committed where there was one, and every path under `src/` answers `arch_context` with
  something.
- Four new rules over this repository's real code: `layering/model-independent` (the Model
  references neither Checking nor Rendering — the product thesis as law), `packs/depends-on-core-only`,
  `naming/interfaces`, and `model/constraint-nodes`.
- CI's self-check job now renders this repository's own spec and requires a zero diff, and runs
  `status`, `graph` and `explain` against this solution. Before, `check` was the only command any
  gate pointed at this repository.
- Filter-aware catch bans and the throw ban: the `MustNotCatchUnfiltered` and `MustNotThrow`
  verbs. "The Web layer must not catch `Exception` without a `when` filter" is now a one-line,
  ratcheted rule: every catch edge additionally records which of its sites spell no `when`
  filter, and the verb reds a matching edge only when at least one site is unfiltered — an edge
  whose broad catches are all filtered passes, which is what a plain `MustNotCatch` could never
  say. Violations list the unfiltered sites only. A bare `catch` counts as `System.Exception`
  and counts as unfiltered; filter presence is syntactic, so a filter's contents are never
  judged (`when (true)` counts as filtered) and `when` filters still never suppress the catch
  edge. `MustNotThrow` is the ban polarity beside the strict allow-list `MustOnlyThrow`, for the
  case where the forbidden thrown types are a handful and the permitted ones are the rest of the
  world. Both verbs match exact definition-level FQN, report through the existing catch and
  throw kinds in human, JSON, and SARIF output, and key the same edge identities as their
  siblings, so a baseline never records which verb a spec chose. The persisted extraction
  cache's schema moves with the new fact: the first check after upgrading pays one cold
  extraction per solution, then steady state.
- The rethrow-aware catch ban: the `MustNotSwallow` verb. "The Web layer must not swallow
  `Exception`" is the third and narrowest question on the catch axis, and the one a broad-catch
  policy usually means: a catch edge reds only where a clause matches the banned type, spells no
  `when` filter, **and** does not end in a `throw`. A cleanup-and-rethrow and a
  translate-and-throw suppress nothing, so both pass — which is what `MustNotCatchUnfiltered`
  could never say, and what forced a policy to exempt whole types just to spare them. Every catch
  edge now additionally records which of its unfiltered sites swallow, and violations list those
  sites only. The rethrow fact is syntactic, and its boundary is stated rather than discovered:
  it reads the block's **last statement**, never an all-paths flow analysis, so
  `catch { if (…) return; throw; }` counts as throwing and `catch { if (…) throw; Cleanup(); }`
  counts as swallowing. The throw's operand is never judged. Like its siblings the verb matches
  exact definition-level FQN, reports through the existing catch kind in human, JSON and SARIF
  output, and keys the same edge identity, so a baseline entry means the same thing under all
  three catch verbs.
- Two more rules over this repository's real code: `exceptions/no-swallowed-broad-catches` (a
  broad catch either names what it expects in a `when` filter or ends in a `throw`; the seven
  sanctioned handlers — the process, rule and test boundaries, two background loops, the shutdown
  drain and one quarantined probe — are exempt by type name, each with its reason written beside
  it, stated in the spec rather than recorded in a baseline, and every type whose broad catches
  all either filter or rethrow stays inside the rule and passes it) and
  `exceptions/no-bare-bcl-throws` (nothing throws `Exception`, `SystemException` or
  `ApplicationException` — green today, and red the day the first one arrives). The managed
  AGENTS.md block's glossary gains its catch clause for the first time.
- Member attribute facts and the member-side attribute vocabulary: the `.AttributedWith`
  member adjective and the `MustBeAttributedWith` / `MustNotBeAttributedWith` member verbs,
  each in `typeof`, string, and generic-twin forms. The member inventory now records each
  member's declared attributes (reaching escape-hatch predicates via `IMemberInfo.Attributes`),
  so a rule whose true subject is "the methods carrying attribute X" can finally say so
  instead of approximating it by namespace or naming convention. Declared attributes only: an
  attribute on a property's `get`/`set` accessor or a method's `[return:]` attribute hangs off
  a different symbol and is outside the fact, and a partial method reports the union of both
  parts' attributes. The adjective renders as a head prefix — "`[McpServerTool]`-attributed
  methods of types in `Zphil.LoadBearing.*`" — because an inline clause would render an
  attributed type's methods and a type's attributed methods as one byte-identical sentence
  for two different subjects; stacked attribute adjectives concatenate, because the subject
  is their intersection and a sentence that dropped one would lie about it. The persisted
  extraction cache's schema moves with the new fact: the first check after upgrading pays one
  cold extraction per solution, then steady state.
- Attribute anchors by string: every attribute position — the `AttributedWith` adjectives and
  the `Must[Not]BeAttributedWith` verbs, type-side and member-side — now takes the attribute
  definition's fully-qualified name (`Attribute` suffix included) beside the `typeof` form,
  so a spec can govern an attribute without taking a package reference just to write the
  `typeof`. The string matches any construction of that definition, exactly as an
  open-generic `typeof` anchor does; a constructed spelling never matches — the stated
  honesty boundary. A string anchor renders byte-identically to its `typeof` twin, collision
  widening included, so which form a spec chose is invisible to its sentences. `typeof`
  remains the right anchor whenever the attribute is referenceable: the compiler checks a
  `typeof`, and nothing checks a string.
- A rule this repository's verb ledger recorded as wanted and unaffordable, now affordable
  and shipped: `mcp/tool-types-attributed` — "Types in `Zphil.LoadBearing.Cli.Mcp.Tools.*`
  must be attributed with `[McpServerToolType]`". Tool discovery is the attribute walk, so a
  tool class without the attribute compiles, registers nothing, and its tools vanish from the
  server in silence. Both MCP attribute rules name their attribute by string: the spec
  project still takes no SDK package reference, which is what the string anchor bought.
- The committed architecture diagram grows a second fence. `render --diagram` already drew the
  codebase survey — the projects and the references between them, taken from what the code
  does. Beside it now sits the architecture law, taken from the spec: forbidden references as
  `--x` edges, an exposure ban labelled `expose`, the only-verbs as `-->|"only"|` with the
  self-target allow edge omitted, a Migrate rule's ratcheted debt as a dotted
  `-.-x|"grandfathered"|` edge, and each quarantined scope as a box holding its sanctioned
  surface. Layers, namespace globs, projects and named types are the nodes, nested where one
  glob provably contains another, under a legend carrying only the constructs that drawing
  uses. One composer writes both fences into the one managed block, and it is the path
  `render` and the drift gate share, so the command and the test cannot disagree.
  `--diagram-only` and `--diagram-exclude` scope the survey alone: what the spec forbids is not
  a property of the projects someone chose to draw, so the law fence can name a place the
  survey dropped. A diagram can only draw a rule whose subject and targets are places, and most
  of a real spec is about names, shapes, attributes and members instead. Those rules are listed
  by ID under the fence with a posture tag and an `explain` pointer, so the picture is never
  mistaken for the whole law.
- Hierarchy anchors by string, completing the escape hatch the attribute positions already
  had: `Implementing`, `DerivedFrom` and the four `Must[Not]Implement` / `Must[Not]DeriveFrom`
  verbs now take an interface or base type's fully-qualified name beside the `typeof` form, so
  a spec can govern a contract it cannot compile against — "types implementing
  `MyApp.Web.IHandler<T>` must be named `*Handler`" with no reference to the assembly that
  declares the interface. The string is the name a report prints, declared type-parameter
  names included, and it matches any construction of that definition exactly as an
  open-generic `typeof` anchor does; a constructed spelling never matches, which is the one
  place the string form is deliberately weaker than its `typeof` twin. Rendering is
  byte-identical to that twin, collision widening included, so which form a spec chose stays
  invisible to its sentences. Only blankness is validated: the category check that refuses
  `MustNotImplement(typeof(SomeClass))` cannot read a category off a name, so a wrong string
  is loud on a positive rule and silent on a negative — stated, not discovered. Every
  single-type anchor position now offers the same triple: the compile-checked `typeof`, the
  no-reference string, and the generic twin.

### Changed

- **Breaking:** `Scope(id).Freeze(selection)` is now `Scope(id).Quarantine(selection)`, and every
  rendered surface (agent context, status/check JSON, SARIF, explain output, validation messages)
  says "quarantined", never "frozen". In the surrounding ecosystem "freeze" means "snapshot current
  violations as an accepted baseline" (ArchUnit's `FreezingArchRule`, `pip freeze`) — which is
  LoadBearing's `Migrate(...).Baseline(...)`, not its containment posture, so the old name pointed
  readers at the wrong sibling. No alias or compat shim. Machine-readable posture values change from
  `"freeze"` to `"quarantine"`; desugared rule ids (`{id}/containment`, `{id}/tripwire`), the clause
  names (`BoundaryOnlyVia`, `Dragons`, `DragonsDoc`, `Baseline`), and `Migrate` are unchanged.
- The self-spec's own documentation no longer claims to exercise "the full posture and verb range",
  which was true of the postures and false of the verbs. It now says what it exercises, and carries
  a ledger naming every unused verb family with its reason — including the rules that were tried
  against the real code and declined rather than contrived, and the stance on `baseline --add`. A
  new test holds the ledger complete against the public `Must*` surface, so a verb that ships with
  neither a self-use nor a ledger line reddens CI.
- Spec-load diagnostics now cover .NET Framework specs. A `typeof()` anchor whose type closure
  reaches a Framework-only assembly (a base type or implemented interface in `System.Web`, say)
  used to surface as a raw `TypeLoadException`; it is now a spec-load error naming the type that
  could not be loaded and pointing at the namespace-pattern anchor, which needs no assembly load.
  The missing-dependency message it sits beside was reworded for the same reason: its remedy used
  to lead with `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`, which is the fix
  for a NuGet-packaged dependency and a dead end for a .NET Framework reference assembly, because
  such an assembly resolves from the targeting pack or the GAC and is never staged into `bin`.
  Both remedies are now named, each with the case it applies to.
- The hook wrappers read the edited file's path from the PostToolUse payload and skip the check
  when the file is one the extractor cannot see (anything but source, project, and solution
  files), so a documentation edit no longer pays a full solution check. A hand-run wrapper, with
  no payload on stdin, still checks; so does any edit whose payload cannot be parsed.
- `mcp/tools-accept-cancellation` in this repository's own spec now reads
  "`[McpServerTool]`-attributed methods of types in `Zphil.LoadBearing.*` must accept a
  parameter of type `CancellationToken`" — the attribute is the whole subject. The
  tools-namespace scope and the `Task`-returning narrowing it carried were both
  approximations of that attribute, and keeping either would have silently exempted a future
  synchronous tool method, or one declared outside the tools namespace, from the rule meant
  to catch it.
- The CLI's four JSON documents (`check`, `status`, `graph`, and the SARIF report) now serialize
  through a source-generated context rather than by reflection. Output is byte-identical — the
  goldens are unchanged — so this matters only if you trim or AOT-compile the tool, where
  reflection-based serialization is what breaks.

### Fixed

- The per-directory context cards `render` writes were gated by nothing: a scoped card could drift,
  and a card orphaned by a spec change could sit committed forever with no test that would look at
  it. The directory-grouping and merge step now lives in one composer shared by the command and a
  new drift gate, which asserts every committed card is byte-current and that no committed card
  exists that no placement produced.

- The PowerShell hook wrapper now hands the agent the violation report itself. PowerShell
  captures multi-line command output as an array, and `[Console]::Error.WriteLine` printed the
  array's type name — an agent blocked by a red rule read `System.Object[]` where the rule ID,
  reason, fix, and `file:line` should have been. The lines are joined before writing; the POSIX
  wrapper was already correct.
- The hook recipe no longer assumes it fires from the repository root. Claude Code runs command
  hooks in the session's current directory, so the settings snippet now anchors the wrapper path
  to `${CLAUDE_PROJECT_DIR}` and the wrappers change to that directory before checking. The
  snippet also sets an explicit 120-second hook timeout: the 60-second default sits too close to
  a cold check.
- A warm MCP server no longer holds the spec project's build output open, so `dotnet build` of a
  spec project succeeds while a client is connected. Spec assemblies and their dependencies load
  from their bytes rather than their paths: the load context is collectible, but the model it
  returns roots the spec's `Type` references, so it was never actually collected and a long-lived
  server pinned the spec DLL and everything staged beside it for its whole lifetime. Every build
  then failed with `MSB3021`/`MSB3027` until the server was stopped — and stopping a stdio server
  is unrecoverable for its client, which silently disarmed every hook armed against it.

## [0.2.0] - 2026-07-23

Pre-alpha. The verb vocabulary grows past dependency bans — member access, construction, DI
lifetimes, exceptions, parameters, hierarchy/attribute negatives, and signature exposure — and
checks now run build-free from caches, with SARIF as a third render target.

### Added

- Warm MCP server: the workspace loads on the first tool call, then is held warm and
  reconciled against disk per call, so a post-edit `arch_check` answers in milliseconds
  (opt out with `LOADBEARING_DISABLE_WARM_WORKSPACE=true`). The one-shot CLI gains a
  persisted per-solution extraction cache: a clean tree checks without a design-time
  build; `--no-cache` bypasses it.
- `check`/`status`/`graph` `--binlog <path>`: replay a real build's binlog instead of running a design-time build (via Basic.CompilerLog). The capture persists per solution, and later runs replay it automatically while it stays structurally valid; `--no-cache` now bypasses both the fragment cache and the build capture. Known v1 limit: replayed models can omit source-generator output (measured: ASP.NET Razor generated types), so on generator-heavy solutions the design-time path remains the fidelity reference.
- Member-access bans: `arch.Member(...)` targets and the `MustNotUse` verb. "The Web layer
  must not use `DateTime.Now`" is now a one-line rule, checked at source-level member use
  with `file:line` and ratcheted like any other rule (`baseline --add --target` accepts
  member names and symbol IDs).
- Member subjects: `.Methods`/`.Properties`/`.Fields`/`.Events`/`.Members` projections
  with member adjectives (`.Returning(...)`, name affixes, `.Where(...)`) and member
  verbs (`MustHaveSuffix`, `MustBePrivate`, `MustBeVirtual`, …): "methods returning
  `Task` must be named `*Async`" in closed vocabulary.
- Compile-checked anchors and generic sugar: `arch.Member<T>(x => x.M)` /
  `arch.Member(() => X.M)` expression member anchors, `arch.Type<X>()`, generic
  adjective/constraint twins (`.Implementing<T>()`, `MustImplement<T>()`, …), and the
  static forms directly on the verb, `MustNotUse(() => DateTime.Now, () => DateTime.UtcNow)`.
  All pure authoring sugar reifying to the identical model.
- Spec-source locations in validation errors: every spec-build error that names a rule, scope, or
  member now renders with the `file:line` of the offending statement — captured via
  `[CallerFilePath]`/`[CallerLineNumber]` on the anchor factories (`arch.Rule`, `arch.Scope`, the five
  `arch.Member` forms) — so all-errors-at-once lands each one at a jump target. Rendered file-name-only
  (never the machine-specific full path), keeping goldens deterministic; an unlocated error degrades to
  the un-prefixed message. The optional caller-info parameters are invisible at call sites but are
  binary-breaking for a spec DLL compiled against the previous Core (acceptable pre-publish at lockstep
  0.1.0).
- Constructor bans: the `MustNotConstruct` verb. "Types must not construct types implementing
  `IHandler<T>`" forbids direct `new` of types meant to arrive via DI — a service you may reference
  but may not construct — recorded at source-level object-creation sites (`new`, including
  target-typed `new()`) with `file:line`, reported human (`{source} constructs {target}`) and JSON
  (kind `"construction"`), and ratcheted on the (source, constructed) type pair like any other rule.
  Carries the same `(first, params more)` selection and type-sugar overloads as the reference verbs.
  The canonical §12 sample adopts it as `di/handlers-via-registry` — every type except the
  sanctioned `HandlerRegistry` must not construct an `IHandler<T>` implementor — the first rule added
  to the founding sample.
- DI registration facts and the captive-dependency ban: the `arch.Registered(Lifetime.Singleton)` /
  `arch.Registered()` noun selects types named in source-visible container registrations
  (`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/`AddDbContext`/
  `AddHttpClient<TClient>` — service and implementation types alike), and the `MustNotInject`
  verb bans constructor-parameter dependencies (primary constructors included): "Singleton-registered
  types must not inject scoped-registered types or transient-registered types" is now a one-line,
  ratcheted rule with `file:line` sites — the general captive-dependency check that no
  whole-solution static tool ships. Reported human (`{source} injects {target}`) and JSON (kind
  `"injection"`); registrations made by assembly scanning, factory internals, or framework
  defaults are the documented honesty boundary. The `Meridian.Interchange` guidance pack now
  enforces it as `di/no-captive-dependencies` with the Microsoft DI-guidelines citation.
- SARIF output: `check --sarif <path>` writes SARIF 2.1.0 beside the human and `--json`
  renderers, over the same report — one result per violation site with stable,
  line-independent alert identities (`partialFingerprints`), red sites as `error`/`new`,
  grandfathered sites as suppressed `note`/`unchanged` results carrying the baseline's
  `--because` attribution, and workspace diagnostics as tool-execution notifications. The
  repo's own CI uploads its self-check to GitHub code scanning; gating stays the CLI exit
  code — SARIF is visibility, not enforcement.
- Exception edges: the `MustNotCatch` and `MustOnlyThrow` verbs over two new edge families.
  "The Web layer must not catch `Exception`" and "the Domain layer must throw only
  `OrderRuleViolation`" are now one-line, ratcheted rules with `file:line` sites: catch
  edges record every `catch` clause (a bare `catch` counts as `System.Exception`; `when`
  filters never suppress; a rethrowing catch still mints — sanctioned log-and-rethrow sites
  are excepted or grandfathered deliberately), and throw edges record every throw statement
  and throw expression at the thrown expression's static type (`throw;` mints nothing;
  `throw ex` mints the variable's static type; `ArgumentNullException.ThrowIfNull` stays a
  member use — the documented honesty boundary). Operand matching is exact: banning
  `Exception` never flags a narrower catch — the narrow catch is the good state.
  `MustOnlyThrow` is strict: external thrown types are constrained too (no type must throw
  a BCL exception), so its sentence carries no external-packages caveat. Reported human
  (`{source} catches {target}` / `{source} throws {target}`), JSON (kinds
  `"catch"`/`"throw"`), and SARIF. The `Meridian.Interchange` pack now enforces the
  Framework Design Guidelines scoped catch policy as `exceptions/no-general-catch` — only
  the `BackgroundService` dispatcher may catch base `Exception` — with the
  using-standard-exception-types citation.
- Parameter facts and the CancellationToken-presence rule: the member inventory now records
  each method's declared parameters (name + definition-level type, reaching escape-hatch
  predicates via `IMemberInfo.Parameters`), and the methods-only `MustAcceptParameter` verb
  turns TAP's token guidance into closed vocabulary: "methods returning `Task` or
  `Task<TResult>` must accept a parameter of type `CancellationToken`" — a presence rule no
  analyzer ships (CA1068 orders a token already present; the call-site flow analyzers require
  one in scope). Matching is definition-level and exact (`CancellationToken?` and
  `params CancellationToken[]` are different declared types; a closed-generic anchor is
  refused at spec build), violations are member-shape reds at declaration `file:line`,
  ratcheted by member identity like the naming verbs. The `Meridian.Interchange` pack now
  enforces it as `async/accept-cancellation` with the TAP citation.
- Negative hierarchy/attribute verbs: `MustNotImplement` / `MustNotDeriveFrom` /
  `MustNotBeAttributedWith` (each with a generic twin) ban what a type may implement,
  derive from, or carry — "types in `Meridian.Interchange.*` must not be attributed with
  `[Table]` or `[ComplexType]`" is now a one-line rule. None-of over one or more anchors
  (`(Type first, params Type[] more)`; the positives stay deliberately single-type), with
  the same matching semantics as the positive twins — transitive with type-argument
  substitution for implement/derive, declared attributes only for attributes — red at the
  subject's declaration `file:line`. Spec build now refuses category-invalid hierarchy
  anchors on both polarities (a non-interface on `Must[Not]Implement`, an interface on
  `Must[Not]DeriveFrom`, a non-attribute on `Must[Not]BeAttributedWith`), steering to the
  right verb where the old behavior was a silent always-red or always-pass. The
  `Meridian.Interchange` pack now enforces persistence ignorance as
  `persistence/no-mapping-attributes` with the architectural-principles citation.
- Signature-exposure bans: the `MustNotExpose` verb over new exposure edges, a
  position-aware fact family recorded where a type appears in a public signature position
  (a method return or parameter type, or a property/field/event type) of an
  effectively-public member. Distinct from plain references: a body reference that never
  reaches a public signature is not an exposure, and a member of an `internal` type
  exposes nothing. "The Web layer must not expose `DataTable`" (the return-DTOs guidance
  as closed vocabulary) is now a one-line, ratcheted rule with declaration `file:line`
  sites, reported human (`{source} exposes {target}`), JSON (kind `"expose"`), and SARIF. The
  `Meridian.Interchange` pack now enforces it as `contracts/no-entity-exposure` with the
  DDD/CQRS citation.

### Changed

- The member inventory excludes explicit interface implementations of every kind: property and
  event implementations now join the method implementations the kind screen always dropped. An
  explicit implementation is interface plumbing (`Private` accessibility, a name fixed by the
  interface), so member subjects, the member-shape verbs, and `MustAcceptParameter` never see
  one, and none mints an exposure edge.

### Fixed

- `check` no longer fails closed on NuGet audit advisories: the NU19xx family (advisory
  re-raises and the audit-fetch failure) is filtered out of the workspace-diagnostics gate
  input, because advisory publication timing is an external input that must not flip a
  deterministic verdict. The advisories still render everywhere they did — the stderr
  warnings, the JSON `workspaceDiagnostics` array, and SARIF tool-execution notifications.
- Colliding simple names widen in every multi-operand list: the negative hierarchy/attribute
  anchor lists (`MustNotImplement` / `MustNotDeriveFrom` / `MustNotBeAttributedWith`) now
  qualify colliding anchors with the minimal distinguishing trailing namespace segments,
  exactly as the dependency target lists already did, with the attribute form qualifying
  inside the brackets (`[Billing.Audit]` or `[Sales.Audit]`).

## [0.1.0] - 2026-07-14

Initial public release, pre-alpha. One fluent C# architecture spec, two render targets:
deterministic enforcement and generated AI-agent context.

### Added

- The reified model and fluent builder (`Zphil.LoadBearing`, netstandard2.0): layers, scopes,
  and rules, each carrying a posture: `Enforce` (the law), `Migrate` (ratcheted tech debt with
  a grandfathered baseline), `Freeze` (contained legacy with "here be dragons" prose).
- Roslyn/MSBuildWorkspace extraction with `file:line` violation locations
  (`Zphil.LoadBearing.Roslyn`).
- The `loadbearing` global tool (`Zphil.LoadBearing.Cli`): `check` (with `--json` and the
  diff-aware `--diff-base` freeze tripwire), `graph`, `render` (the managed AGENTS.md block),
  `explain`, `status`, and `baseline` (`--init` / `--accept-reductions` / `--add`).
- MCP stdio server (`loadbearing mcp`): `arch_check`, `arch_status`, `arch_explain`,
  `arch_context`, `arch_graph` with CLI-identical output, plus the `derive_spec` onboarding
  prompt; ships as an MCP server package (`PackageType=McpServer`) with `.mcp/server.json`.
- xUnit adapter (`Zphil.LoadBearing.Xunit`): every rule in the spec as an individually named
  xUnit test, failure text identical to the CLI.

[Unreleased]: https://github.com/andypgray/loadbearing/compare/v0.7.0...HEAD
[0.7.0]: https://github.com/andypgray/loadbearing/compare/v0.6.1...v0.7.0
[0.6.1]: https://github.com/andypgray/loadbearing/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/andypgray/loadbearing/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/andypgray/loadbearing/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/andypgray/loadbearing/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/andypgray/loadbearing/compare/v0.2.0...v0.3.1
[0.3.0]: https://github.com/andypgray/loadbearing/compare/v0.2.0...v0.3.1
[0.2.0]: https://github.com/andypgray/loadbearing/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/andypgray/loadbearing/releases/tag/v0.1.0
