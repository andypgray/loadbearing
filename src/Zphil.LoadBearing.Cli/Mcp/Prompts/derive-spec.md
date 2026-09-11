# Derive an architecture spec for an existing codebase

Your goal is a **compiling LoadBearing spec that states this codebase's architecture honestly,
as it is today**: the target rules new code must follow (`Enforce`), the known debt being
worked off (`Migrate`), and the untouchable dragons (`Quarantine`). A legacy codebase's
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
  `arch_status`) or their identical CLI verbs (`loadbearing graph|check|explain|status`, add
  `--json` for the same documents). Prefer the tools: they are already bound to the solution,
  the workspace stays warm between calls, and their narrowing parameters (`overview`,
  `skeleton` and `index` for grain, `projects` for scope on the survey, `rules` on the check)
  keep a big solution's answer a complete document rather than a truncated one. The CLI verbs are for when a document belongs in a
  file. They take the solution path as their first argument — `loadbearing graph MyApp.sln
  --json` — or walk up from the working directory when omitted (`explain` differs: its rule
  ID comes first, the solution second — `loadbearing explain area/rule MyApp.sln`). The two
  ratchet mutations that end the flow — `loadbearing baseline --init` and the commit — belong
  to the human. A session launched from the MCP registry manifest has no `loadbearing` command:
  there, prefix every CLI verb in this recipe with `dotnet dnx Zphil.LoadBearing.Cli@... --yes --`.

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
one tool that works before any spec exists — and returns the codebase as extraction sees it.

It does need the solution **restored and built**. If a project fails to load, the call returns an
error naming it instead of a survey: a map missing whole projects would send the whole derivation
down a wrong path, and there is no later step that would catch it. Restore and build, then retry.
Survey the partial model deliberately (`allowWorkspaceDiagnostics: true`) only if some project
genuinely cannot be made to load, and then treat every conclusion below as provisional.

- `projects[]` — each project's declared `projectReferences`, type count, and exact namespace
  inventory with type counts. The namespace inventory is your raw material for layer globs.
  `solutionMember: false` marks a project a `ProjectReference` dragged into the workspace that
  the solution file does not declare — a passenger, not part of the estate you are writing law
  for, so keep it out of your layer globs. An absent key means membership could not be read.
  `targetFrameworks` lists every framework the project declares, ordinal-ordered by framework
  name — extraction's own order, never the csproj's — normalized to the short moniker, so a
  project predating the SDK reads `net48` beside everything else. `factsFollow` names the
  framework whose compilation supplied the facts for the types more than one framework
  declares; it is absent when the frameworks share no type (every type then keeps its own
  framework's facts), and absent for every project that compiled once. Read both before writing
  a rule whose subject is a multi-targeting project: the rule is checked against
  `factsFollow`'s compilation alone, and a `#if`-divergent branch on a losing framework is
  invisible to it.
  `isPackable`, `locksPackages` and `packageReferences[]` are the artifact facts, and they are
  evaluated rather than read off the project file — which is the only way they can be right.
  The SDK makes a project packable without anybody writing it down, and a lock-file policy is
  regularly declared once in a props file above the whole solution, so the project's own XML
  says nothing about either. `packageReferences[]` is what the project declares, never the
  transitive closure. An absent key means no answer: nothing evaluated the project, or — for
  `isPackable` alone — the project is outside the SDK's pack machinery and has none.
  `generated` qualifies a type count — on the project, and on each namespace — with how many of
  those types a generator emitted; it is absent when none were. **A namespace whose `generated`
  equals its `types` is wholly generator output: never make it a layer glob and never anchor a
  rule on it.** A compiled view tier collects hundreds of such types under one namespace nobody
  typed, and naming it would aim your law at code no one can fix. Where a project's `generated`
  is a large share of its `types`, prefer namespace subjects over `arch.Project(...)`, or narrow
  the project noun with `.Authored()`.
- `projectEdges[]` — **observed** project→project references (distinct type pairs), and only
  references the code declares: a project reaching a type it compiles itself is not an edge,
  however extraction attributed that type (see `multiplyDeclaredTypes[]` below). Compare
  against the declared references: a declared reference with no observed edge is a dead
  reference (note it for the human; it is cleanup evidence, not a rule). An observed edge you
  did not expect is the interesting kind.
- `externalEdges[]` — external references grouped by namespace root. Scan for the classic
  dangerous externals: `System.Data` (inline SQL), `System.Web` (HttpContext-era coupling),
  direct driver namespaces, and anything the team says it is migrating away from.
- `multiplyDeclaredTypes[]` — the types more than one project declares, because one source file
  is compiled into several of them (a `<Compile Include>` link, shared source, a polyfill).
  Each entry names the type, every project declaring it, and the one whose facts and project
  attribution it follows. Read this **before** anchoring a subject on a project:
  `arch.Project(...)` on any declarer selects the type, its facts answer from the declarer the
  entry names, and a declarer's reference to its own compiled-in copy is intra-project rather
  than an edge to that declarer. The key is absent when the solution has none, which is
  the common case.
- `shadowedTypes[]` — the full names a project declares that a referenced assembly also supplies:
  a stand-in written under a package's own namespace, a polyfill under a BCL one. The name means
  two types, and each reference reaches whichever the referencing project bound, so a rule naming
  the type reaches both while `arch.Project(...)` over the declaring project reaches only the
  declaration. Each entry names the type, that project, the supplying assemblies, and the projects
  binding the assembly instead. Absent when the solution has none.
- `unsupportedProjects[]` — the projects the solution declares that this survey does not cover,
  each with its reason. A project in another language (an `.fsproj`, a `.vbproj`, a `.sqlproj`) is
  not in `projects[]`, contributes no edges, and can never violate a rule you write. A shared
  project (`.shproj`) is not in `projects[]` either, but its `.projitems` are compiled into every
  project that imports it, so its code is already surveyed under those names. Read this **first**,
  before treating `projects[]` as the estate: without it the roster is simply shorter than the
  solution and nothing says so, and a spec derived from it silently cannot reach whatever ships
  from those projects. Absent when the survey covers every declared project. Unlike the two
  coverage keys above it is never elided — it is bounded by the solution, not the codebase.

The document's keys, exactly (camelCase; an optional field is absent, never null):

```text
projects[]              { name, solutionMember?, targetFrameworks?, factsFollow?, isPackable?, locksPackages?, projectReferences[], packageReferences[], types, generated?, namespaces[]{ namespace, types, generated? } }
projectEdges[]          { source, target, references }
externalEdges[]         { source, targetNamespaceRoot, references }
multiplyDeclaredTypes[] { type, declaredBy[], factsFollow }
shadowedTypes[]         { type, declaredBy, suppliedBy[], boundFromAssemblyBy[] }
unsupportedProjects[]   { project, reason }
```

Grain is a ladder, and an over-budget survey walks down it by itself rather than coming back
cut. At overview grain — `overview: true`, or the server's own first step — the document
stamps `"grain": "overview"` and elides each project's `namespaces`; every project, edge and
external row survives. At skeleton grain — `skeleton: true`, or the server's second step when
the overview is still too big — it stamps `"grain": "skeleton"` and drops `externalEdges[]`
and `multiplyDeclaredTypes[]` and `shadowedTypes[]` too, reporting how many rows went as
`externalEdgeCount`, `multiplyDeclaredTypeCount` and `shadowedTypeCount`; each project's
`packageReferences[]` goes with them, being the build-side twin of the external rows. The
projects and their edges stay. At index grain — `index: true`, the ladder's floor — it stamps
`"grain": "index"` and keeps the roster alone: every project's `name`, `solutionMember` and
`types`, with its `projectReferences`, framework pair and packaging pair gone and
`projectEdges[]` reported as `projectEdgeCount`. That is the list `projects` globs match, so a
survey that degrades this far hands you the argument for the next call. `unsupportedProjects[]`
survives every rung whole, having no count key at all, and so does a project's own `generated`,
riding its row; the framework pair (`targetFrameworks`, `factsFollow`) and the packaging pair
(`isPackable`, `locksPackages`) ride it the same way down to skeleton.
Only the per-namespace `generated` goes with the inventory that carries it — so at any grain
you can still see which projects are mostly generator output, and drop to full grain to see
which namespaces. Read the stamp: a survey with no `grain` is the complete one. An absent
coverage key with no count beside it means the solution has none; the count key is what tells
elision from absence.

Scope is the other axis. `projects` (name globs) narrows the survey and stamps
`projectsScope`; edges keep both directions, so a scoped `projectEdges[]` can name a project
outside the roster, and a `multiplyDeclaredTypes[]` entry survives when any of its declarers is
in scope. A `shadowedTypes[]` entry survives on either end too — its declaring project, or any
project binding the assembly. `unsupportedProjects[]` does not scope either: it is a fact about
the load rather than about the roster. On a solution too big to survey whole even at index
grain, scope is the knob left — grain has nowhere further to go, and the index document you
are holding names every project you can scope to.

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
        <!-- The spec pins its own LoadBearing version, so this project builds the same whether or not
             the repository manages package versions centrally. Without it, a repository carrying a
             Directory.Packages.props fails the restore with NU1008. -->
        <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Zphil.LoadBearing" Version="..." />
        <!-- In a source checkout, a (relative) ProjectReference to Zphil.LoadBearing works too;
             replace it with the PackageReference once the package is published. -->
    </ItemGroup>
</Project>
```

The four LoadBearing packages ship one version in lockstep: reference the version
`loadbearing --version` prints. Where the repository manages package versions centrally, leave its
`Directory.Packages.props` alone — the property above opts this one project out, so the spec keeps
its own pin and the central file needs no entry for it.

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

Name the project — and with it its root namespace — so the **last segment is not `Arch`**: inside
a namespace ending in `.Arch`, the simple name `Arch` binds to that namespace rather than to the
`Arch` type `Define` takes, and the spec does not compile (CS0118, verbatim below).
`MyApp.ArchSpec` is safe, and so is the `arch/` folder — only the namespace segment collides.

Add it to the solution (`dotnet sln add arch/MyApp.ArchSpec/MyApp.ArchSpec.csproj`) and build.
Spec discovery is by convention: **the unique solution project that references
`Zphil.LoadBearing.dll`** — via the package or via a project reference. As a solution member,
the spec project is excluded from the checked universe — its own types never trip your rules.

**Expect `dotnet sln add` to rewrite more of a `.sln` than the one project it adds.** The CLI
unions its default platforms (`x64`, `x86`) into the solution's configuration list, then writes the
project-configuration table out complete: an `ActiveCfg`/`Build.0` pair per project per
configuration × platform combination. On a dozen-project solution that is a hundred-plus inserted
lines for the one project you added. The rewrite is legitimate, not damage: those mappings are what
make the spec project, and every other member, build under every combination the solution declares.
Do not revert it to hand-write a narrower entry mapping only `Debug|Any CPU` and `Release|Any CPU`.
The project registers either way (`dotnet sln list` shows it), but a solution build under any other
declared combination skips a project with no mapping for it — MSBuild names it in an `MSB4121`
warning and still exits 0, so the build stays green while `check` reads a stale spec assembly, or
none at all.

**Source-checkout setups only**: `dotnet sln add` follows project references, so it may also
add the LoadBearing contract library to the solution — and **solution membership decides
whether its types come under your rules**. A contract library the solution declares is checked
like any other member; one that only rides in as a project reference of the spec counts as the
spec's private plumbing and stays out of the checked universe. It appears in the survey either
way, because `arch_graph` is spec-less and excludes nothing. Ignore it there, and scope broad
subjects to your product namespaces
(`arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*")`, not bare
`arch.Types.OfKind(...)`) so convention rules never bite the tooling. A `PackageReference`
setup has none of this — the package is a metadata reference, not a project. (Outside the
LoadBearing repo, expect `dotnet sln add` to record the contract library by a long relative
path — harmless, but it will show in the solution diff; the published package is the cure.)

Test projects need the same subject-scoping care in ANY setup: a test project sharing the
product root namespace (`MyApp.Tests.*` inside `MyApp.*`) is solution-declared, so
`.InNamespace("MyApp.*")` subjects include its types — fakes and test helpers then pollute
naming, member, and hierarchy rules. Anchor product-wide subjects on `arch.Project("MyApp")`
instead; a project noun never crosses project boundaries. When the product is several projects,
name them as one union — `arch.AnyOf(arch.Project("MyApp"), arch.Project("MyApp.Data"))` — rather
than a namespace cone minus an `.Except`: the union names exactly what you mean, while the cone
also sweeps every other solution member that happens to sit inside it.

Errors you may see, verbatim, and what they mean:

- `error NU1008: The following PackageReference items cannot define a value for Version:
  Zphil.LoadBearing. Projects using Central Package Management must define a Version value on a
  PackageVersion item.` — NuGet's, not this tool's, so nothing in it names LoadBearing as the cause.
  The repository has a `Directory.Packages.props` at or above the spec project and the
  `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` line is missing from the
  spec csproj. Put it back rather than adding a `PackageVersion` entry to the central file: the
  spec's pin belongs to LoadBearing and moves when the tool does, not with the repository's own
  dependencies.
- `error CS0118: 'Arch' is a namespace but is used like a type`, beside a CS0535 that the spec
  class does not implement `IArchitectureSpec.Define(Arch)` — the compiler's, not this tool's, so
  nothing in either names the cause. The spec project's namespace ends in `.Arch`, and inside that
  namespace the simple name `Arch` is the namespace itself, shadowing the contract type. Rename the
  project and namespace so the last segment is not `Arch` (the scaffold's `MyApp.ArchSpec` shape);
  qualifying `Zphil.LoadBearing.Arch` in the signature also compiles, but leaves the shadow armed
  for every file the spec grows.
- `No spec project found: no solution project references Zphil.LoadBearing.dll. Pass --spec
  to name one.` — the spec project is not in the solution yet (`dotnet sln add`), or you need
  an explicit `--spec`. The line below it says how many C# projects the workspace held: a count
  far short of the solution's is the real finding, not the missing spec project.
- `No spec project found: N projects failed to load, so a project that references
  Zphil.LoadBearing.dll may be among them:` — followed by every failed project, uncapped. The
  one that would have matched may be among the casualties, and `--spec` cannot repair a load,
  so the remedy is the one the refusal names: restore and build, then retry.
- `No spec project found: NuGet packages did not resolve for N projects, so a project that
  references Zphil.LoadBearing.dll may have failed to resolve it:` — the quieter break: every
  named project loaded completely, but without its package references, so a reference to the
  contract library resolves to nothing and convention discovery cannot see it. Restore, then
  retry. One broken tree can raise this and the previous refusal at once — they compose, each
  naming its own projects.
- `No spec project found: the workspace did not load cleanly, so a project that references
  Zphil.LoadBearing.dll may have failed to resolve it:` — followed by the diagnostics, up to
  three of them: load problems that blame no project in particular, which is why this one says
  "may" — that is all it knows. The spec project may well be there, its reference to the
  contract library hidden by whatever the diagnostics describe. Restore and build, then retry.
  `--spec` cannot help here — an unresolved reference is unresolved whichever project you name.
- `Multiple spec projects found; pass --spec to disambiguate:` — more than one project
  references the contract library; the listed lines name each one's `.csproj`, so pass yours.
  A project that multi-targets is listed once: its frameworks are one spec project.
- `The spec project '…' has no built output … Build the solution first (dotnet build).` —
  the CLI and this server **never build**; build before every check, or the results are stale.
- `The spec assembly '…' failed to load its dependency '…' while running Define().` — a
  `typeof()` anchor names a type whose assembly the loader cannot reach. The message says which
  of two situations you are in, so read its second line rather than reaching for the setting.
  If the assembly is a **NuGet package**, `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`
  in the spec csproj (the scaffold above carries it) plus a rebuild stages it. If it is a **.NET
  shared framework** — anything reached through `<FrameworkReference Include="Microsoft.AspNetCore.App" />`,
  such as an MVC `ControllerBase` or a `Microsoft.WindowsDesktop.App` type — **no build setting
  can help**: that assembly is never staged into a spec's output and the tool's own host does not
  carry it. Anchor those by string instead — `.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")`
  renders exactly like the `typeof()` form and needs no assembly load — or target a namespace
  pattern.

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
- Token-presence candidates: where the codebase's `Task`-returning methods already accept a
  `CancellationToken`, the convention is one line —
  `web.Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken))`.
  Matching is declaration-level and exact (a `CancellationToken?` or `params CancellationToken[]`
  parameter is a different declared type and does not count); whether a body *flows* the token
  is call-site analyzer territory, not this rule's.
- DI-construction candidates: a type resolved through a container or a discovery registry may be
  *referenced* but must not be *constructed* — `arch.Types.Except(arch.Type<HandlerRegistry>())
  .MustNotConstruct(arch.Types.Implementing(typeof(IHandler<>)))` where a family of types
  (handlers, commands, services) is meant to arrive via one sanctioned root, so a stray `new`
  bypasses discovery. `.Except` the composition root that legitimately `new`s them.
- DI-lifetime candidates: where the composition root registers services with
  `AddSingleton`/`AddScoped`/`AddTransient` (or `TryAdd*`, `AddHostedService`, `AddDbContext`,
  `AddHttpClient<TClient>`), the captive-dependency rule is one line —
  `arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped),
  arch.Registered(Lifetime.Transient))`. Registrations made by assembly scanning, factory
  internals, or framework defaults are not seen, so an empty-subject failure here means the
  registrations live outside the recognized calls — drop the rule rather than guessing.
- Anchor a rule on the `Layer` handle wherever one exists (`tools.MustHaveSuffix("Tools")`,
  not `arch.Types.InNamespace("MyApp.Tools.*").MustHaveSuffix("Tools")`). The two check
  identically, but render's per-directory local-rules card is keyed on the layer-anchored
  subject — the glob-spelled twin emits no card in that layer's directory, so agents editing
  there never see the rule locally.
- Dragon-zone candidates are the one exception to "all as Enforce": a boundary has no Enforce
  form, so draft them as `arch.Scope(id).Quarantine(...)` directly (step 5 shows the full shape).
  The scope's containment violations arrive in step 4 alongside every other rule's evidence.

Build the spec. Spec-build validation reports **every** error at once (missing `Because`,
malformed IDs, dangling rules, blank prose) — fix them in one pass.

## 4. Check = the evidence pass

Run `arch_check` (CLI: `loadbearing check MyApp.sln --json`; exit 1 is expected — violations
are the data). For each rule, read `rules[]` — keyed by `id` (`ruleId` is SARIF's spelling,
not this document's), camelCase and exact like the step-1 key map:

- `violations[]` with `sites[]` — the real edges, each with `file:line`. One violation per
  offending type pair; multiple reference sites between the same pair ride together in
  `sites[]`. This is your edge summary at rule precision. Beside them, `violationCount` and
  `siteCount` are present at every grain — script against the counts (step 5 reads the
  violation count); the arrays are their expansion and are elided at coarser grain.
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
pay a workspace load each time (a clean tree with a valid extraction cache skips it). On a
big solution, narrow instead of leaving: `rules` takes rule-ID globs (`rules:
"data-access/*"`; the CLI twin is `--rules`), so the evidence pass can walk the draft area
by area with every response a complete document, and `arch_explain` returns one rule whole.
You never have to page a report out to read it entire: over your client's budget it comes
back whole at a coarser grain rather than cut, down to `index` — every rule ID with its
verdict and violation count, which is the menu those globs pick from.

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

- **A region with no target state → `Quarantine`.** No one will fix it; the enforceable thing is
  the boundary:

  ```csharp
  arch.Scope("legacy/billing")
      .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
      .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
      .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
               "Nightly reconciliation depends on this. Do not normalize.")
      .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");
  ```

  **List the facade implementation type(s) in `BoundaryOnlyVia` alongside the interface** —
  the composition root's DI registration references the concrete type, and forgetting it puts
  that registration red on day one. Omit `BoundaryOnlyVia` entirely for a hermetic quarantine.
  `Dragons` must carry three things: what the code does, **which weirdness is load-bearing**
  (the behavior a "fix" would break), and the sanctioned interaction surface. "Don't touch,
  it's bad" is not dragons prose — agents still have to call into this code.

  A Quarantine desugars to two checkable rules under the scope ID: `{id}/containment` (red on any
  new reference into the scope not via the facade; existing inbound references get
  grandfathered in step 7) and `{id}/tripwire` (a diff-aware warning; it reports as *skipped*
  in `check` runs without `--diff-base` — expected, not a bug).

- **Nothing the team will stand behind → drop the rule.** An unratified rule in the spec is
  exactly the stale-doc problem this tool exists to kill.

Before moving on, check the invariant those four bullets exist to leave true: **`Enforce`
carries no baseline**, so `baseline --init` cannot grandfather it and a red Enforce rule
stays red for good. Every rule that still has violations must leave this step re-postured to
`Migrate`, fenced inside a `Quarantine`, dropped, or named in the handoff as work the team is
choosing to do now. An unexplained red is the one outcome the flow cannot recover from,
because the reader cannot tell it from a mistake they made.

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
each Migrate rule's current violations and each quarantined scope's existing inbound references.
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

When a failing rule was left `Enforce`, `--init` says so rather than staying quiet: a notice
names each rule failing with no baseline to capture, with its violation count (on a spec with
no ratcheted rules at all, it leads with `nothing to capture`). Enforce is the one posture a
baseline cannot absorb, so every rule in that list is either a step-5 miss — go back and
re-posture, fence, or drop it — or work the team is choosing to do now, which the handoff
must say.

Re-run `arch_check`. If step 5 ratcheted every violating rule, expect exit 0,
`rulesFailed: 0`, with the grandfathered counts visible. If reds were left deliberately, the
re-check still exits 1 and those rules — and only those — are what remains: state each one
with its violation count in the outcome report, so the reader can tell a chosen red from a
mistake they made. `arch_status` now shows the per-rule burndown — the numbers the team
watches shrink.

## 8. Render and commit

Have the human run `loadbearing render MyApp.sln`: it writes the managed block into the root `AGENTS.md`
(everything outside the markers is preserved byte-for-byte) and drops a second managed
`AGENTS.md` into each quarantined scope's directory (the dragons card) and into each layer's
directory when rules are anchored on that layer (the local-rules card). Then commit —
spec project, `arch/baselines/**`, and the rendered
`AGENTS.md` files — as **one reviewable diff**: the reviewer sees the proposed law, the
acknowledged debt, and the generated context in a single change. On a multi-configuration `.sln`
the largest hunk in that diff is often the solution file itself: the configuration-table expansion
from step 2, which is mechanical, expected, and not to be trimmed by hand.

Report the outcome: rules by posture, debt counts per Migrate rule, any rule left
deliberately red with its violation count, dragons documented, and anything you dropped at
curation (with why) so it is on the record.

---

## Authoring reference (condensed)

A spec is one class implementing `IArchitectureSpec` with one method `Define(Arch arch)`, and
three statement forms: definitions, rules, scopes.

**Nouns** — `arch.Types` (all solution-declared types) · `arch.Layer(name, glob, ...)` ·
`arch.Namespace(glob)` · `arch.Project(name)` · `arch.Type(typeof(X))` (or the sugar
`arch.Type<X>()`) · `arch.AnyOf(a, b, ...)` (the union of any selections — the way to say "these
four projects" in one subject; `arch.AnyOf(typeof(X), typeof(Y), ...)` is the multi-type sugar.
Adjectives apply to the union, not through it: `AnyOf(a, b).Except(c)` is (a ∪ b) − c. Every
operand must match at least one type in subject position, so a typo'd operand fails loudly
rather than hiding behind its siblings) · `arch.Registered(Lifetime.Singleton)` (types named in a source-visible
container registration at that lifetime — service and implementation alike; `arch.Registered()`
= any lifetime) · `arch.Member(typeof(X), nameof(X.M))` (a declared member of `X`, the
`MustNotUse` target form; matching is by declaring type + member name, so one ban covers every
overload) — or the compiler-checked expression forms `arch.Member<X>(x => x.M)` (instance) and
`arch.Member(() => X.M)` (static), which anchor the same member with the type↔member pairing
verified at compile time — and `MustNotUse` accepts these static lambdas bare (see the verb
below). (These expression forms cannot anchor a compiler-inlined member: a
`const` field, `enum` member, or literal is baked to its value with no member left in the tree, so
anchor those with the `typeof`/`nameof` form.)

**Adjectives** (chain onto any selection) — `.InNamespace(glob)` · `.OfKind(TypeKind.Class |
Interface | Struct | Enum | Delegate)` · `.WithSuffix(s)` / `.WithPrefix(s)` /
`.WithNameMatching(glob)` · `.Implementing(type)` / `.Implementing<T>()` / `.Implementing("Fully.Qualified.Name")` ·
`.DerivedFrom(type)` / `.DerivedFrom<T>()` / `.DerivedFrom("Fully.Qualified.Name")` ·
`.AttributedWith(attributeType)` / `.AttributedWith<T>()` / `.AttributedWith("Fully.Qualified.NameAttribute")` ·
`.Except(selection)` · `.Where(pred, description:)` · `.Authored()` (drops source-generated
types — `[GeneratedCode]` on the type or its container; a project noun otherwise names them).

The **string overload** on every hierarchy and attribute anchor position — the three adjectives
above, and the `Must[Not]Implement` / `Must[Not]DeriveFrom` / `Must[Not]BeAttributedWith` verbs
below — is the escape hatch for a type the spec project cannot compile against, and it renders
byte-identically to the `typeof()` form. Reach for it whenever a reference on the spec project
would be the only reason to add one, and *always* for a **.NET shared framework** type such as an
MVC `ControllerBase`, which no build setting can stage into a spec's output (see the load-failure
message in step 2). It anchors fine either way: only an external **subject** carries a shallow
hierarchy, while a declared subject's base chain and interface closure are walked through metadata,
so `arch.Types.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` selects your controllers. A
string always names the **definition**, so it reads like the open-generic `typeof` form
(`"IHandler<T>"` matches every construction) and a constructed spelling matches nothing.

**Constraint verbs** (selection → complete sentence) — `MustNotReference` /
`MustOnlyReference` / `MustNotBeReferencedBy` / `MustOnlyBeReferencedBy` (each takes
selections or `typeof()`s, one-or-more) · `MustOnlyReferenceItself()` (nullary — a leaf of the
reference graph, whose whole allow-set is the subject) · `MustNotUse(arch.Member(...), ...)` — or, when every
target is a **static** member, the lambdas bare: `MustNotUse(() => DateTime.Now,
() => DateTime.UtcNow)` — (bans member accesses — `DateTime.Now`, `.Result`,
`ConfigurationManager.AppSettings`; *use* = a
source-level member access, and `nameof` operands are not uses) ·
`MustNotConstruct(target, …)` (bans object creation — `new`, including target-typed `new()`; the
constructed type may be *referenced* but not *created*, keying the (source, constructed) type pair) ·
`MustNotInject(target, …)` (bans constructor-parameter dependencies, primary constructors
included, keying the (source, injected) type pair; the natural operands are `Registered`
selections — `arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped),
arch.Registered(Lifetime.Transient))` is the captive-dependency rule) ·
`MustBeRegistered()` (nullary — the completeness half beside `MustNotInject`'s shape half: the
injection verb constrains what the registered may depend on, this one demands the registration
itself. Membership is `arch.Registered()`'s exactly, any lifetime, so the same visibility
boundary applies with its polarity inverted: a registration the checker cannot see reds a
correctly registered type, so an estate that registers through invisible routes should not use
the verb) ·
`MustNotCatch(target, …)` (bans `catch` clauses naming the target, keying the (source, caught)
type pair; matching is exact at the definition level, so banning `Exception` does not ban its
subclasses — though a bare `catch` counts as catching `Exception`) ·
`MustNotCatchUnfiltered(target, …)` (the same ban narrowed to `catch` clauses that carry no
`when` filter — a filtered broad catch is the good state it rewards; filter presence is
syntactic, so `when (true)` counts as filtered, and a violation's sites are the unfiltered
clauses alone, keying the same (source, caught) type pair) ·
`MustNotSwallow(target, …)` (the same axis narrowed once more, to `catch` clauses that neither
carry a `when` filter nor end their block in a `throw` — a filtered catch and a rethrowing
catch are both good states it rewards; the throw fact is the block's last statement, syntactic,
never an all-paths analysis, and the same (source, caught) type pair is keyed) ·
`MustOnlyThrow(target, …)` (the strict throw allow-list: every `throw new X()` / `throw expr`
must mint a listed type, with no exemption for external packages, keying the (source, thrown)
type pair; a bare rethrow `throw;` mints nothing) ·
`MustNotThrow(target, …)` (the ban polarity beside it, for when the forbidden thrown types are
enumerable and the permitted ones are not — enumerating fifteen legitimate ones to ban three is
the wrong tool; exact definition-level matching again, so banning `Exception` does not reach a
derived throw) ·
`MustNotExpose(target, …)` (bans a type appearing in a public signature position — a return,
parameter, or property/field/event type — of an effectively-public member, keying the (source,
exposed) type pair; the type may be *referenced* internally but not *surfaced* on the public API) ·
`MustResideInNamespace(glob)` ·
`MustResideInProject(name)` (one project name, no glob; a type that several projects compile is
satisfied by any of its declarers, agreeing with `arch.Project`) ·
`MustBelongTo(membership, …)` (the coverage verb: each membership is a selection naming where a
type may live — layers, projects, namespaces — and a subject type no membership names is red;
"any of several projects" is this verb with project memberships, and there is deliberately no
`typeof` membership form — a single-type membership would collapse into "must be that type") ·
`MustHaveExactlyOneCounterpart(among: selection, named: "I{Name}")` (the correspondence verb: per
subject, every `{Name}` in the template is replaced by the subject's simple name — ordinal,
arity-free, nested types by their leaf name — and exactly one type in `among:` must carry the
derived name; zero counterparts and several are both red. E.g.
`arch.Types.WithSuffix("Service").MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}")`
demands exactly one `I{Name}` interface per service. One `among:` selection only — union candidate
homes with `arch.AnyOf`; write `among:`/`named:` as named arguments at every call site)
· `MustHaveSuffix` / `MustHavePrefix` / `MustHaveNameMatching` · `MustImplement` /
`MustDeriveFrom` / `MustBeAttributedWith` (each with a generic twin — `MustImplement<T>()`,
`MustDeriveFrom<T>()`, `MustBeAttributedWith<T>()`) · `MustNotImplement(type, …)` /
`MustNotDeriveFrom(type, …)` / `MustNotBeAttributedWith(type, …)` (the negative hierarchy/attribute
bans — none-of over the anchors, so unlike the single-`Type` positives they take one-or-more anchors;
each with a generic twin — `MustNotImplement<T>()`, `MustNotDeriveFrom<T>()`,
`MustNotBeAttributedWith<T>()`) · `MustBeSealed` / `MustBeStatic` /
`MustBeAbstract` / `MustBePublic` / `MustBeInternal` · `.Must(pred, description:)`.

The generic twins — `arch.Type<X>()`, `.Implementing<T>()` / `.DerivedFrom<T>()` /
`.AttributedWith<T>()`, the `Must[Not]*<T>` hierarchy verbs (member-side pair included), the `arch.Member<X>(x => x.M)` /
`arch.Member(() => X.M)` anchors, and the static `MustNotUse(() => X.M)` verb forms — are pure
sugar for the `typeof`/`nameof` form and reify identically; a generic twin needs the same
compile-time reference the `typeof` does, so where you cannot have one, use the string overload
above rather than reaching for `<T>`. An **open** generic has no
type-argument form, so it stays `typeof` (`Implementing(typeof(IHandler<>))`,
`.Returning(typeof(Task<>))`). The dependency verbs take
`typeof` or a wrapping `arch.Type<X>()` (never a generic verb); `.Returning` and
`MustAcceptParameter` take `typeof` only (`Returning(Type, params Type[])`,
`MustAcceptParameter(Type)`), and a `Selection` such as `arch.Type<X>()` is not a `Type`.

**Member subjects** — a projection turns any selection into a selection of its declared
members, constrained directly: projections `.Members` / `.Methods` / `.Properties` / `.Fields`
/ `.Events` · member adjectives `.WithSuffix` / `.WithPrefix` / `.WithNameMatching` ·
`.Returning(typeof(Task))` (methods-only, so it chains only off `.Methods`; matches the
declared return type at the definition level — `typeof(Task<>)` matches every construction,
and a closed generic like `typeof(Task<int>)` is refused) · `.AttributedWith(attributeType)`
(declared member attributes only, with the same `<T>` and string forms as the type-side
adjective; renders as a prefix on the subject head — "`[Audit]`-attributed methods of …") ·
`.ThatAreStatic()` (the static members alone; prefixes the head the same way — "static fields
of …" — and stacked prefixes concatenate in authoring order) · `.Where(pred, description:)` ·
member verbs `MustHaveSuffix` / `MustHavePrefix` / `MustHaveNameMatching` · `MustBePublic` /
`MustBeInternal` / `MustBePrivate` · `MustBeStatic` / `MustBeAbstract` / `MustBeVirtual` ·
`MustBeAttributedWith` / `MustNotBeAttributedWith` (the type-side pair again, generic twins and
string forms included) ·
`MustAcceptParameter(typeof(CancellationToken))` (methods-only, so it chains only off
`.Methods` like `.Returning`; one anchor, matched at the definition level — `typeof(IProgress<>)`
matches every construction, and a closed generic is refused) ·
`MustBeGetOnly()` (properties-only, so it chains only off `.Properties`; strict about the
declaration — an `init`-only setter is a setter, so it reds) · `MustBeReadonly()` (fields-only,
so it chains only off `.Fields`; a `const` field satisfies it, const being readonly's superset) ·
`.Must(pred, description:)` (member predicates see `IMemberInfo`, parameters included). The flagship:
`web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")` — *"Methods of types in
`MyApp.Web.*` returning `Task` must be named `*Async`."*

**Project subjects** — `arch.Projects` names the solution's projects as build artifacts rather than
as sets of types (`arch.Project(name)` is the unrelated *type* noun, and both keep their meanings):
project adjectives `.Named(name, …)` (exact, ordinal) / `.Matching(glob, …)` (`*` over one token — a
project name carries no dot-segment structure) · `.Packable()` (the projects an evaluation reported as
producing a package) · `.Except(projects)` · `.Where(pred, description:)` · project verbs
`MustOnlyTarget(tfm, …)` (the allow-list of short monikers — `netstandard2.0`, `net48`; a classic
project's `v4.8` reads `net48` here — and strict, with no external exemption, because a project's
frameworks are a closed set) · `MustReferenceNoPackages()` (zero-arity, because the empty list *is* the
law: one violation per `PackageReference` the project declares, and declared references only — packages
arriving transitively through a project reference are not seen) · `MustLockPackages()`
(`RestorePackagesWithLockFile`) · `MustNotBePackable()` (the SDK defaults `IsPackable` on, so this is
what makes an internal project's opt-out checkable rather than assumed) · `.Must(pred, description:)`
(project predicates see `IProjectInfo`: name, target frameworks, package and project references, and the
two flags). Every fact here is read from the build's own evaluation, never from the project file's XML —
the property a rule is about is very often set in a `Directory.Build.props` above the project — so a
violation points at whichever declaration actually won, and a fact nothing evaluated is unknown and
always passes. The flagship:
`arch.Projects.Matching("MyApp.*").Except(arch.Projects.Named("MyApp.Cli")).MustNotBePackable()` —
*"Projects matching `MyApp.*`, except project `MyApp.Cli` must not be packable."*

**Postures** — `arch.Rule(id).Enforce(constraint)` · `arch.Rule(id).Migrate(from:, to:)`
[`.Baseline(path)`] [`.WhileYoureThere(MigrationPolicy.MigrateIfSmall | AlwaysMigrate |
NeverExpand)`] · `arch.Scope(id).Quarantine(selection)` [`.BoundaryOnlyVia(types...)`]
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
source-level member access; "construct" means a source-level object creation (`new`, including
target-typed `new()`); "inject" means a source-level constructor-parameter dependency (primary
constructors included); "catch" means a source-level `catch` clause (a bare `catch` counts as
`System.Exception`; whether the clause spells a `when` filter, and whether its block ends in a
`throw`, are recorded beside it, so a ban can reach the unfiltered ones — or the swallowing
ones — alone); "throw" means a source-level `throw` of the thrown
expression's static type (bare rethrows `throw;` are not recorded); "expose" means a type named in a public signature position (a public member's return, parameter, or property/field/event type) of an externally visible type: the checker records all seven edge kinds. A construction ban keys the
(source, constructed) type pair (overload-indifferent) and is honest about reflection — a DI
*registration* mints only a type reference, never a construct edge, so a container-resolved type is
not caught; a factory lambda that genuinely `new`s the type IS caught, so `.Except` the sanctioned
composition root or baseline the edge. `Registered` membership comes only from source-visible
registration calls (`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/
`AddDbContext`/`AddHttpClient<TClient>`) — assembly scanning, factory-lambda internals, and
framework defaults are invisible; an empty `Registered` subject fails loud (check the visibility
boundary before blaming the code), while an empty `MustNotInject` operand is the win condition
and never warns. Member bans are
source-visibility bans, not runtime-dispatch bans — a ban on a concrete member does not catch
calls through an interface-typed receiver, nor the reverse. Member subjects range over the
declared members of solution-declared types (accessors, constructors, operators, indexers,
and compiler-generated members are excluded; external types carry no member inventory), and
an empty member subject fails the rule exactly like an empty type subject. Type subjects
range over solution-declared types; targets also reach external (BCL/NuGet) types. `MustOnlyReference` constrains solution-declared targets only (external
packages are exempt, and the rendered sentence says so) and allows the subject implicitly, after
any `Except` it spells — list what a layer may reach BEYOND itself, and reach for
`MustOnlyReferenceItself()` where that list is empty. `MustOnlyBeReferencedBy` reads its subject
the same way. `MustOnlyThrow` is stricter
still: external thrown types ARE constrained (no type must throw a BCL exception), so its
sentence carries no exemption; `MustNotThrow` is its ban twin, and a spec may carry either or
both. All five exception verbs match their operands exactly — banning `Exception` never flags a
narrower catch or a derived throw, which is the good state — and the three catch verbs key the
same (source, caught) edge, so a baseline entry means the same thing under any of them.
`Implementing`/`DerivedFrom`
are transitive with type-argument substitution; an open generic (`typeof(IHandler<>)`)
matches any construction. `AttributedWith` sees declared attributes only. The `MustNot*`
hierarchy verbs share these three matchers, negated per subject over the anchor list (a
subject reds iff it matches ANY anchor). Hierarchy
adjectives never match external types (their hierarchy is not extracted). Closed generics are
refused in reference positions — ban the open definition and/or the argument type. An empty
subject fails the rule; an inert target warns; both are authoring signals, not code evidence.

## Where to go deeper

- `arch_explain <rule-id>` (CLI: `loadbearing explain`) — any rule's because / fix / posture
  payload, including desugared `{scope-id}/containment` and `{scope-id}/tripwire` children.
- `arch_context <path>` — the architecture scope cards covering a directory (a quarantined
  scope's dragons, a layer's local rules).
- `loadbearing status` — the burndown after baselining.
- [GRAMMAR.md](https://github.com/andypgray/loadbearing/blob/main/GRAMMAR.md) — the canonical
  fluent-language spec: the complete vocabulary with its rendered prose fragments and pinned
  semantics. The authoring reference above is its condensed subset; when a rule needs a form
  not shown here, look there before improvising.
- The generated `AGENTS.md` block is the always-on summary; this recipe's output is what
  keeps it true.
