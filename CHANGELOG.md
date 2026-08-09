# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

### Fixed

- **The xUnit adapter no longer turns a project that fails to load into a green test run.** The
  adapter never passed the loader a diagnostic log, which is the only way load failures leave
  it, so they were dropped: every rule whose subject lived in an unloaded project selected
  nothing, an empty subject passes, and the green landed in a CI report. The adapter now gives
  `check`'s answer in test dress: a new `Workspace_LoadedCompletely` test fails carrying the
  diagnostics inline, every rule case skips rather than report a verdict that was never
  reached, and a `protected virtual bool AllowWorkspaceDiagnostics` override opts into the
  partial model, flipping the named test to a skip so its name never asserts something false.
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

[Unreleased]: https://github.com/andypgray/loadbearing/compare/v0.3.1...HEAD
[0.3.1]: https://github.com/andypgray/loadbearing/compare/v0.2.0...v0.3.1
[0.3.0]: https://github.com/andypgray/loadbearing/compare/v0.2.0...v0.3.1
[0.2.0]: https://github.com/andypgray/loadbearing/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/andypgray/loadbearing/releases/tag/v0.1.0
