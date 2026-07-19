# Derive an architecture spec for an existing codebase

Your goal is a **compiling LoadBearing spec that states this codebase's architecture honestly,
as it is today**: the target rules new code must follow (`Enforce`), the known debt being
worked off (`Migrate`), and the untouchable dragons (`Freeze`). A legacy codebase's
architecture is partly descriptive, not prescriptive — a spec that only states the ideal is
useless on day one, and a spec that launders the mess into law is worse. The postures exist so
you never have to choose between the two.

Work the steps in order. Do not skip the curation gate.

## What this recipe is — and is not (load-bearing, not a disclaimer)

- **This server does not infer the architecture.** There is no "derive" command. *You* derive
  a proposal from evidence; the deterministic checker validates every claim; the human decides
  what becomes law. A spec nobody confirmed is worse than no spec — the rendered agent context
  claims to be provably true, so every rule in it must be something the team actually stands
  behind.
- **Violations during this flow are data, not failures.** You will deliberately author rules
  the codebase violates, precisely to measure the violations. A red `arch_check` mid-derive
  means the evidence pass is working.
- Everything here uses the read-only tools (`arch_graph`, `arch_check`, `arch_explain`,
  `arch_status`) or their identical CLI verbs (`loadbearing graph|check|explain|status`,
  add `--json` for the same documents). The CLI verbs take the solution path as their first
  argument — `loadbearing graph MyApp.sln --json` — or walk up from the working directory
  when omitted (`explain` differs: its rule ID comes first, the solution second —
  `loadbearing explain area/rule MyApp.sln`); the MCP tools are already bound to the solution. The two ratchet mutations
  that end the flow — `loadbearing baseline --init` and the commit — belong to the human.

## 0. Discover stated intent first

Before proposing anything, look for architecture the team already wrote down:

- Existing architecture tests (ArchUnitNET, NetArchTest) — these are rules someone already
  ratified; carry them over rather than re-deriving them.
- Analyzers or lint config enforcing boundaries; `Directory.Build.props` conventions.
- ADRs, `docs/architecture*`, wiki exports checked into the repo.
- `AGENTS.md` / `CLAUDE.md` / Cursor rules prose about layers or "do not touch" zones.
- Solution folders and project naming — they encode intended grouping.

**Already-stated intent wins over inference.** Where a document and the code disagree, that
disagreement is not noise — it is a posture decision waiting for step 5 (the document
describes the target; the code is the debt).

## 1. Survey the estate

Call `arch_graph` (CLI: `loadbearing graph MyApp.sln --json`). It needs no spec — it is the
one tool that works before any spec exists — and returns the codebase as extraction sees it:

- `projects[]` — each project's declared `projectReferences`, type count, and exact namespace
  inventory with type counts. The namespace inventory is your raw material for layer globs.
- `projectEdges[]` — **observed** project→project references (distinct type pairs). Compare
  against the declared references: a declared reference with no observed edge is a dead
  reference (note it for the human; it is cleanup evidence, not a rule). An observed edge you
  did not expect is the interesting kind.
- `externalEdges[]` — external references grouped by namespace root. Scan for the classic
  dangerous externals: `System.Data` (inline SQL), `System.Web` (HttpContext-era coupling),
  direct driver namespaces, and anything the team says it is migrating away from.

From the survey, write down **hypotheses, not conclusions**:

- Candidate layers: coherent namespace subtrees or project groups (Domain-shaped, Web-shaped,
  Infrastructure-shaped). Keep the globs disjoint — overlapping layers double-count evidence.
- Candidate direction rules: which layer should never reference which (the `projectEdges`
  matrix tells you which of those are already true and which are aspirational).
- Candidate conventions: naming patterns the inventory suggests (interfaces, handlers,
  controllers, repositories).
- Candidate dragon zones: "Legacy"/"Old"/"V1" names, a facade-shaped surface guarding a blob,
  areas with no tests that everything fears. If a spec project already exists in the solution
  it will appear in the survey like any other project — ignore it (and in a source-checkout
  setup, the LoadBearing contract library and its types appear too; ignore both — see step 2).

## 2. Scaffold the spec project

Create a small class library for the spec (convention: an `arch/` folder next to the
solution):

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>enable</Nullable>
        <!-- Stages package assemblies into the build output so `check` can load typeof() targets
             that live in NuGet packages; harmless when every target is a project type or pattern. -->
        <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Zphil.LoadBearing" Version="..." />
        <!-- In a source checkout, a (relative) ProjectReference to Zphil.LoadBearing works too;
             replace it with the PackageReference once the package is published. -->
    </ItemGroup>
</Project>
```

**Pick the TFM by one rule: the spec project must be able to reference the product projects
it will `typeof()`.** On a `net48` estate, the spec targets `net48` — the LoadBearing contract
library is `netstandard2.0` precisely so that works. Where referencing a product project is
awkward, you do not need it: namespace-pattern targets (`arch.Namespace("System.Data.*")`)
need no compile-time reference at all, and matching is by full name.

`typeof()` targets must also be **accessible** to the spec assembly: anchoring an `internal`
type fails the spec build with CS0122. For dependency-verb targets, switch to a namespace
pattern; where the type itself is the point — a `BoundaryOnlyVia` facade, an `Implementing`
anchor — have the product project grant `[InternalsVisibleTo("MyApp.ArchSpec")]` (or the
csproj `<InternalsVisibleTo Include="MyApp.ArchSpec" />`) and rebuild.

```csharp
using Zphil.LoadBearing;

namespace MyApp.ArchSpec;

public sealed class ArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
    }
}
```

Add it to the solution (`dotnet sln add arch/MyApp.ArchSpec/MyApp.ArchSpec.csproj`) and build.
Spec discovery is by convention: **the unique solution project that references
`Zphil.LoadBearing.dll`** — via the package or via a project reference. As a solution member,
the spec project is excluded from the checked universe — its own types never trip your rules.

**Source-checkout setups only**: `dotnet sln add` follows project references, so it may also
add the LoadBearing contract library to the solution — and the workspace loads the reference
closure regardless of membership, so the contract library's types appear in the survey **and
in the checked universe**. Ignore them in the survey, and scope broad subjects to your product
namespaces (`arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*")`, not bare
`arch.Types.OfKind(...)`) so convention rules never bite the tooling. A `PackageReference`
setup has none of this — the package is a metadata reference, not a project. (Outside the
LoadBearing repo, expect `dotnet sln add` to record the contract library by a long relative
path — harmless, but it will show in the solution diff; the published package is the cure.)

Test projects need the same subject-scoping care in ANY setup: a test project sharing the
product root namespace (`MyApp.Tests.*` inside `MyApp.*`) is solution-declared, so
`.InNamespace("MyApp.*")` subjects include its types — fakes and test helpers then pollute
naming, member, and hierarchy rules. Anchor product-wide subjects on `arch.Project("MyApp")`
instead; a project noun never crosses project boundaries.

Errors you may see, verbatim, and what they mean:

- `No spec project found: no solution project references Zphil.LoadBearing.dll. Pass --spec
  to name one.` — the spec project is not in the solution yet (`dotnet sln add`), or you need
  an explicit `--spec`.
- `Multiple spec projects found; pass --spec to disambiguate:` — more than one project
  references the contract library; name yours.
- `The spec project '…' has no built output … Build the solution first (dotnet build).` —
  the CLI and this server **never build**; build before every check, or the results are stale.
- `The spec assembly '…' failed to load its dependency '…' while running Define().` — the spec
  names a NuGet-packaged type via `typeof()`, and that package assembly is not in the spec's
  build output. Add `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` to the
  spec csproj (the scaffold above carries it) and rebuild, or switch the target to a
  namespace pattern.

If discovery still cannot find your spec project, do not stall: pass
`--spec path/to/YourSpec.csproj` to every verb and continue — nothing downstream depends on
convention discovery.

## 3. Draft candidate rules — all as `Enforce`, all of them hypotheses

Turn every step-1 hypothesis into a rule. Do not pre-judge postures — the checker assigns the
evidence in step 4; postures come in step 5. **Author the already-true directions too**: every
layer pair the survey's edge matrix shows clean becomes an Enforce candidate — the cheapest
law you will ever get. A derive that only writes rules about problems under-produces law.

```csharp
Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
Layer web    = arch.Layer("Web",    "MyApp.Web.*");

arch.Rule("layering/domain-independent")
    .Enforce(domain.MustNotReference(web))
    .Because("Candidate: Domain looked UI-agnostic in the survey.");

arch.Rule("data-access/no-inline-sql")
    .Enforce(web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
    .Because("Candidate: externalEdges shows System.Data reached from Web.");
```

- One hypothesis per rule — a rule is one sentence; compound requirements are multiple rules.
- IDs are `area/rule-name`, matching `^[a-z0-9-]+(/[a-z0-9-]+)*$`. They are permanent handles
  (baseline keys, violation citations), so name them as if they will survive — they will.
- `Because` is **required** and will not pass spec build when blank. During drafting a
  candidate note is fine ("Candidate: …"); by step 6 every kept rule needs the real reason.
- Prefer **namespace-pattern targets** for external bans. A bare `typeof(SomeType)` target
  that does not exist in the codebase passes silently (absence is the win condition), so a
  typo'd type name looks like success; a pattern target that matches nothing raises an inert
  warning — the misspelling check comes free.
- Naming/shape candidates come straight from the inventory: interfaces `MustHavePrefix("I")`,
  handler types `MustHaveSuffix("Handler")`, `MustBeSealed()` where the convention looks
  intended. Member-level conventions are candidates too:
  `web.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async")` where the
  codebase names its async methods `*Async`.
- Anchor a rule on the `Layer` handle wherever one exists (`tools.MustHaveSuffix("Tools")`,
  not `arch.Types.InNamespace("MyApp.Tools.*").MustHaveSuffix("Tools")`). The two check
  identically, but render's per-directory local-rules card is keyed on the layer-anchored
  subject — the glob-spelled twin emits no card in that layer's directory, so agents editing
  there never see the rule locally.
- Dragon-zone candidates are the one exception to "all as Enforce": a boundary has no Enforce
  form, so draft them as `arch.Scope(id).Freeze(...)` directly (step 5 shows the full shape).
  The scope's containment violations arrive in step 4 alongside every other rule's evidence.

Build the spec. Spec-build validation reports **every** error at once (missing `Because`,
malformed IDs, dangling rules, blank prose) — fix them in one pass.

## 4. Check = the evidence pass

Run `arch_check` (CLI: `loadbearing check MyApp.sln --json`; exit 1 is expected — violations
are the data). For each rule, read `rules[]`:

- `violations[]` with `sites[]` — the real edges, each with `file:line`. One violation per
  offending type pair; multiple reference sites between the same pair ride together in
  `sites[]`. This is your edge summary at rule precision.
- A violation of kind `emptySubject` ("The subject selection matched no solution-declared
  types.") — **your subject glob is wrong**, not evidence about the code. Check the pattern
  semantics: a trailing `.*` is the subtree operator and is self-inclusive (`MyApp.Domain.*`
  matches `MyApp.Domain` itself); `MyApp.Legacy*` matches within a segment and never crosses
  a dot.
- A warning "This rule is inert: its target selection matched no types." — the target pattern
  matched nothing. Either the glob is wrong or that layer genuinely declares no types; decide
  which before keeping the rule.
- An **error result** (not a violations document) means the run itself failed — an
  unresolvable or unbuilt spec, or spec validation errors. Fix, rebuild, rerun.

Iterate globs until the failures that remain are *genuine* — real edges, real nonconforming
names. Iterating is cheap over MCP: the server holds the workspace warm and reconciles your
edits per call, so a re-check after the first load answers in milliseconds; one-shot CLI runs
pay a workspace load each time (a clean tree with a valid extraction cache skips it). If the
MCP response is truncated on a big solution, check rule-by-rule at the CLI or split the draft
spec temporarily.

## 5. Assign postures from the evidence

For each surviving rule, the violation count decides the honest posture:

- **Zero violations → `Enforce`.** The codebase already obeys; making it law costs nothing
  and protects it from the next change. Keep the rule exactly as drafted; upgrade `Because`
  to the real rationale.
- **Violations, and the team wants the target → `Migrate`.** Rewrite as a pair:

  ```csharp
  arch.Rule("data-access/no-inline-sql")
      .Migrate(
          from: "Controllers reach System.Data directly (legacy inline-SQL style).",
          to: web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
      .Because("Repository pattern for testability — ADR-012.")
      .Fix("Inject the repository; see OrdersRepository for the pattern.");
  ```

  `from:` is **descriptive prose about the OLD pattern, stated factually** — never
  aspiration, never blame. The current violations become the grandfathered baseline in
  step 7; new code in the old pattern goes red from then on. The boy-scout policy defaults to
  `MigrateIfSmall` (override with `.WhileYoureThere(...)`), and the baseline path defaults to
  `arch/baselines/<rule-id>.json` — omit `.Baseline(...)` unless the team wants it elsewhere.

- **A region with no target state → `Freeze`.** No one will fix it; the enforceable thing is
  the boundary:

  ```csharp
  arch.Scope("legacy/billing")
      .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
      .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
      .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
               "Nightly reconciliation depends on this. Do not normalize.")
      .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");
  ```

  **List the facade implementation type(s) in `BoundaryOnlyVia` alongside the interface** —
  the composition root's DI registration references the concrete type, and forgetting it puts
  that registration red on day one. Omit `BoundaryOnlyVia` entirely for a hermetic freeze.
  `Dragons` must carry three things: what the code does, **which weirdness is load-bearing**
  (the behavior a "fix" would break), and the sanctioned interaction surface. "Don't touch,
  it's bad" is not dragons prose — agents still have to call into this code.

  A Freeze desugars to two checkable rules under the scope ID: `{id}/containment` (red on any
  new reference into the scope not via the facade; existing inbound references get
  grandfathered in step 7) and `{id}/tripwire` (a diff-aware warning; it reports as *skipped*
  in `check` runs without `--diff-base` — expected, not a bug).

- **Nothing the team will stand behind → drop the rule.** An unratified rule in the spec is
  exactly the stale-doc problem this tool exists to kill.

## 6. The curation gate — **do not guess**

Stop. Present every proposed rule to the human with its evidence, one row per rule: ID,
proposed posture, the rule sentence, violation count, one example `file:line`, and your draft
`Because`. The human accepts, edits, or drops **each rule individually**. Conflicts you found
in step 0 (doc says X, code does Y) are decided here, not by you.

**Author nothing final until every rule is decided**, and never write a `Because` the human
would not say in a design review — it renders into the generated context and into every
violation message as the team's stated rationale.

## 7. The human baselines the remainder

With the curated spec built and checked, the remaining reds are exactly the acknowledged debt:
each Migrate rule's current violations and each frozen scope's existing inbound references.
Grandfather them:

```
loadbearing baseline MyApp.sln --init
```

**`baseline --init` is run by the human, never by you.** The baseline is the team's signature
on its debt — an attributed, reviewable artifact, not an agent convenience. It captures every
uncaptured ratcheted rule's *current* violations in one pass, writing one entry per line under
`arch/baselines/` (paths resolve against the solution root) — everything red right now,
including code written five minutes ago; day
zero is the one moment "current" and "accepted" coincide, which is why curation comes first.
From then on the ratchet holds: baselined violations pass, new ones go red, and the file only
shrinks (`baseline --accept-reductions`). One precision worth knowing: entries key
subject×target pairs, not sites — every reference site between one pair rides in a single
entry, `status` counts pairs, and a new site inside an already-grandfathered pair does not go
red (a new pair does).

Re-run `arch_check`: expect exit 0, `rulesFailed: 0`, with the grandfathered counts visible.
`arch_status` now shows the per-rule burndown — the numbers the team watches shrink.

## 8. Render and commit

Have the human run `loadbearing render MyApp.sln`: it writes the managed block into the root `AGENTS.md`
(everything outside the markers is preserved byte-for-byte) and drops a second managed
`AGENTS.md` into each frozen scope's directory (the dragons card) and into each layer's
directory when rules are anchored on that layer (the local-rules card). Then commit —
spec project, `arch/baselines/**`, and the rendered
`AGENTS.md` files — as **one reviewable diff**: the reviewer sees the proposed law, the
acknowledged debt, and the generated context in a single change.

Report the outcome: rules by posture, debt counts per Migrate rule, dragons documented, and
anything you dropped at curation (with why) so it is on the record.

---

## Authoring reference (condensed)

A spec is one class implementing `IArchitectureSpec` with one method `Define(Arch arch)`, and
three statement forms: definitions, rules, scopes.

**Nouns** — `arch.Types` (all solution-declared types) · `arch.Layer(name, glob, ...)` ·
`arch.Namespace(glob)` · `arch.Project(name)` · `arch.Type(typeof(X))` (or the sugar
`arch.Type<X>()`) · `arch.Member(typeof(X), nameof(X.M))` (a declared member of `X`, the
`MustNotUse` target form; matching is by declaring type + member name, so one ban covers every
overload) — or the compiler-checked expression forms `arch.Member<X>(x => x.M)` (instance) and
`arch.Member(() => X.M)` (static), which anchor the same member with the type↔member pairing
verified at compile time — and `MustNotUse` accepts these static lambdas bare (see the verb
below). (These expression forms cannot anchor a compiler-inlined member: a
`const` field, `enum` member, or literal is baked to its value with no member left in the tree, so
anchor those with the `typeof`/`nameof` form.)

**Adjectives** (chain onto any selection) — `.InNamespace(glob)` · `.OfKind(TypeKind.Class |
Interface | Struct | Enum | Delegate)` · `.WithSuffix(s)` / `.WithPrefix(s)` /
`.WithNameMatching(glob)` · `.Implementing(type)` / `.Implementing<T>()` · `.DerivedFrom(type)` /
`.DerivedFrom<T>()` · `.AttributedWith(attributeType)` / `.AttributedWith<T>()` ·
`.Except(selection)` · `.Where(pred, description:)`.

**Constraint verbs** (selection → complete sentence) — `MustNotReference` /
`MustOnlyReference` / `MustNotBeReferencedBy` / `MustOnlyBeReferencedBy` (each takes
selections or `typeof()`s, one-or-more) · `MustNotUse(arch.Member(...), ...)` — or, when every
target is a **static** member, the lambdas bare: `MustNotUse(() => DateTime.Now,
() => DateTime.UtcNow)` — (bans member accesses — `DateTime.Now`, `.Result`,
`ConfigurationManager.AppSettings`; *use* = a
source-level member access, and `nameof` operands are not uses) · `MustResideInNamespace(glob)`
· `MustHaveSuffix` / `MustHavePrefix` / `MustHaveNameMatching` · `MustImplement` /
`MustDeriveFrom` / `MustBeAttributedWith` (each with a generic twin — `MustImplement<T>()`,
`MustDeriveFrom<T>()`, `MustBeAttributedWith<T>()`) · `MustBeSealed` / `MustBeStatic` /
`MustBeAbstract` / `MustBePublic` / `MustBeInternal` · `.Must(pred, description:)`.

The generic twins — `arch.Type<X>()`, `.Implementing<T>()` / `.DerivedFrom<T>()` /
`.AttributedWith<T>()`, the three `Must*<T>` verbs, the `arch.Member<X>(x => x.M)` /
`arch.Member(() => X.M)` anchors, and the static `MustNotUse(() => X.M)` verb forms — are pure
sugar for the `typeof`/`nameof` form and reify identically. An **open** generic has no
type-argument form, so it stays `typeof` (`Implementing(typeof(IHandler<>))`,
`.Returning(typeof(Task<>))`). The dependency verbs take
`typeof` or a wrapping `arch.Type<X>()` (never a generic verb); `.Returning` takes `typeof` only:
its sole overload is `Returning(Type, params Type[])`, and a `Selection` such as `arch.Type<X>()`
is not a `Type`.

**Member subjects** — a projection turns any selection into a selection of its declared
members, constrained directly: projections `.Members` / `.Methods` / `.Properties` / `.Fields`
/ `.Events` · member adjectives `.WithSuffix` / `.WithPrefix` / `.WithNameMatching` ·
`.Returning(typeof(Task))` (methods-only, so it chains only off `.Methods`; matches the
declared return type at the definition level — `typeof(Task<>)` matches every construction,
and a closed generic like `typeof(Task<int>)` is refused) · `.Where(pred, description:)` ·
member verbs `MustHaveSuffix` / `MustHavePrefix` / `MustHaveNameMatching` · `MustBePublic` /
`MustBeInternal` / `MustBePrivate` · `MustBeStatic` / `MustBeAbstract` / `MustBeVirtual` ·
`.Must(pred, description:)` (member predicates see `IMemberInfo`). The flagship:
`web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")` — *"Methods of types in
`MyApp.Web.*` returning `Task` must be named `*Async`."*

**Postures** — `arch.Rule(id).Enforce(constraint)` · `arch.Rule(id).Migrate(from:, to:)`
[`.Baseline(path)`] [`.WhileYoureThere(MigrationPolicy.MigrateIfSmall | AlwaysMigrate |
NeverExpand)`] · `arch.Scope(id).Freeze(selection)` [`.BoundaryOnlyVia(types...)`]
[`.Dragons(prose)` / `.DragonsDoc(path)`] [`.Baseline(path)`].

**Trailers** — `.Because(prose)` required everywhere; `.Fix(prose)` optional (for containment
it is auto-derived from the facade list).

**Escape hatches** — descriptions are required parameters and must be non-blank. `Where`
descriptions are relative clauses continuing the noun ("whose name contains a digit"); `Must`
descriptions are bare-infinitive phrases completing "must …" ("keep type names at or under 40
characters"). The predicate sees `ITypeInfo`: `Name`, `Namespace`, `Kind`, `ProjectName`,
`Accessibility`, `IsSealed`, `IsStatic`, `IsAbstract`, `IsRecord` (how record rules are
written), `FilePaths`, attributes, base type, interfaces.

**Namespace patterns** (dot-segment aware, case-sensitive) — trailing `.*` = the subtree
*including the namespace itself*; interior `*` = exactly one segment; partial `Legacy*` =
within one segment, never crossing a dot; lone `*` = everything. So `MyApp.Domain.*` matches
`MyApp.Domain` and `MyApp.Domain.Orders` but not `MyApp.DomainX`.

**Semantics worth knowing** — "reference" means a source-level type reference; "use" means a
source-level member access: the checker records both edge kinds. Member bans are
source-visibility bans, not runtime-dispatch bans — a ban on a concrete member does not catch
calls through an interface-typed receiver, nor the reverse. Member subjects range over the
declared members of solution-declared types (accessors, constructors, operators, indexers,
and compiler-generated members are excluded; external types carry no member inventory), and
an empty member subject fails the rule exactly like an empty type subject. Type subjects
range over solution-declared types; targets also reach external (BCL/NuGet) types. `MustOnlyReference` constrains solution-declared targets only (external
packages are exempt, and the rendered sentence says so) and is strict — list a layer's own
selection among its allowed targets if self-references are fine. `Implementing`/`DerivedFrom`
are transitive with type-argument substitution; an open generic (`typeof(IHandler<>)`)
matches any construction. `AttributedWith` sees declared attributes only. Hierarchy
adjectives never match external types (their hierarchy is not extracted). Closed generics are
refused in reference positions — ban the open definition and/or the argument type. An empty
subject fails the rule; an inert target warns; both are authoring signals, not code evidence.

## Where to go deeper

- `arch_explain <rule-id>` (CLI: `loadbearing explain`) — any rule's because / fix / posture
  payload, including desugared `{scope-id}/containment` and `{scope-id}/tripwire` children.
- `arch_context <path>` — the architecture scope cards covering a directory (a frozen
  scope's dragons, a layer's local rules).
- `loadbearing status` — the burndown after baselining.
- The generated `AGENTS.md` block is the always-on summary; this recipe's output is what
  keeps it true.
