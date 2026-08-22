# LoadBearing — the spec language (grammar)

## 0. Authority and how to read this document

This document is the **canonical specification of the fluent surface** — the "language" spec
authors write. It pins what a spec sentence may say, what it reifies to, and what prose it
renders. Derived from a survey of the prior art (ArchUnit, ArchUnitNET, NetArchTest,
FluentValidation, EF Core ModelBuilder, .NET fluent-idiom survey) plus an adversarial review
pass; the four dialect decisions in §1 are ratified.

The pinned tests are the enforcement of this document: **the fluent surface may only change
together with this document and its pinned tests.** When a pin must move, move it deliberately
as part of the change (house test culture).

## 1. Ratified dialect decisions

1. **Modal `Must*` constraint verbs** with lexical polarity — not ArchUnit-literal
   `That()/Should()`.
2. **Split escape hatches**: `.Where(pred, description:)` in selector position, `.Must(pred,
   description:)` in constraint position; the mandatory-description semantics are locked.
3. **`Because` is required** (spec-build error when missing); `Fix` is optional.
4. **Posture granularity: dual surface, single model** —
   `Rule().Enforce/Migrate(...)` and `Scope().Quarantine(...)` on the surface; Quarantine desugars to
   ordinary posture-bearing rule nodes (§7); checker/renderer/baseline walk ONE rule model.

## 2. Design principles

1. **Rules are data.** The fluent API builds a walkable model; nothing on a rule executes.
   There is no `Check()`/`GetResult()` terminal anywhere in the grammar.
2. **Every vocabulary member renders.** Each combinator carries a deterministic prose
   fragment, pinned by string-equality tests. The admission rule (§10) forbids members that
   cannot self-describe.
3. **Polarity is lexical.** Negation lives in the verb name (`MustNotReference`); there is no
   `Not()` combinator, no `ShouldNot()` gate, no `noTypes()` prefix. Double negation is
   inexpressible by construction.
4. **Fragments compose without conjugation.** Modal "must" is number-invariant in English, so
   singular subjects ("the Domain layer") and plural subjects ("types implementing X") take
   the same verb fragments. Adjectives are participial/prepositional phrases that append as
   reduced relative clauses.
5. **Selections and constraints are first-class values** — assignable to variables, returnable
   from helpers, buildable in loops. The grammar never requires a chain to complete in one
   expression.
6. **Compile-enforce the big structure; validate the fine rules.** Stage types make illegal
   sentence *shapes* uncompilable; spec-build validation reports every remaining error at
   once (§8) so an agent fixing a spec sees all problems in one pass.
7. **Honesty is pinned.** Where a verb's semantics have a boundary (the `MustOnly*` reference
   universe, §4.1), the rendered fragment states the boundary. Escape hatches without
   descriptions do not compile.

## 3. The grammar

### 3.1 Statement forms

A spec is a class implementing `IArchitectureSpec` with one method, `Define(Arch arch)`.
Inside it there are exactly three statement forms:

```
definition :=  var x = arch.Layer(name, glob, globs...) | arch.Namespace(glob)
             | arch.Project(name) | arch.Type(typeof(X)) | arch.Type<X>()
             | arch.Registered(lifetime) | arch.Registered()
             | arch.AnyOf(selection, selections...) | arch.AnyOf(typeof(X), types...)
             | arch.Member(typeof(X), nameof(X.M))
             | arch.Member<T>(x => x.M) | arch.Member(() => X.M)
rule       :=  arch.Rule(id) . posture-verb . trailer*
scope      :=  arch.Scope(id) . Quarantine(selection) . quarantine-clause* . trailer*

posture-verb :=  Enforce(constraint)
              |  Migrate(from: prose, to: constraint) [.Baseline(path)] [.WhileYoureThere(policy)]
constraint   :=  selection . modal-verb(target...)          — a complete, reified sentence
             |   member-selection . member-modal-verb(...)  — a member-shape sentence (§4.6)
selection    :=  noun [. adjective]*                        — an immutable, reusable value
member-selection := selection . projection [. member-adjective]*   — a member subject (§4.6)
projection   :=  Members | Methods | Properties | Fields | Events
member       :=  arch.Member(typeof(X), nameof(X.M))        — a leaf value, NOT a selection (§4.5)
             |   arch.Member<T>(x => x.M)                   — a typed instance-member anchor (§4.5)
             |   arch.Member(() => X.M)                     — a static-member anchor (§4.5)
quarantine-clause :=  BoundaryOnlyVia(types...) | Dragons(prose) | DragonsDoc(path) | Baseline(path)
trailer      :=  Because(prose) | Fix(prose)
```

Modal-verb targets are selections for the dependency verbs (§3.3) and members for the
member-access verb `MustNotUse` (§4.5). The member-modal verbs (§4.6) take no target — they
are shape/naming assertions over the projected member set, so `member-selection` is itself the
whole subject side of a member-shape sentence. The one exception is the methods-only
`.MustAcceptParameter(Type)` (§5.7), whose `Type` anchor renders on the verb side of the
sentence.

### 3.2 Stage machine

```
Arch
 ├─ .Layer(name, string glob, params string[] more) → Layer (: Selection)
 ├─ .Namespace(glob)  → Selection
 ├─ .Project(name)    → Selection
 ├─ .Type(Type)       → Selection      (single type; there is deliberately no
 │                                      arch.Types(params Type[]) — it cannot coexist with
 │                                      the arch.Types property (CS0102); the multi-type noun
 │                                      is .AnyOf(Type, params Type[]) below)
 ├─ .Type<T>()        → Selection      (generic sugar: ≡ .Type(typeof(T)); §5.2 note)
 ├─ .AnyOf(Selection first, params Selection[] more) → Selection
 │                                     (the union of its operands; §5.1)
 ├─ .AnyOf(Type first, params Type[] more) → Selection
 │                                     (sugar: ≡ .AnyOf(.Type(a), .Type(b), …) — the
 │                                      multi-type noun)
 ├─ .Registered(Lifetime) → Selection  (types named in a source-visible container
 │                                      registration with that lifetime — service and
 │                                      implementation alike; §4.7)
 ├─ .Registered()     → Selection      (same, any lifetime; §4.7)
 ├─ .Member(Type, string) → Member     (member-access leaf, target-only — NOT a Selection:
 │                                      adjectives and modal verbs must not apply; §4.5)
 ├─ .Member<T>(Expression<Func<T, object?>>)  → Member  (typed instance value-member anchor; §4.5)
 ├─ .Member<T>(Expression<Action<T>>)         → Member  (typed instance void-method anchor; §4.5)
 ├─ .Member(Expression<Func<object?>>)        → Member  (static value-member anchor; §4.5)
 ├─ .Member(Expression<Action>)               → Member  (static void-method anchor; §4.5)
 ├─ .Types            → Selection      (all solution-declared types)
 ├─ .Rule(id)         → IRuleBuilder   (registers the node immediately)
 └─ .Scope(id)        → IScopeBuilder  (registers the node immediately)

Selection    — adjectives → Selection; modal verbs → Constraint (terminal);
               projections (.Members / .Methods / .Properties / .Fields / .Events)
               → MemberSelection (§4.6)
MemberSelection — member adjectives (.WithSuffix / .WithPrefix / .WithNameMatching /
               .AttributedWith / .Where) → the SAME concrete member-selection type;
               member modal verbs → Constraint (terminal)
MethodSelection — a MemberSelection minted by .Methods that additionally offers
               .Returning(Type first, params Type[] more) → MethodSelection (§4.6)
               and .MustAcceptParameter(Type) → Constraint (terminal; §5.7)
IRuleBuilder — ONLY .Enforce(Constraint) → IEnforceRule | .Migrate(from:, to:) → IMigrateRule
IEnforceRule — .Because / .Fix
IMigrateRule — .Because / .Fix / .Baseline(path) / .WhileYoureThere(MigrationPolicy)
IScopeBuilder — ONLY .Quarantine(Selection) → IQuarantinedScope
IQuarantinedScope — .BoundaryOnlyVia(params Type[]) / .Dragons(prose) / .DragonsDoc(path)
               / .Baseline(path) / .Because
```

Structural consequences, all deliberate:

- A rule cannot exist without an ID (it is the anchor argument) or without a posture (the
  only methods on `IRuleBuilder` are posture verbs). Rule anatomy is enforced by the grammar.
- Trailers exist only on posture stage types — `arch.Rule(id).Because(...)` does not compile.
- Register-on-anchor: a dangling `arch.Rule("x");` is caught by validation (§8 item 2), not
  silently dropped.
- **`Selection` and `Constraint` are closed class hierarchies** — abstract classes with
  `private protected` constructors. Foreign nodes cannot enter the model, so every node is
  walkable and renderable by construction. Stage interfaces stay thin; vocabulary ships as
  extension methods where growth is expected (the FluentValidation shape — evolvable and
  netstandard2.0-safe).
- Fresh-instance contract: one model build = one fresh spec instance + one fresh `Arch`.
  Reusing a `Selection` minted on another `Arch` is a validation error (§8 item 10).
- **`Member` is a leaf, not a `Selection`.** It never enters the selection hierarchy, so
  adjectives and modal verbs are uncompilable on it by construction — a member names a thing
  to ban, not a set of types to constrain. It is Owner-stamped like a selection, and the
  fresh-instance contract covers it (§8 item 13).
- **`MemberSelection` is its own closed hierarchy, disjoint from `Selection`.** A projection
  turns a type selection into a member selection; the two hierarchies share no base, so the
  member adjectives/verbs and the type adjectives/verbs never collide on overload resolution
  (a `.WithSuffix` call binds to the type-side or member-side vocabulary purely by receiver
  type). Member adjectives are generic self-type extensions, so a chain preserves its concrete
  type: `.Methods.Returning(...).WithSuffix(...)` is still a `MethodSelection`, and
  `.Returning` stays reachable in any adjective order. Because `.Returning` and
  `MustAcceptParameter` live only on `MethodSelection` (the `.Methods` projection's type),
  calling either off `.Properties` / `.Fields` / `.Events` / `.Members` is uncompilable by
  construction — a structural consequence, not a validated one.

### 3.3 Dependency-verb overloads (pinned)

```csharp
Constraint MustNotReference(Selection first, params Selection[] more)
Constraint MustNotReference(Type first, params Type[] more)      // sugar for arch.Type(...)
```

Same shape for `MustOnlyReference`, `MustNotBeReferencedBy`, `MustOnlyBeReferencedBy`, the
constructor-ban verb `MustNotConstruct`, the injection-ban verb `MustNotInject`, the
exception verbs `MustNotCatch`, `MustNotCatchUnfiltered`, `MustNotSwallow`, `MustNotThrow` and
`MustOnlyThrow`, and the signature-exposure verb `MustNotExpose`: each carries the identical
pair — a `Selection`
list and the `Type` sugar — and deliberately **no expression overload**. Constructor-ness lives in
the verb, not the anchor, so ordinary selections name what may not be `new`ed
(`arch.Types.Implementing(typeof(IHandler<>))`), which scales to "all registered services" where a
per-constructor anchor would be dummy-argument noise. Injection-ness lives in the verb the same
way, and its natural operands are the registration-fact selections
(`arch.Registered(Lifetime.Scoped)`, §4.7), though any selection or bare type works.
Catch-ness and throw-ness live in the verb the same way again: the operands name exception
types, most often through the bare `typeof` sugar (`MustNotCatch(typeof(Exception))`); their
edge semantics are §4.8. The `when`-filter condition on `MustNotCatchUnfiltered` rides the verb
for the same reason and a sharper one — a filter is not a type, so no operand could name it.
The same holds for the rethrow condition on `MustNotSwallow`: a terminal `throw` is not a type
either, and the verb reads both conditions together.
Exposure-ness lives in the verb the same way: the operands name the types that may not surface
in a public signature position, most often through the bare `typeof` sugar
(`MustNotExpose(typeof(DataTable))`); its edge semantics are §4.9. The
`(first, more)` shape makes zero-argument calls **uncompilable** and keeps single-argument
overload resolution unambiguous. Mixing selections and types in one call = wrap the type:
`MustNotReference(web, arch.Type(typeof(SqlConnection)))`. The same `(first, more)` shape
applies to `Layer(name, glob, more)` — a layer with zero globs is uncompilable.

The member-access verb (§4.5) carries the same shape over `Member` targets — a zero-member
call is uncompilable:

```csharp
Constraint MustNotUse(Member first, params Member[] more)
```

and, as pure authoring sugar for the static anchor forms, the same shape over static-member
lambdas (each desugars at mint through `arch.Member(() => …)` to the identical leaf):

```csharp
Constraint MustNotUse(Expression<Func<object?>> first, params Expression<Func<object?>>[] more)  // () => Type.M  /  () => Type.M()
Constraint MustNotUse(Expression<Action>      first, params Expression<Action>[] more)           // () => Type.M()  (void)
```

Member targets are `arch.Member`-anchored — `typeof` + `nameof`, or the expression forms
`arch.Member<T>(x => x.M)` / `arch.Member(() => X.M)` (§4.5) — so a
banned member participates in compilation and refactoring like every other spec reference; the
expression forms additionally let the compiler check the type↔member pairing that `typeof` +
`nameof` leaves unverified. There is deliberately no string-FQN member overload: string-FQN
*member* anchoring stays named growth (§11). Attribute anchors are where the string form has
shipped (§5.2–§5.3, §5.7), and they set the anchoring policy any later string form inherits:
`typeof` whenever the spec project can compile against the anchored type — a `typeof`
participates in compilation and refactoring, and nothing checks a string — and the string only
when it cannot.

The dependency verbs deliberately grow **no** generic twin (decided against): the
pinned `(first, params more)` shape has no generic form (a variadic type-argument list is
inexpressible), and mixing a bare type into a selection list is already served by wrapping it —
`MustNotReference(web, arch.Type<SqlConnection>())`. The generic sugar lands only where a single
type is the whole argument: `arch.Type<X>()` and the `Implementing` / `DerivedFrom` /
`AttributedWith` hierarchy adjectives and their `Must*` twins (§5.2, §5.3).

The member-access verb grows **no instance-form verb twin and no cross-form mixing** (decided
against): a `MustNotUse<T>(x => x.M)` twin would drag the type argument onto the
verb, and one call cannot name two declaring types — instance members stay
`arch.Member<T>(x => x.M)`. A static value and a static void target are disjoint delegate shapes
(`Func<object?>` vs `Action`) that no single overload spans, so one call mixes neither the two
static forms nor a static with an instance — wrap the odd target with `arch.Member` and pass the
uniform `Member` list, exactly as a mixed selection/type list wraps the type (above). The sugar
lands only where the whole list is static and one form:
`web.MustNotUse(() => DateTime.Now, () => DateTime.UtcNow)`.

## 4. Pinned semantics

### 4.1 Reference universe (per position)

- **Subjects** range over **solution-declared types** — the set extraction walks. That walk applies
  a **type-side screen**, the twin of the member inventory's (§4.6): out go the implicitly declared,
  the types no C# source can name, and anything outside the v1 `TypeKind` set. Generator output
  stays in. A generated partial is explicitly declared and its name is writable, so
  `arch.Project("MyApp.Cli")` names the regex types a `[GeneratedRegex]` method puts in that
  assembly, and a top-level-statements `Program` is a subject like any other (its name is writable
  and it carries real reference, catch and throw edges). **A project noun names what the assembly
  declares, generated types included** — a pinned boundary, not an oversight. Screening them here
  would also strip them from target position and from the codebase graph, and they would come back
  as *external* nodes the moment anything referenced them. `.Authored()` (§5.2) is the opt-in
  narrowing for a law a generator's output cannot be expected to satisfy. Because the boundary is
  silent by construction, both surfaces state it: the codebase survey counts generated types per
  project and per namespace, and `check` reports how many of a rule's subject were generated
  whenever any of it was.
- **Targets** range over **all referenced types, including metadata references** —
  `MustNotReference(typeof(SqlConnection))` and `arch.Namespace("System.Web.*")` as a target
  both work against BCL/NuGet types.
- **One fully-qualified name can mean two types.** A project declares a name that a referenced
  assembly also supplies: a stand-in written under a package's own namespace, a polyfill under
  a BCL one. The model carries both — the source declaration, and a shallow external attributed
  to the supplying assembly — and every reference resolves to whichever the referencing
  compilation actually bound. So a project that references the package reaches the package's
  type and never the declaring project's, on the edge axes and in the hierarchy alike. A rule
  naming the type reaches both; `arch.Project` is what tells them apart, because the external
  one carries the supplying **assembly**'s name, so a project selection over the declaring
  project reaches the declaration alone. Nothing else splits: one source file compiled into
  several projects still means one node (the next bullet states its membership), and a name
  no project declares is one node as before. `check` reports the split
  as an advisory note, and the codebase survey states it as a coverage key.
- **One source file can be compiled into several projects.** A linked `<Compile Include>`,
  shared source, a polyfill: N projects each compile the file as their own source, and the
  model still carries **one node** for the type. Its facts — edges, member inventory,
  hierarchy — follow the first declarer in ordinal project order. Membership is N-way:
  `arch.Project` naming **any** declarer contains the type — as a subject, as a target
  operand, in a member selection, inside a union. Edges add attribution on top. The projects
  an edge *into* such a type counts against are the intersection of the two endpoints'
  declarer sets when it is nonempty — the source compiled its own copy, so the edge is
  intra-project — and the first declarer alone otherwise. A project-headed target operand
  forbids (or, for `MustOnly*`, allows) the edge only when its project is among those, so a
  declarer's reference to its own compiled-in copy never satisfies a ban aimed at another
  declarer; a project-headed *subject* of `MustNotBeReferencedBy` / `MustOnlyBeReferencedBy`
  counts an edge by the same rule, because there the subject is the edge's target. The edge's
  *source* belongs to every declarer — each one's compilation genuinely makes the reference —
  so a source-side project operand reaches it from any of them. Selections not headed by a
  project stay attribution-insensitive: `typeof`, `arch.Namespace`, `arch.Types`,
  `arch.Layer`, `arch.Registered`, and `MustNotUse`'s string-keyed member anchors, so
  `MustNotReference(typeof(T))` reds even on a project's own copy. `MustOnly*` stays strict
  (no implicit self-allowance, below): an intra-copy edge is satisfied only by an allow entry
  naming the compiling project, or by a non-project entry containing the type. `Except`
  subtracts by node identity: `arch.Project("A").Except(arch.Project("B"))` removes a
  co-declared node from A's selection too. Where nothing is multiply declared this all
  degenerates to the single-attribution reading, byte for byte. The attribution is disclosed
  three ways: `check`'s advisory merge note, the survey's `multiplyDeclaredTypes` coverage
  statement, and the winning node's `AlsoDeclaredBy` roster.
- **One project file can mean several compilations.** A multi-targeting project compiles once
  per framework; extraction walks them all, and the model unions them under the one project
  name. The union is lossless — types, references and solution membership all survive — except
  for the facts of a type more than one framework declares: its edges, member inventory and
  hierarchy come from the first framework extracted, and extraction consumes frameworks in
  ordinal order of the framework name, never the csproj's declaration order. So on
  `net48;net6.0;net8.0;netstandard2.0` every shared type's facts come from `net48`, and anything
  another framework's `#if` guards is not in the model at all: a rule about such a type is
  checked against the winning framework's compilation alone. A framework-exclusive type (one
  only a single framework declares) keeps that framework's facts. The codebase survey lists
  each multi-targeted project's frameworks in extraction order and names the one its shared
  types follow, and `check` stamps the same pair per project beside an advisory note wherever
  two frameworks actually collapsed a type.
- **`MustOnly*` complement universe = solution-declared types — for the *reference* verbs.**
  BCL/NuGet references are
  exempt, and the fragment states it: *"must reference only {list} (external packages are not
  constrained by this rule)"*. Without this pin every `MustOnlyReference` rule is either 100%
  violated (`System.String`) or renders a false sentence — both fatal to "the prose is
  provably true". `MustOnlyThrow` diverges deliberately: no type must *throw* a BCL
  exception, so the exemption's rationale never applies — the verb is strict and its
  fragment carries no caveat (§4.8).
- **`MustOnly*` is strict — no implicit self-allowance.** A subject's reference to another
  member of its own selection is a violation unless that selection is itself among the allowed
  targets; authors list their own layer when they mean it. Internal precedent: the Quarantine
  `{id}/containment` desugaring explicitly lists the quarantined selection in its own allowed set
  (§7). (Self-edges never arise — extraction drops them.)
- `MustOnlyBeReferencedBy` needs no caveat: only solution types can be observed referencing.
- Checker behavior: an empty *subject* selection **fails** the rule by default
  (ArchUnit and ArchUnitNET precedent, with a pinned message). For a **union** subject the
  default sharpens per operand: every operand must match at least one type, and each empty one
  fails the rule in its own right with the operand named, so a typo'd operand is never masked by
  its siblings (§5.1, §9). A run a solution filter (§4.4) narrowed skips instead: a rule *all* of
  whose violations are these empty-selection failures reports **skipped**, one line naming the
  filter and the unchecked-project count, because under a narrowing filter an empty selection is
  the expected consequence of the narrowing rather than evidence of a typo'd spec. The cost is
  deliberate: a genuinely typo'd selection goes quiet under a filter until an unfiltered run reds
  it. Unfiltered runs keep the fail-closed default untouched. An empty resolved *operand* set
  warns **"rule is inert"** only on a forbidden-set dependency verb (`MustNotReference` /
  `MustNotBeReferencedBy` / `MustNotConstruct` / `MustNotCatch` / `MustNotCatchUnfiltered` /
  `MustNotSwallow` / `MustNotThrow` / `MustNotExpose`) whose operand is a
  **pattern selection**
  (Layer / Namespace / Project
  or a refined `Types`); a bare `typeof(...)` target absent from the codebase is the *win
  condition* and stays silent, the `MustOnly*` verbs never warn (an empty allow-set is loud
  on its own), and `MustNotUse` / `MustNotInject` never warn at all (§4.5, §4.7).
- **Edges are definition-level (v1).** A source-level reference to `IHandler<Order>` is a
  reference to *both* the open definition `IHandler<T>` and the argument `Order`; the checker
  mints no edge to the closed construction. A closed generic in an `arch.Type(...)` /
  dependency-verb position (a reference *target* or *subject*) therefore has no node to resolve
  to and is refused as a rule-level error with guidance ("ban the open definition and/or the
  argument type"). The hierarchy adjectives (§5.2), which match against *preserved*
  constructions, are where a closed generic is meaningful.
- Rendered glossary line, once per managed block: *"reference = a source-level type
  reference."* A collective layer sentence is exactly the universally-quantified types
  sentence at v1 edge granularity.

### 4.2 Namespace patterns

Not `Microsoft.Extensions.FileSystemGlobbing` — that library is path-segment based and stays
for file paths (`DragonsDoc`, scope→directory mapping). Namespace patterns are dot-segment
aware and case-sensitive:

- **Trailing `.*` is the subtree operator and is self-inclusive**: `MyApp.Domain.*` matches
  the namespace `MyApp.Domain` itself and all descendants. (Otherwise a facade declared
  directly in `MyApp.Legacy.Billing` falls outside its own quarantine — the silent-hole bug.)
- An interior standalone `*` segment matches exactly one segment.
- A partial-segment `*` (e.g. `Legacy*`) matches within the segment and never crosses a dot.
- A lone `*` matches everything.

Edge cases, pinned by tests:

| Pattern | Matches | Does not match |
|---|---|---|
| `MyApp.Domain.*` | `MyApp.Domain`, `MyApp.Domain.Orders`, `MyApp.Domain.Orders.Internal` | `MyApp.DomainX`, `MyApp` |
| `MyApp.Domain` | `MyApp.Domain` only | `MyApp.Domain.Orders` |
| `MyApp.*.Orders` | `MyApp.Sales.Orders` | `MyApp.Orders`, `MyApp.A.B.Orders` |
| `MyApp.Legacy*` | `MyApp.Legacy`, `MyApp.LegacyBilling` | `MyApp.Legacy.Billing` |
| `*` | everything | — |

**Type- and member-name globs** (`WithSuffix`/`WithPrefix`/`WithNameMatching`, their member
twins, and the naming verbs — §5.2/§5.3/§5.7) share one matcher, distinct from namespace
patterns and pinned by its own edge-case tests: matching is case-sensitive ordinal; `*`
matches any run of characters including the empty run (`*Async` matches `Async`); a pattern
with no `*` is an exact name match; a lone `*` matches every name. Name globs have no
dot-segment structure and no subtree operator, so §8 item 16 never applies to them — only
the blank check (item 15) does.

### 4.3 Violation identity (what a baseline entry keys)

Per verb class — this is grammar-level semantics, not baseline file format:

- **Dependency verbs**: `(ruleId, source symbol ID, target symbol ID)`. A second forbidden
  reference from a grandfathered type — or the same type referencing a *different* forbidden
  target — is NEW and red, preserving the ratchet ("new code in the old pattern must be
  red"). Multiple reference sites within one (source, target) pair ride together; this is
  documented behavior.
- **Construction verb** (`MustNotConstruct`, §4.5/§5.3): `(ruleId, source symbol ID,
  constructed symbol ID)` — the same edge-key shape as a dependency reference (the constructed
  type keys the target slot), so a construction entry rides `BaselineEntry.ForEdge` with **zero
  baseline-format change**. Overload-indifferent: every constructor overload of the constructed
  type collapses to the one type-pair identity, and the `new` sites are evidence, not identity.
  A grandfathered `new Foo()` plus a *different* forbidden constructed target from the same
  source is NEW and red, exactly like the reference ratchet; multiple `new` sites within one
  (source, constructed) pair ride together.
- **Injection verb** (`MustNotInject`, §4.7/§5.3): `(ruleId, source symbol ID, injected
  symbol ID)` — the same edge-key shape again (the injected parameter type keys the target
  slot), so an injection entry rides `BaselineEntry.ForEdge` with **zero baseline-format
  change**. Constructor-overload- and parameter-name-indifferent: every constructor parameter
  typed on the injected type collapses to the one type-pair identity, and the parameter sites
  are evidence, not identity. A grandfathered captive injection plus a *different* forbidden
  injected target from the same source is NEW and red; multiple injecting parameters within
  one (source, injected) pair ride together.
- **Catch verbs** (`MustNotCatch`, `MustNotCatchUnfiltered` and `MustNotSwallow`, §4.8/§5.3):
  `(ruleId, source symbol ID, caught symbol ID)` — the same edge-key shape once more (the caught
  exception type
  keys the target slot), riding `BaselineEntry.ForEdge` with **zero baseline-format change**.
  Catch sites are evidence, not identity: multiple `catch` clauses within one (source, caught)
  pair ride together, and a grandfathered catch plus a *different* forbidden caught type from
  the same source is NEW and red. **All three catch verbs key that same edge.** The *unfiltered*
  sites `MustNotCatchUnfiltered` reports and the *swallowing* sites `MustNotSwallow` reports are
  evidence too — narrowing which sites a violation
  prints never narrows what it keys — so an entry written under one catch verb keys the
  identical edge under the others, and adding a `when` filter or a terminal `throw` to one clause
  of a grandfathered pair moves the evidence, not the baseline.
- **Throw verbs** (`MustOnlyThrow` and `MustNotThrow`, §4.8/§5.3): `(ruleId, source symbol ID,
  thrown symbol ID)` — the same shape (the thrown type keys the target slot), riding
  `BaselineEntry.ForEdge` with **zero format change**. Throw sites are evidence, not
  identity; a grandfathered thrown type plus a *different* thrown type the rule objects to,
  from the same source, is NEW and red. The allow-list and the ban key edges identically, so
  which polarity a spec chose is invisible to its baselines.
- **Exposure verb** (`MustNotExpose`, §4.9/§5.3): `(ruleId, source symbol ID, exposed symbol
  ID)` — the same edge-key shape once more (the exposed type keys the target slot), riding
  `BaselineEntry.ForEdge` with **zero baseline-format change**. Signature positions are
  evidence, not identity: every signature position of one exposed type within a source rides
  together, and a grandfathered exposure plus a *different* forbidden exposed type from the
  same source is NEW and red.
- **Shape/naming/inheritance/attribute verbs and escape hatches**:
  `(ruleId, subject symbol ID)`.
- Symbol IDs are Roslyn `DocumentationCommentId` strings — stable across file moves and
  formatting.
- A symbol ID names a name, not a node. Where a fully-qualified name means several types — see the
  reference universe above — they share an ID, so a baseline entry written for either grandfathers
  the other. That is deliberate: an ID that forked on which assembly supplied the name would not
  survive the package upgrade it exists to survive. It is also a widening, so prefer fixing such a
  pair to baselining it; the survey's shadowed-name key is where the split shows before a baseline
  is written.

### 4.4 Migrate defaults

- `.Baseline(path)` omitted ⇒ a deterministic conventional path derived from the rule ID:
  `arch/baselines/<rule-id>.json`,
  where the rule ID's `/` separators become subdirectories (IDs match §8's
  `^[a-z0-9-]+(/[a-z0-9-]+)*$`, so the result is filesystem-safe). The path is filled into the model
  at build time — `MigrateData.BaselinePath` is never null post-build — stored forward-slash, and
  resolved by the CLI against the solution directory (an absolute spec path wins). A run through a
  solution filter (`.slnf`) resolves against the directory of the solution the filter references,
  so the filter and its solution find the same committed file.
- `.WhileYoureThere(policy)` omitted ⇒ `MigrationPolicy.MigrateIfSmall`, and the default
  renders in the counter-prior prose (the boy-scout sentence always has content).

### 4.5 Member-use edges (member-access targets)

The first rules a long-lived .NET estate asks for are member-level bans — `DateTime.Now`,
`.Result`/`.Wait()`, `ConfigurationManager.AppSettings`, `HttpContext.Current` — all invisible
to type-level edges (`DateTime` the *type* cannot be banned). `arch.Member` + `MustNotUse`
ship exactly that half: members as constraint **targets**. Member *subjects* stay §11 growth.

- **A member-use edge** is `(source type, target member, file:line sites)`, recorded beside
  the type-level edge wherever the walk binds a member: property/field/event accesses
  (including `?.`, compound assignment, and `+=`/`-=` subscription), method invocations,
  method-group references, and `using static` bare names.
- **Not recorded, pinned**: `nameof` operands — a deliberate asymmetry with type edges
  (`nameof(Target)` DOES mint a type edge; `nameof(DateTime.Now)` never reads the clock, so
  it is not a *use*); compiler-pattern consumption the syntax walk never sees as a member
  access (`await`'s `GetAwaiter`, `using`'s `Dispose`, `foreach`'s enumerator pattern, query
  syntax) — the documented syntax-walk boundary; indexers (growth, §11). Constructors are not a
  member *use* either, but they ARE recorded as their own **construction edge** (below), a
  third axis beside reference and use.
- **A construction edge** is `(source type, constructed type,
  file:line sites)`, recorded beside the type-level edge at every object-creation expression —
  explicit `new Foo()` **and** target-typed `new()` (the modern-codebase form, pinned early).
  Constructed generics normalize to their open definition (§4.1); self-construction is dropped like
  the type-edge self-reference. **Excluded, each with a pinned must-NOT-mint row**: attribute
  applications, `: base(…)`/`: this(…)` initializers, delegate creation (`new Action(M)` and its
  target-typed form, keyed on the constructed symbol's delegate kind so both spellings skip), `with`
  expressions, and array creation. Reflection/container construction is the documented **honesty
  boundary**, invisible either way — a container *registration* (`AddScoped<IFoo, Foo>()`) mints only
  type references, so pure-DI composition roots need no exemption, while a factory lambda that
  genuinely `new`s (`AddScoped(sp => new Foo(…))`) IS caught and wants `.Except(root)` or a baseline:
  the honest, teachable shape. The verb (`MustNotConstruct`) is §5.3; the glossary clause is §10.
- **Normalizations**: an accessor resolves to its property/event; a reduced extension-method
  call resolves through `ReducedFrom` to the declaring static class's method; constructed
  generic members resolve to their `OriginalDefinition` — member edges are definition-level
  exactly like type edges (§4.1); same-type accesses are dropped (matches the type-edge
  self-drop).
- **Matching** is by `(declaring type, member name)` — one ban covers every overload; there is
  no signature form. **Violation identity** is `(ruleId, source type ID, member
  DocumentationCommentId)` — the *specific* member's ID (`M:`/`P:`/`F:`/`E:` forms), so a
  grandfathered `Wait()` rewritten as `Wait(timeout)` is a NEW red (the ratchet blesses the
  observed violation, not the ban).
- **Dispatch boundary, pinned**: an edge records the symbol Roslyn resolves at the call site.
  A ban on a concrete member does not catch calls through an interface-typed receiver, and an
  interface-member ban does not catch direct concrete calls — member bans are
  source-visibility bans, not runtime-dispatch bans.
- **Anchoring** (`typeof` is not the only v1 form; receiver casts are identity-preserving
  only, and extension parity extends to the static form):
  `arch.Member(typeof(X), nameof(X.M))` and, as pure authoring sugar desugaring at mint to the
  identical leaf, the expression anchors `arch.Member<T>(x => x.M)` (instance; value member via
  `Func<T, object?>`, void method via `Action<T>`) and `arch.Member(() => X.M)` (static). The
  anchor is always the tree's **resolved** member: the declaring type from its `MemberInfo`
  (`DeclaringType`, **never** `ReflectedType` — an inherited `Task<int>.Wait()` anchors `Task`),
  with a constructed generic normalized to its definition at mint (`Member<Task<int>>(t => t.Result)`
  → `typeof(Task<>)`; `t.Wait()` → `typeof(Task)`). A closed-generic anchor type is refused at
  check time with the §4.1 guidance (ban the open definition); because expression anchors
  auto-normalize, that refusal — and the §8 item 12 "declared on the anchored type" guard — are
  **unreachable** from an expression-minted member (the member is real and generic-normalized by
  construction), and both remain live for the `typeof` form. An unresolvable anchor expression is
  reported at spec build (§8, one code / eight messages). The receiver rules: the instance form
  wants the lambda parameter itself, reached only through identity-preserving casts (an interface
  or reference `Convert` with no operator method, or an `as`-cast, which anchors the cast-to member
  per the dispatch boundary above); a user-defined conversion instead stops the peel and is
  reported, since following it would silently anchor the post-conversion type. A reduced extension
  call `x.Ext()` in the instance form anchors the declaring static class (the `ReducedFrom` parity
  above); in the static form an extension is anchored like any other static call, so `() => Ext.M(arg)`
  and `() => captured.M()` both name `Ext.M`. What the **compiler** already refuses
  needs no runtime handling: events (CS0070), inaccessible members (CS0122), `ref`-returns
  (CS8153), `ref struct` receivers (CS8640), and statics accessed through an instance (CS0176); a
  method-group body (`x => x.M` or `() => T.M`) warns CS8974 and is steered to the invocation form
  (`x => x.M()` or `() => T.M()`) at spec build.

  The `MustNotUse` verb also carries the two **static** anchor forms directly (§3.3):
  `web.MustNotUse(() => DateTime.Now, () => Type.M())` desugars target-by-target through
  `arch.Member(() => …)` to the identical leaves, so the common all-static ban needs no
  `arch.Member` ceremony. Six of the eight unresolvable-anchor messages are reachable from the
  verb's static forms (the two instance-form steers — a static member in the typed form, a
  receiver that is not the parameter — have no static spelling); they fire from the verb position
  in the same §8 pass. Instance and mixed-form targets keep the explicit `arch.Member` wrap.
- **Inert semantics**: a banned member absent from the codebase is the win condition and
  stays silent. Member targets are concrete `(type, name)` anchors — no pattern form
  exists — so `MustNotUse` never warns, exactly like a bare `typeof` target (§4.1). An
  empty *subject* fails the rule (shared checker default).
- Rendered glossary line, beside the reference entry and **only when the spec carries a
  member-target rule** (a spec without one renders byte-identically to before this section
  existed): *"use = a source-level member access."*

### 4.6 Member subjects (member selections)

Where §4.5 ships members as constraint *targets* (`arch.Member` + `MustNotUse`), this section
ships them as *subjects*: a projection turns a type selection into a **member selection** over
the declared members of the selected types, refinable with member adjectives and asserted on
with member modal verbs. The flagship is `naming/async-suffix` — *"Methods of types in
`MyApp.Web.*` returning `Task` must be named `*Async`."* The named structural decision:
`MemberConstraint : Constraint` carries the `MemberSelection`, but its inherited `Subject` is
the underlying **type** selection (`Subject => MemberSubject.Source`), so every existing walk
that reaches through `Constraint.Subject` — foreign-`Arch` detection (§8 item 10) and Quarantine
desugaring (§7) — keeps working unchanged on the type side.

- **The inventory universe** is the **declared members of solution-declared types** — the same
  subject universe as type selections (§4.1), read one level down. Extraction inventories, per
  declared type, its declared methods, properties, fields, and events. **Excluded** (ratified):
  property/event accessors (they fold into the property/event, matching the §4.5 member-use
  normalization), constructors including static constructors, operators and conversions,
  finalizers, **explicit interface implementations of every kind** — method (`void IFoo.Bar()`),
  property (`int IFoo.P`), and event (`event Action IFoo.E`) alike — indexers, any
  compiler-generated or implicitly-declared member, and **any member with no source-writable
  name** — the member-side twin of the type-side screen, which is what keeps the synthesized
  top-level-statements entry point `<Main>$` out of a naming law's reach while leaving its
  enclosing `Program` type (whose name *is* writable, and which carries real reference, catch and
  throw edges) fully inventoried. An explicit interface implementation is
  interface plumbing, not authored surface: its accessibility is `Private` and its name is fixed
  by the interface, so a naming or shape verb could never sensibly apply. The *method* impl is
  dropped by the non-`Ordinary` `MethodKind` screen, the *property* and *event* impls by their
  non-empty `ExplicitInterfaceImplementations`. A member subject and `MustAcceptParameter` (§5.7)
  therefore never see an explicit implementation of any kind, and none mints an exposure edge
  (§4.9 gates on public — moot now that they carry no inventory). **Enum and
  delegate types contribute no inventory** — an enum's fields are its values (an enum-value read
  stays a recorded field *use*, §4.5, untouched by this section) and a delegate's `Invoke`/
  `BeginInvoke` are runtime plumbing, not authored surface. **External (metadata) types carry no
  inventory** — the member axis is solution-declared-only, exactly as external types carry a
  shallow type hierarchy (§5.2).
- **An empty member subject fails the rule** by default, the member analog of the empty-type
  subject (§4.1), with its own pinned message *"The subject selection matched no
  solution-declared members."* (a selection that matches types none of whose members survive the
  kind filter is the ordinary way to hit it). Under a narrowed run it skips exactly as the type
  analog does (§4.1).
- **Violation identity is `(ruleId, member DocumentationCommentId)`** via `BaselineEntry.ForSubject`
  — the member's own `M:`/`P:`/`F:`/`E:` DocId, so the ratchet blesses the *specific* member and
  a renamed or newly-added member is a NEW red. This reuses the shape-verb identity
  substrate (§4.3, second bullet) with a member ID in the subject slot — **zero baseline
  format/parser changes**.
- **`.Returning(Type first, params Type[] more)`** matches a method's return type at the
  **definition level**, mirroring `Implementing` (§5.2): a **non-generic** anchor (`typeof(Task)`)
  matches exactly; an **open-generic** anchor (`typeof(Task<>)`) matches *any* construction
  (`Task<int>`, `Task<Order>`, …) on the definition name. A **closed-generic** anchor
  (`typeof(Task<int>)`) is refused at spec build (§8 item 14) with guidance to use the open
  definition, and a check-time backstop guards the same class of mistake. There is no
  derived-from / assignability matching — the return type is compared to the anchor's definition
  FQN, nothing wider. `.Returning` is **methods-only**: it lives on the `.Methods` projection's
  `MethodSelection` and is uncompilable elsewhere (§3.2), so a return-type filter on a field or
  property never type-checks. There is deliberately **no `.Returning<T>()` generic twin**
  (decided against): the open-generic anchor is the main use and is inexpressible as
  a type argument, and a closed-generic type argument would only reproduce the §8 item 14 refusal.
- **Parameter facts.** Extraction inventories, per declared *method*, its parameters in
  declaration order — each `(Name, TypeFullName)` with the type definition-normalized exactly
  like the return type (§5.6); properties, fields, and events carry an empty list (accessors,
  constructors, operators, and indexers are already outside the inventory).
  `MustAcceptParameter` (§5.7) evaluates against these facts: a subject method passes iff any
  declared parameter's type matches the anchor's definition FQN — a non-generic anchor
  exactly, an open-generic anchor on any construction, the `.Returning` matching discipline
  verbatim. Declaration semantics, pinned in the extraction matrix: a default-valued
  parameter counts (`CancellationToken cancellationToken = default` — the most common
  compliant signature); the extension-method `this` parameter is included (the declared
  static method's list, never the reduced form); `ref`/`in`/`out` do not change the recorded
  type; `params CancellationToken[]` is the array type and does not match a
  `CancellationToken` anchor; `CancellationToken?` is `Nullable<>`'s definition and does not
  match either — the definition-level-exact honesty boundary `.Returning` and `MustNotCatch`
  already carry, deliberately not widened; a type-parameter-typed parameter records its
  declared name (`T`) — the same rendering that pins `Echo<T>(T value)`'s return type — so it
  never matches a `typeof` anchor; a record's positional list surfaces as the generated
  property, never as parameter facts (the primary constructor is outside the inventory);
  partial-method parameters are read once and ride the partial-union merge.
- **Attribute facts.** Extraction inventories, per declared member, its **declared
  attributes** — each a definition/constructed name pair (`IAttributeInfo`, §5.6), the
  definition open-reduced so a C# 11 generic attribute keeps both its definition
  (`N.MarkAttribute<T>`) and its substituted construction (`N.MarkAttribute<System.Int32>`),
  ordinal-sorted by constructed name. Name pairs, not nodes: unlike the type side the fact
  mints nothing — no reference edge, no graph entry — keeping the member model's
  by-FQN-string discipline. **Declared-only is the boundary**: an attribute on a property's
  `get`/`set` accessor hangs off the accessor method symbol (accessors fold into their
  property and are not inventoried in their own right), and a `[return:]` attribute hangs off
  the return value — both are outside the fact. A **partial method** reports the **union** of
  both parts' attributes, because the merged symbol does — pinned empirically in the
  extraction matrix. The member `AttributedWith` adjective and the member attribute verbs
  (§5.7) match against these facts on the type side's discipline (§5.2): declared attributes
  only, a non-generic or open-definition anchor matching on the definition name, a closed
  `typeof` construction on the constructed name.
- **Declaration-semantics flags are pinned to C#, not IL.** `IsVirtual` is true for a member
  declared `virtual` and false for an `override` or `abstract` one (an override is not itself
  "virtual" in the authored sense); `IsAbstract` is true for an `abstract` member and for every
  interface member (interface members are abstract); `IsAsync` reflects the `async` keyword. These
  carry the same declaration-semantics discipline as the type flags (§5.6) and are pinned in the
  extraction matrix.
- **The violation line** names the member as `{DeclaringType.FullName}.{Name}` (with `()`
  appended iff the member is a method — the shared member-display convention of §6/§4.5), located
  at the member's **declaration** `file:line`.
- **No glossary change.** Member subjects never mint or read an edge, so they never trip the
  `use`-glossary gate (§4.5) — a spec that adds only member-subject rules renders its managed
  block byte-identically to before this section existed.

### 4.7 Registration facts and injection edges (the DI axis)

The captive-dependency rule — a singleton must not depend on a scoped or transient service —
needs two facts no other section provides: who is *registered* with what lifetime, and who
*injects* whom. Extraction records both; the `arch.Registered` noun (§3.2, §5.1) and the
`MustNotInject` verb (§3.3, §5.3) consume them.

- **An injection edge** is `(source type, injected parameter type, parameter file:line
  sites)`, read from the **declared instance constructors** of every solution-declared type —
  a declaration-side pass, not a body walk: the edge exists because the parameter is declared,
  whether or not any body dereferences it. **Primary constructors are included** (their
  parameters are the modern injection surface); implicitly-declared constructors mint nothing
  (the compiler's parameterless default, a record's copy constructor, static constructors).
  Parameter types decompose **definition-level like type edges** (§4.1): `IEnumerable<IFoo>`
  contributes `IEnumerable<>` and `IFoo`; arrays contribute their element type; constructed
  generics contribute the definition and every argument. Self-injection is dropped (the
  type-edge self-drop); enum and delegate types declare no walkable constructors and
  contribute none. External parameter types get nodes like any other external endpoint, so
  targets can match them (§4.1).
- **A registration fact** is `(lifetime, service type, implementation type?, file:line
  sites)`, read from a **whole-compilation pass** over every syntax tree — not a per-declared-
  type walk, because the most common composition root is a top-level-statements `Program`.
  Recognition is **symbol-first** (never name-only): the invoked method must resolve
  (`ReducedFrom`-normalized) to a method whose containing namespace is
  `Microsoft.Extensions.DependencyInjection` (or its `.Extensions` sub-namespace, the
  `TryAdd*` family's home), whose name is in the recognized-call table, and whose first
  parameter is `IServiceCollection`. A look-alike extension in a user namespace is not
  recognized — but an **in-solution wrapper whose body calls the real thing IS seen** (the
  pass walks the wrapper's body like any other tree).

  | Recognized call | Lifetime | service / implementation |
  |---|---|---|
  | `AddSingleton` / `AddScoped` / `AddTransient` and their `TryAdd*` twins | by name | two type-args → (service, impl); one type-arg → (T, T) iff the call has receiver-only arguments, else (T, —) — a factory or instance registration names no implementation type; the `typeof` overloads mirror the same split by `Type`-parameter count |
  | `AddHostedService<T>` | Singleton | service = `Microsoft.Extensions.Hosting.IHostedService`, synthesized — no syntactic mention exists (implementation-only if unresolvable); impl = `T` |
  | `AddDbContext<T>` / `AddDbContextPool<T>` | Scoped; an explicit **literal** `ServiceLifetime.X` argument is honored; a non-literal lifetime argument → no fact recorded (never guess) | (T, T) |
  | `AddHttpClient<TClient>` / `AddHttpClient<TClient, TImpl>` | Transient | (TClient, TClient) / (TClient, TImpl); the named-only string form registers no user type → no fact |

  Open-generic `typeof` registrations (`AddSingleton(typeof(IRepo<>), typeof(Repo<>))`)
  record definition-level, like every other fact (§4.1).
- **The honesty boundary** (rendered into the glossary whenever a `Registered` noun is in
  play, §10): registrations the source does not spell with a recognized call are invisible —
  `Configure`/`AddOptions` (the options interfaces are framework-registered), keyed-service
  overloads, raw `ServiceDescriptor`/`TryAddEnumerable`, assembly-scanning registrars,
  reflection, framework defaults, and wrapper extensions compiled into packages (no syntax
  tree to walk). The recognized-call table is the fence; growth beyond it is named in §11.
- **`arch.Registered(lifetime)` membership** is the union of **service and implementation
  type FQNs** of the recognized registrations at that lifetime (`arch.Registered()` — any
  lifetime). Registration is many-to-many, so membership is resolved at evaluation against
  these facts, never denormalized onto the type model. In **subject** position the selection
  intersects solution-declared types (§4.1, as always); in **target** position it also
  matches externals (`AddSingleton<IClock, SystemClock>()` makes an external `IClock` a
  matchable operand). An empty `Registered` **subject** fails the rule (the shared §4.1
  default) — the loud failure is how an author discovers the visibility boundary above. An
  empty `Registered` **operand** on `MustNotInject` means no such registrations exist — the
  win condition — so `MustNotInject` **never warns** (§5.3, the bare-`typeof` precedent).

### 4.8 Exception edges (catch and throw)

The scoped exception-catch policy — only top-level handlers may catch base `Exception` — and
the custom-exceptions rule — scoped code throws its own domain exceptions, not bare BCL ones —
need facts no other section records: who *catches* what, and who *throws* what. Extraction
records both; the catch verbs `MustNotCatch`, `MustNotCatchUnfiltered` and `MustNotSwallow` and
the throw verbs `MustOnlyThrow` and `MustNotThrow` (§3.3, §5.3) consume them.

- **A catch edge** is `(source type, caught type, file:line sites)`, recorded at every
  source-level `catch` clause. A typed catch mints the catch edge **only** — its type-name
  syntax already mints the ordinary reference edge, so the catch channel never double-mints
  (the explicit-`new` precedent, §4.5). A **bare `catch`** records `System.Exception` — the
  language's own semantics — as a catch edge only: nothing in source names the type, so no
  reference edge exists (and if the compilation cannot resolve `System.Exception`, nothing is
  minted). `when` filters never suppress the edge, and filter contents mint their ordinary
  type/member edges. **A rethrowing catch still mints**: `catch (Exception) { throw; }` is a
  catch of `Exception` — edges are facts, and a sanctioned log-and-rethrow site is excepted or
  grandfathered deliberately, never silently exempted. Type-parameter catches mint nothing;
  constructed generic caught types normalize to their definition (§4.1); self-catches drop;
  lambda and local-function catches attribute to the enclosing type; top-level-statements
  catches attribute to `Program`.
- **A catch edge additionally records its unfiltered sites** — a named, separate fact, not a
  narrowing of the rules above. Beside its sites, each edge carries the **subset of them whose
  `catch` clause spells no `when` filter**; the keying, the site list, and every minting rule
  are exactly what they were. The recorded subset is the *unfiltered* one, and the polarity is
  load-bearing: sites dedupe by `(file, line)`, so a filtered and an unfiltered catch of one
  type on one physical line collapse to a single site, and a filter that a conditional
  compilation applies under one target framework but not another reaches the merge as one site
  reported both ways. Recording which sites are unfiltered answers both cases the way a ban
  needs — the site counts as unfiltered, and a rule reads the recorded list as its evidence
  rather than subtracting one set from another. A **bare `catch`** is unfiltered; a bare
  **`catch when (…)`** is filtered. **Filter presence is syntactic**, and that is the honesty
  boundary: `when (true)` counts as filtered, because this axis records what the source spells
  and never judges what a filter tests. A filter that admits everything is a code-review
  matter, not an extraction one.
- **A catch edge additionally records its swallowing sites** — a second named fact recorded the
  same way, and a subset of the unfiltered subset. A site is **swallowing** when its clause
  carries no `when` filter **and** its block's last statement is not a `throw` statement, bare
  `throw;` or expression `throw new X(…)` alike. That composite is what a handler needs to hold
  a failure and continue: a filtered catch named its expectations, and a clause ending in a
  throw suppresses nothing, so neither is swallowing. The polarity is load-bearing for the same
  reason as the unfiltered subset — a rethrowing and a swallowing catch of one type on one
  physical line collapse to a single site, and a `#if`-divergent rethrow reaches the merge as
  one site reported both ways; recording the *swallowing* sites answers both the way a ban needs.
  **The rethrow fact is syntactic**, and that is its honesty boundary, stated rather than
  discovered: it reads the block's **last statement** and never analyses whether every path
  reaches it. `catch { if (…) return; throw; }` counts as throwing even though one path leaves
  having suppressed the failure, and `catch { if (…) throw; Cleanup(); }` counts as swallowing
  even though one path rethrows. The throw's operand is never judged either — a
  translate-and-throw and a bare rethrow read identically.
- **A throw edge** is `(source type, thrown expression's static type, file:line sites)`,
  recorded at throw statements and throw expressions alike (`?? throw`, conditional and
  switch-expression arms, and the expression-bodied `=> throw new X()`). A bare rethrow
  `throw;` mints nothing — it introduces no thrown expression — while `throw ex` mints the
  variable's **static** type (a pinned asymmetry): under a strict `MustOnlyThrow`, a
  `catch (Exception ex) { …; throw ex; }` in scoped code is red, and the cure is `throw;` —
  which also preserves the stack trace. `throw null` and type-parameter throws mint nothing;
  `throw new T()` coexists with its construction edge (§4.5) — one site, two facts;
  constructed generics normalize; self-throws drop; attribution follows the catch rules.
- **Throw helpers are the honesty boundary**: `ArgumentNullException.ThrowIfNull(x)` is an
  invocation, not a throw — it mints a member-use edge (§4.5) and no throw edge. A
  helper-mediated throw is invisible to this axis, exactly as reflection construction is
  invisible to the construction axis; the member-use axis is how helper calls are governed.
- **Matching is exact, definition-level FQN**: `MustNotCatch(typeof(Exception))` does not
  flag `catch (IOException)` — the narrow catch is the good state the rule steers toward,
  and widening the match would punish it. There is no hierarchy-aware operand matching
  (growth, §11); hierarchy adjectives as operands (`arch.Types.DerivedFrom<X>()`) match
  solution-declared types only, because external types carry a shallow hierarchy (§5.2) — a
  `DerivedFrom` operand never matches an external exception type.
- **`MustOnlyThrow` is strict.** The §4.1 "`MustOnly*` complement universe =
  solution-declared" exemption is scoped to the *reference* verbs: the unavoidable-BCL-noise
  argument does not hold for throw statements — no type must throw a BCL exception, guard
  throws are enumerable — and the custom-exceptions rule's teeth are exactly the
  `throw new InvalidOperationException(…)` in scoped code. External thrown types ARE
  constrained, and the fragment carries no exemption parenthetical (§5.3) because there is no
  exemption to state. A `Type`-sugar operand (`MustOnlyThrow(typeof(TimeoutException))`)
  resolves external allowed-set members by FQN as targets always have (§4.1); an allowed type
  absent from the model resolves empty and harmlessly allows nothing.
- **`MustNotThrow` is the ban polarity beside it.** `MustOnlyThrow` enumerates what a type may
  throw; `MustNotThrow` enumerates what it may not, for the ordinary case where the forbidden
  types are a handful and the permitted ones are the rest of the world. Matching is exact,
  definition-level FQN like every other operand on this axis, so a ban on `Exception` does not
  reach a `TimeoutException` throw — the narrow throw is the good state the rule steers toward,
  exactly as with the catch verbs. The two verbs are independent statements about one fact
  family: a spec may carry either, or both.
- Rendered glossary lines, beside the reference entry and gated per axis on the §4.5
  byte-identical-without-it terms: *"catch = a source-level `catch` clause (a bare `catch`
  counts as `System.Exception`)"* whenever the spec carries any catch-verb rule; *"throw =
  a source-level `throw` of the thrown expression's type (bare rethrows `throw;` are not
  recorded)"* whenever it carries any throw-verb rule. Each clause glosses the fact, not the
  verb, so swapping one verb on an axis for its sibling renders byte-identically.

### 4.9 Exposure edges (public signature positions)

The API-surface policy — a layer's public types must not leak another layer's types through
their own signatures — needs a fact no other section records: which type a public member
*names* in a signature position. Extraction records it; the `MustNotExpose` verb (§3.3, §5.3)
consumes it.

- **An exposure edge** is `(source type, exposed type, file:line sites)`, recorded at every
  public **signature position** of an effectively-public member: a method's return type and each
  parameter type, and a property/field/event's type. The sites are the exposing members'
  declaration lines. Where the signature **textually names** its type, the exposure edge is
  minted **beside** the ordinary reference edge that type-name syntax mints (the double-mint of
  §4.5's explicit-`new` precedent) — one site, two facts, the exposure channel never standing
  in for the type edge. A signature spelling that names no type mints no twin: the wrappers
  decomposition synthesizes from tuple and `?` syntax (nothing in `(Order, Widget)` or `int?`
  spells `ValueTuple` or `Nullable`) and a predefined keyword like `int` (keywords mint no type
  edge) reach the exposure channel alone — a twin-less endpoint is the designed record of a
  signature fact the reference axis never sees, not a gap.
- **The effective-visibility pin.** An edge is minted only from a member that is itself public
  **and** whose containing-type chain is public at every level. A `public` member nested in an
  `internal` type mints **nothing**: an internal type has no external contract, so nothing it
  declares is surface. This is the honesty boundary — internal members are not the API, and a
  rule about what a type exposes must not fire on what it keeps to itself. Explicit interface
  implementations are private, so they never mint; interface members are public, so a public
  interface's own signatures do.
- **Excluded**, because none is a member's public signature: constructors (their parameters are
  the injection axis's, §4.7), base-type and interface lists (inheritance, §5.2, not members),
  indexers/operators/conversions/accessors and every compiler-generated or record-synthesized
  member (the §4.6 inventory filter), `void` returns (a void return names no type), and
  non-public members. Type parameters, pointers, and `dynamic` mint nothing (the shared
  reference-universe gates); an enum's value fields are typed as the enum itself and self-drop.
- **Decomposition is definition-level and recursive**, exactly as a reference-edge target (§4.1)
  and an injected parameter type (§4.7): a constructed generic yields its open definition **and**
  every type argument (`Task<Order>` → `Task<>` and `Order`), an array yields its element type,
  a tuple yields the open `ValueTuple` definition **and** every element type (`(Order, Widget)`
  → `(T1, T2)`, `Order`, `Widget` — the open tuple definition is recorded under its display form
  `(T1, T2)`, not `System.ValueTuple<T1, T2>`), a nullable value type yields the open definition
  **and** its argument (`int?` → `System.Nullable<T>` and `System.Int32`), and a plain named
  type yields itself. Primitives and framework types **do** mint (factual and
  harmless — an operand selection never names them), so a `public string Name` exposes
  `System.String`; the noise stays invisible until a rule chooses to constrain it.
- **The honesty boundary is static signatures only.** Laundering an exposed type behind `object`
  (or `dynamic`, or an interface the caller downcasts) is invisible to this axis — the edge
  records the *declared* signature type, not what flows through it, exactly as the construction
  axis is blind to reflection. A signature that says `object` exposes `System.Object` and nothing
  more; the cure for a laundering signature is a named growth path (§11), not a wider match here.
- **Matching is exact, definition-level FQN**: `MustNotExpose(typeof(DataTable))` flags a
  `DataTable` return but not a `DataView` one, and there is no hierarchy-aware operand matching
  (growth, §11). Self-exposure (a type naming itself in its own signature) is dropped like the
  type-edge self-drop (§4.1).
- Rendered glossary line, beside the reference entry and gated on the §4.5
  byte-identical-without-it terms: *"expose = a public signature position (return, parameter, or
  property/field/event type) on a public member of an externally visible type"* whenever the
  spec carries a `MustNotExpose` rule.

## 5. Vocabulary v1 (closed; every member ships with pinned fragments)

### 5.1 Nouns

| Combinator | Fragment (reference position) |
|---|---|
| `arch.Types` | "types" |
| `arch.Layer("Domain", "MyApp.Domain.*")` | "the Domain layer" — definition fragment: "**Domain** — `MyApp.Domain.*`" |
| `arch.Namespace("MyApp.Legacy.Billing.*")` | "types in `MyApp.Legacy.Billing.*`" |
| `arch.Project("MyApp.Web")` | "types in project `MyApp.Web`" |
| `arch.Type(typeof(SqlConnection))` / `arch.Type<SqlConnection>()` | "`SqlConnection`" — simple name; FQN retained in the model |
| `arch.Registered(Lifetime.Singleton)` / `arch.Registered()` | "singleton-registered types" (per lifetime: "scoped-registered types", "transient-registered types") / "registered types" — types named in a source-visible container registration (§4.7). The fragment is the noun's **head** and survives adjectives ("Singleton-registered types must not inject scoped-registered types, except `X`." — never a false bare "Types, …"); the §5.2 `OfKind` head-substitution mechanic, pinned by an adjective-bearing-subject test. |
| `arch.AnyOf(a, b, …)` / `arch.AnyOf(typeof(X), typeof(Y), …)` | the union of its operands: "types in projects `A` or `B`" when they collapse, "types in project `A` or types in `B.*`" when they do not (§6). A union has no single noun — it is the one noun-position node that renders through its own assembly arm. |
| `arch.Member(typeof(DateTime), nameof(DateTime.Now))` / `arch.Member(() => DateTime.Now)` | "`DateTime.Now`" — member leaf, target-only (§4.5); parens iff method: "`Task.Wait()`" (`arch.Member<Task>(t => t.Wait())`) |

**The union noun (`arch.AnyOf`)** names every type any operand names. Pinned semantics:

- **Operands may be any selection** — a `Layer`, a `Registered` noun, an already-refined
  selection, or another union.
- **Nested unions flatten at mint**: `AnyOf(AnyOf(a, b), c)` ≡ `AnyOf(a, b, c)`, so prose and
  evaluation both read one leaf list. Only an *adjective-free* union operand flattens — an inner
  union carrying adjectives is a narrowed set of its own, so it stays a leaf.
- **One operand is legal and is an identity**: same model shape, and it renders exactly as the
  bare operand would in the same position. Selections are loop-buildable (§2 principle 5), so a
  loop that happens to yield one operand must not become an error.
- **Adjectives apply to the union, not through it**: `AnyOf(a, b).Except(c)` = (a ∪ b) − c, and
  `.Methods` projects over the union. The evaluator applies union adjectives *after* the set union.
- **The multi-type sugar** `AnyOf(Type first, params Type[] more)` desugars to
  `AnyOf(arch.Type(a), arch.Type(b), …)` — identical model, identical prose. It is the multi-type
  noun `arch.Types(params Type[])` cannot be (§3.2).
- **In subject position every operand must match at least one type** — an empty operand fails the
  rule with the empty-subject violation naming that operand, so a typo'd project name inside a
  four-way union is never masked by its siblings (§9). This is check-time behavior, not a §8
  spec-build error: whether an operand matches depends on the codebase, not on the spec. Target
  position keeps the softer per-rule inert-target warning unchanged.
- **A union subject anchors no scoped card**: even when a `Layer` is an operand, a union has no
  single home directory, so its rule renders into the root block only (§6).

### 5.2 Adjectives (reduced relative clauses)

| Combinator | Fragment |
|---|---|
| `.InNamespace("MyApp.Web.*")` | "in `MyApp.Web.*`" |
| `.OfKind(TypeKind.Interface)` | noun substitution: "interfaces" (kinds: Class, Interface, Struct, Enum, Delegate; records via escape hatch in v1) |
| `.WithSuffix("Controller")` | "named `*Controller`" |
| `.WithPrefix("Legacy")` | "named `Legacy*`" |
| `.WithNameMatching("*Repo*")` | "whose name matches `*Repo*`" |
| `.Implementing(typeof(IHandler<>))` / `.Implementing("MyApp.Web.IHandler<T>")` | "implementing `IHandler<T>`" — the string form renders byte-identically (string anchors, below) |
| `.DerivedFrom(typeof(ControllerBase))` / `.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` | "derived from `ControllerBase`" |
| `.AttributedWith(typeof(ApiControllerAttribute))` / `.AttributedWith("ModelContextProtocol.Server.McpServerToolAttribute")` | "attributed with `[ApiController]`" — `Attribute` suffix stripped, bracketed; the string form renders byte-identically (string anchors, below) |
| `.Except(selection)` | ", except {ref}" — canonicalized to sentence-final (§6) |
| `.Where(pred, description:)` | description verbatim — canonicalized to sentence-final (§6) |
| `.Authored()` | head premodifier: "authored types", "authored interfaces" (§6) |

`Implementing` auto-detects open generics: `typeof(IHandler<>)` means *any* construction;
`typeof(IHandler<Order>)` means that construction exactly. Generic rendering uses declared
type-parameter names: `IHandler<T>`, `IDictionary<TKey, TValue>`.

**Hierarchy matching (checker semantics).** `Implementing` and `DerivedFrom` are
**transitive with type-argument substitution** — they read the full interface closure
(Roslyn `AllInterfaces`: an interface reached through a base class or through interface
inheritance matches) and the whole base-type chain, arguments substituted. A class extending
`HandlerBase<Order>` where `HandlerBase<T> : IHandler<T>` satisfies
`Implementing(typeof(IHandler<Order>))`. An open definition matches any construction (on the
definition name); a closed construction matches that construction exactly (on the constructed
name); a **string** anchor is always the definition arm (string anchors, below). `AttributedWith`
is **declared attributes only** — no inheritance. External (metadata)
types carry a **shallow** hierarchy: extraction records their identity but not their own
bases/interfaces/attributes, so the hierarchy adjectives never match an external **subject** (a
documented boundary; pattern and name adjectives still work on externals). The **anchor**
position is not bounded that way: a *declared* type's base chain and interface closure are walked
straight through metadata, so `DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` selects a
declared controller — and reaches it through an intermediate external base too — even though the
anchor is a type the spec never compiles against. The same three
matchers back the `MustImplement` / `MustDeriveFrom` / `MustBeAttributedWith`
constraint verbs and, negated per subject over the anchor list, their `MustNot*` twins (§5.3).
Under a negative the shallow-external boundary reads in the passing direction: an anchor
reachable only through an external type's own bases/interfaces never matches, so the ban
silently passes.

**Generic sugar.** Each hierarchy adjective carries a thin generic twin —
`Implementing<T>()` ≡ `Implementing(typeof(T))`, likewise `DerivedFrom<T>()` and
`AttributedWith<T>()` (the attribute twin constrained `where T : Attribute`) — that desugars to
the identical adjective, changing nothing in the model. `arch.Type<X>()` ≡ `arch.Type(typeof(X))`
is the same idea on the noun. An **open** generic has no type-argument form, so it stays `typeof`
(`Implementing(typeof(IHandler<>))`); the sugar is for the closed/non-generic single-type case.

**String anchors.** Every single-type anchor position carries a `string` overload beside the
`typeof` form: the escape hatch for a type the spec project cannot compile against, so that
naming someone else's attribute or contract does not force a package reference on the spec just
to write the `typeof`. Two families carry them, on identical terms — **attribute** positions
(this adjective's `AttributedWith`, the `Must[Not]BeAttributedWith` verbs of §5.3, and their
member-side twins in §5.7) and **hierarchy** positions (the `Implementing` and `DerivedFrom`
adjectives above, and the `Must[Not]Implement` / `Must[Not]DeriveFrom` verbs of §5.3).

The string names the type **definition**'s FQN in extraction format — which is exactly what a
report prints for it, `Attribute` suffix included for an attribute and declared type-parameter
names for a generic (`"ModelContextProtocol.Server.McpServerToolAttribute"`,
`"MyApp.Web.IHandler<T>"`) — matched ordinal-exact; it matches **any construction** of that
definition, exactly as an open-generic `typeof` anchor does. A **constructed** spelling
(`"N.MarkAttribute<System.Int32>"`, `"MyApp.Web.IHandler<MyApp.Web.InvoiceCreated>"`) names no
definition and never matches: the hatch's stated honesty boundary, not a defect, and the one
place the string form is deliberately weaker than its `typeof` twin, which can name a
construction. Nothing is inferred from the string's shape: a dotless or suffix-less spelling is
a legal name that never matches, and only blankness is validated (§8 item 15). A string
anchor renders **byte-identically** to its `typeof` twin — the display name is the last
dot-segment outside any `<...>`, and colliding simple names widen by trailing dot-segments (§6);
in attribute position the `Attribute` suffix then strips and the brackets close over the
result — so which form a spec chose is invisible to its sentences. Overloads are homogeneous:
one call takes `typeof` anchors or strings, never a mix. The policy: `typeof` whenever the type
is referenceable — the compiler checks a `typeof`, and nothing checks a string — and the string
when it is not.

**`.Authored()`** narrows a selection to the types a person wrote — the opt-out from the §4.1
boundary that puts generator output inside a project noun. A type counts as generated when either
of two things holds:

- `System.CodeDom.Compiler.GeneratedCodeAttribute` sits on it or on any type containing it, so the
  nested types a generator emits inside an attributed container ride along without carrying their
  own attribute. This arm reads the merged symbol, which is what decides the partial case:
  `[GeneratedRegex]` puts its attribute on the generated *method*, so the author's own partial class
  stays authored while the regex types the generator emits beside it do not.
- Every file declaring it is generator output. A file is generator output when the workspace
  reports it as a source-generated document, or when its leading comment carries the
  `<auto-generated>` banner.

The fact is on `ITypeInfo` as `IsGenerated` (§5.6), so a `.Where` predicate can read it directly,
and the codebase survey counts it per project and per namespace so a rule author can see how much
of a project noun is generator output before writing the rule.

The second arm exists because the attribute is not universal. A compiled Razor view carries
`[RazorCompiledItemMetadata]` and a banner and no `[GeneratedCode]` at all, so under the attribute
alone a web tier's worth of views — often outnumbering the hand-written types beside them — read as
code a rule could hold its author to.

**Every declaring file, never any.** The attribute is a claim about a type; the two file signals are
claims about a file, and a file fact lifts to a type only when it holds of every file declaring it.
The `[GeneratedRegex]` case is the worked example in reverse: the generator writes the author's own
partial class and several wholly generated types into one banner-carrying file, so an any-file rule
would hand the author's class to the generated side and put it beyond every rule they wrote it for.

Nothing infers "generated" from a file path, an `obj/` directory, or a naming convention. A path
answers a different question — may a tool edit this file — and it answers this one wrongly for
exactly the partial types above. It is also redundant: a generator whose output a path convention
would catch is already caught by its provenance or its banner. Two limits follow from reading the
compilation rather than the disk. A hand-written type carrying `[GeneratedCode]` is reported
generated, because the signals are read and never second-guessed. And output the compiler never
compiled — XAML `.g.cs`, T4, anything an MSBuild task adds after the design-time build — is absent
from the model rather than misclassified in it, so no arm here can reach it.

### 5.3 Constraint verbs

| Combinator | Fragment |
|---|---|
| `.MustNotReference(target, ...)` | "must not reference {list}" |
| `.MustOnlyReference(target, ...)` | "must reference only {list} (external packages are not constrained by this rule)" |
| `.MustNotBeReferencedBy(source, ...)` | "must not be referenced by {list}" |
| `.MustOnlyBeReferencedBy(source, ...)` | "must be referenced only by {list}" |
| `.MustNotUse(member, ...)` | "must not use {list}" — member targets (§4.5) |
| `.MustNotConstruct(target, ...)` | "must not construct {list}" — selection/type targets; the DI-construction verb (§3.3) |
| `.MustNotInject(target, ...)` | "must not inject {list}" — selection/type targets; the captive-dependency verb (§3.3, §4.7). Never warns: an empty `Registered` operand means no such registrations exist — the win condition, the §4.1 bare-`typeof` precedent |
| `.MustNotCatch(target, ...)` | "must not catch {list}" — selection/type exception targets; the catch verb (§3.3, §4.8) |
| `.MustNotCatchUnfiltered(target, ...)` | "must not catch {list} without a `when` filter" — selection/type exception targets; the filter-aware catch verb (§3.3, §4.8). A bare `catch` counts as `System.Exception` and counts as unfiltered; filter presence is syntactic, so the filter's contents are never judged |
| `.MustNotSwallow(target, ...)` | "must not swallow {list}" — selection/type exception targets; the rethrow-aware catch verb (§3.3, §4.8), banning the composite of three facts: the caught type matches, the clause spells no `when` filter, and its block does not end in a `throw`. Both conditions are syntactic — the throw fact is the block's last statement, never an all-paths analysis — so a filtered catch and a rethrowing catch are both lawful |
| `.MustNotThrow(target, ...)` | "must not throw {list}" — selection/type exception targets; the ban polarity beside `MustOnlyThrow` (§3.3, §4.8), for the case where the forbidden thrown types are enumerable and the permitted ones are not |
| `.MustOnlyThrow(target, ...)` | "must throw only {list}" — selection/type exception targets (§3.3, §4.8); **strict**: external thrown types are constrained too, so the fragment carries no external-packages parenthetical — the caveat's absence is the strictness rendering |
| `.MustNotExpose(target, ...)` | "must not expose {list}" — selection/type targets in a public signature position; the signature-exposure verb (§3.3, §4.9) |
| `.MustResideInNamespace(glob)` | "must reside in `{glob}`" |
| `.MustHaveSuffix("Handler")` | "must be named `*Handler`" |
| `.MustHavePrefix("I")` | "must be named `I*`" |
| `.MustHaveNameMatching(glob)` | "must have a name matching `{glob}`" |
| `.MustImplement(type)` | "must implement `{X}`" — also anchors by string (§5.2's escape hatch): `MustImplement("MyApp.Web.IHandler<T>")` |
| `.MustDeriveFrom(type)` | "must derive from `{X}`" — string form likewise |
| `.MustBeAttributedWith(type)` | "must be attributed with `[{X}]`" — also anchors by string (§5.2's escape hatch): `MustBeAttributedWith("N.XAttribute")` |
| `.MustNotImplement(type, ...)` | "must not implement {list}" — none-of over the anchors; the negatives take `(Type first, params Type[] more)` (§10), and `(string first, params string[] more)` (§5.2), each call's list homogeneous |
| `.MustNotDeriveFrom(type, ...)` | "must not derive from {list}" — same two arities |
| `.MustNotBeAttributedWith(type, ...)` | "must not be attributed with {list}" — anchors bracketed and `Attribute`-stripped like the positive; also `(string first, params string[] more)` (§5.2), each call's list homogeneous |
| `.MustBeSealed()` | "must be sealed" |
| `.MustBeStatic()` | "must be static" |
| `.MustBeAbstract()` | "must be abstract" |
| `.MustBePublic()` | "must be public" |
| `.MustBeInternal()` | "must be internal" |
| `.Must(pred, description:)` | "must {description}" |

Naming note: the constraint carries its noun where a bare preposition would be ambiguous —
`MustResideInNamespace`, not `MustResideIn`, because `Project` (and later assembly) nouns
exist.

Generic sugar: the three type-taking hierarchy verbs carry generic twins —
`MustImplement<T>()` ≡ `MustImplement(typeof(T))`, `MustDeriveFrom<T>()`, and
`MustBeAttributedWith<T>()` (`where T : Attribute`) — desugaring to the identical constraint, on
the same terms as the §5.2 adjective twins (open generics stay `typeof`).
The negatives carry the same twins — `MustNotImplement<T>()` / `MustNotDeriveFrom<T>()` /
`MustNotBeAttributedWith<T>()` (`where T : Attribute`) — each desugaring to its verb's
single-anchor call. Every one of these verbs additionally carries the §5.2 string form on both
polarities — the attribute verbs on both sides too (type and member, §5.7) — so every anchor
position in both families offers the same triple: the compile-checked `typeof`, the
no-reference string, and the generic sugar (§10).

### 5.4 Posture verbs and options

| Member | Notes |
|---|---|
| `.Enforce(constraint)` | the law; violation = red |
| `.Migrate(from: prose, to: constraint)` | `from` is descriptive prose (the OLD pattern); `to` is the checkable target constraint |
| `.Quarantine(selection)` | scope statement; desugars per §7 |
| `.Baseline(path)` | Migrate **and** Quarantine; ratcheted grandfather store |
| `.WhileYoureThere(MigrationPolicy)` | `MigrateIfSmall` (default) \| `AlwaysMigrate` \| `NeverExpand` |
| `.BoundaryOnlyVia(params Type[])` | the sanctioned surface; omit entirely for a hermetic quarantine |
| `.Dragons(prose)` / `.DragonsDoc(path)` | load-bearing-weirdness prose / linked long-form doc |

### 5.5 Trailers

| Member | Notes |
|---|---|
| `.Because(prose)` | **required** on every rule and quarantined scope (§8 item 3) |
| `.Fix(prose)` | optional; for Quarantine containment it is auto-derived from `BoundaryOnlyVia` ("use `IBillingFacade`") and deliberately not author-overridable — `IQuarantinedScope` carries no `.Fix` |

### 5.6 Escape hatches

Predicate input contract (`ITypeInfo`): `Name`, `Namespace`, `Kind`, `ProjectName`,
`Accessibility`, `IsSealed`, `IsStatic`, `IsAbstract`, `IsRecord`, `IsGenerated`, `FilePaths`
(declaration file paths; empty for external types), attributes, base type, implemented
interfaces. The
contract grows additively as extraction learns new facts. The flags carry C# declaration
semantics — a static class is neither sealed nor abstract, interfaces are abstract,
structs/enums/delegates are sealed — and `IsRecord` is how record rules are written in v1
(§5.2). `IsGenerated` is the fact `.Authored()` filters on (§5.2), readable here so a predicate
can combine it with anything else the contract carries.

Member-predicate input contract (`IMemberInfo`, the input to a member `.Where`/`.Must`, §4.6):
`Name`, `Kind` (`MemberKind`: Method / Property / Field / Event), `DeclaringType` (an
`ITypeInfo` — the type-side contract, reused so a member predicate can reach its declaring
type's facts), `Accessibility`, `IsStatic`, `IsAbstract`, `IsVirtual`, `IsAsync`,
`ReturnTypeFullName` (methods; `System.Void` for a void method; null otherwise),
`MemberTypeFullName` (the property/field/event type; null for methods), `Parameters` (the
declared parameters in declaration order — each an `IParameterInfo` of `Name` and
`TypeFullName`, the type definition-normalized exactly like `ReturnTypeFullName`; empty for
properties, fields, and events), `Attributes` (the member's declared attributes,
ordinal-sorted by constructed name — each an `IAttributeInfo` of `DefinitionFullName` and
`FullName`, the definition open-reduced; empty when none; declared-only per §4.6, so accessor
and `[return:]` attributes are outside it), and `FilePaths` (declaration file paths). The flags carry the same C# declaration semantics as the member axis
(§4.6): an `override` member is not `IsVirtual`, an interface member is `IsAbstract`. The
contract grows additively, exactly like `ITypeInfo`.

Descriptions are **required parameters** (uncompilable without) and must be non-blank (§8
item 5). Phrasing conventions, pinned by example tests:

- `Where` descriptions are relative clauses continuing the noun phrase:
  `.Where(t => t.Name.Any(char.IsDigit), description: "whose name contains a digit")`
  → *"types in `MyApp.*` whose name contains a digit"*.
- `Must` descriptions are bare-infinitive verb phrases completing "must …":
  `.Must(t => t.Name.Length <= 40, description: "keep type names at or under 40 characters")`
  → *"must keep type names at or under 40 characters"*.

Descriptions are spliced verbatim (never derived from lambda source — Shouldly-style
`CallerArgumentExpression` is explicitly rejected; lambda source is not agent-consumable
prose).

**Which anchor forms survive a `net48` spec.** A spec project may target .NET Framework; the
host loads its DLL in an isolated load context and runs `Define()` there. A `typeof()` anchor
loads the anchored type's whole closure at that point, so it holds exactly while that closure
stays inside netstandard2.0. Two boundaries lie past it, both reported as spec-load errors
rather than crashes: an assembly that is not staged beside the spec DLL, and a type whose base
type or implemented interface lives in a Framework-only assembly (`System.Web.IHttpHandler`,
say). Neither is reachable by `typeof()` however the spec project is built. The anchor for
anything on that side of the line is a namespace pattern, `arch.Namespace("System.Data.*")`,
which needs no assembly load. A name pattern is not a substitute: a `Selection` matches only
types inside the checked codebase, so `arch.Types.WithNameMatching("SqlConnection")` goes inert
against an external type (§8, the inert-rule warning). The string anchors (§5.2) are the same
no-load form in attribute and hierarchy position: each names a type **definition** the spec
project never loads — `[McpServerTool]` on a codebase's methods is matchable without the spec
referencing the SDK that declares the attribute, and so is `IHandler<T>` on its types, which no
`typeof` spelling can offer. A namespace pattern remains the anchor for everything the string
form cannot reach, because a string anchor still names one definition rather than a set.

### 5.7 Member vocabulary (member subjects, §4.6)

**Projections** (mint a `MemberSelection`; the fragment is the subject head, §6):

| Combinator | Fragment (subject head) |
|---|---|
| `.Members` | "members of {ref}" |
| `.Methods` | "methods of {ref}" — the selection is a `MethodSelection`, so `.Returning` and `MustAcceptParameter` are available |
| `.Properties` | "properties of {ref}" |
| `.Fields` | "fields of {ref}" |
| `.Events` | "events of {ref}" |

**Member adjectives** (reduced relative clauses on the member set):

| Combinator | Fragment |
|---|---|
| `.WithSuffix("Async")` | "named `*Async`" — the same fragment as the type-side adjective (§5.2) |
| `.WithPrefix("Get")` | "named `Get*`" |
| `.WithNameMatching("*Handler*")` | "whose name matches `*Handler*`" |
| `.Returning(typeof(Task))` | "returning `Task`" — declaration-level (§4.6); an open generic renders declared type-parameter names ("returning `Task<TResult>`"); multiple anchors join "returning `Task` or `Task<TResult>`". Methods-only. |
| `.AttributedWith(typeof(McpServerToolAttribute))` / `.AttributedWith("ModelContextProtocol.Server.McpServerToolAttribute")` | head prefix: "`[McpServerTool]`-attributed" — premodifies the kind-plural (§6), so the subject reads "`[McpServerTool]`-attributed methods of types in `Zphil.LoadBearing.*`". Declared member attributes only (§4.6); the string form is §5.2's escape hatch, rendering byte-identically |
| `.Where(pred, description:)` | description verbatim — canonicalized to sentence-final (§6) |

**Member modal verbs** (turn a `MemberSelection` into a terminal `Constraint`):

| Combinator | Fragment |
|---|---|
| `.MustHaveSuffix("Async")` | "must be named `*Async`" — reuses the type-side naming fragment (§5.3) |
| `.MustHavePrefix("I")` | "must be named `I*`" |
| `.MustHaveNameMatching("*Async")` | "must have a name matching `*Async`" |
| `.MustBePublic()` | "must be public" |
| `.MustBeInternal()` | "must be internal" |
| `.MustBePrivate()` | "must be private" — member-only vocabulary (no type-side twin, deliberate) |
| `.MustBeStatic()` | "must be static" |
| `.MustBeAbstract()` | "must be abstract" |
| `.MustBeVirtual()` | "must be virtual" — member-only vocabulary (no type-side twin, deliberate) |
| `.MustBeAttributedWith(typeof(X))` | "must be attributed with `[{X}]`" — reuses the type-side fragment verbatim (§5.3); also anchors by string (§5.2) and carries a generic twin (§10) |
| `.MustNotBeAttributedWith(type, ...)` | "must not be attributed with {list}" — none-of over the anchors, the type-side negative's `(first, more)` shape and widening; string form included |
| `.MustAcceptParameter(typeof(CancellationToken))` | "must accept a parameter of type `CancellationToken`" — methods-only (it lives on `MethodSelection`, like `.Returning`, §3.2); single-`Type` arity; matching is definition-level (§4.6): a non-generic anchor matches exactly, an open-generic anchor matches any construction and renders declared type-parameter names ("… of type `IProgress<T>`"), a closed-generic anchor is refused at spec build (§8 item 20) |
| `.Must(pred, description:)` | "must {description}" — `pred` is `Func<IMemberInfo, bool>` (§5.6) |

The naming verbs reuse the type-side "must be named" / "must have a name matching" strings
verbatim; `MustBePrivate` and `MustBeVirtual` are new member-only vocabulary. The whole member
vocabulary shipped complete per the admission rule (§10): reification, pinned fragments, and
the checker semantics (member inventory, the shape/naming evaluators, the ratchet) landed
together.

The member attribute vocabulary (`AttributedWith` + `Must[Not]BeAttributedWith`) shipped on
the same admission-rule terms, over the §4.6 attribute facts. The **verbs** reuse the
type-side fragments verbatim — verb position is unambiguous, so there is nothing to
disambiguate. The **adjective** could not: an inline " attributed with" clause would render
the type-side adjective before a projection and the member adjective after one as **one
byte-identical sentence** for two different subjects — every method of an attributed type,
versus the attributed methods of any type — and both attachment sites are live in real
specs. So the member adjective is a **head prefix**, the `.Authored()` move (§5.2, §6)
replayed for the same reference-position ambiguity: its fragment premodifies the kind-plural
("`[McpServerTool]`-attributed methods of types in `Zphil.LoadBearing.*`"), and the two
subjects can never render alike. Unlike the type side's single substituted prefix, stacked
member head prefixes **concatenate** in authoring order ("`[ApiController]`-attributed
`[Audit]`-attributed methods of types") — two attribute adjectives are an intersection, and
a sentence that dropped one would describe a wider subject than the checker uses. The
generic twin is receiver-typed rather than `TSelf`-generic (§10): C# has no partial type
inference, so the sugar ships as an overload pair on `MemberSelection` and
`MethodSelection`, the second keeping `.Returning` and `MustAcceptParameter` reachable after
it.

`MustAcceptParameter` is the first methods-only modal verb — receiver-typed to
`MethodSelection` exactly like `.Returning`, so a parameter constraint on a property, field,
or event never type-checks. Its fragment is deliberately "a parameter of type `X`",
article-safe for arbitrary type names ("a/an `X` parameter" garden-paths on `a Order` /
`an IHandler`). It consumes the `Parameters` facts of §5.6 and evaluates as a member-shape
constraint (§4.6): a subject method passes iff any declared parameter's `TypeFullName`
matches the anchor's definition FQN.

## 6. Sentence assembly

- Fragments are lowercase; the renderer capitalizes sentence-initially. Identifiers and globs
  render in backticks; attributes as `[X]`; target lists join as "`A`, `B` or `C`".
- Enforce sentence: `{subject} {verb-phrase}.` → *"Types implementing `IHandler<T>` must be
  named `*Handler`."*
- **Layer voice**: a bare `Layer` subject speaks collectively — *"The Domain layer must not
  reference the Web layer."* Any adjective switches to types voice — *"Types in the Web layer
  named `*Controller` must not reference `SqlConnection`."* The switch is structural
  (adjective count > 0), hence deterministic.
- **Canonicalization**: `Except` and `Where` clauses render sentence-final regardless of
  chain position. Safe because selection algebra commutes — (T∖X)∩S = (T∩S)∖X — and it
  prevents garden-path sentences ("types, except `Foo`, named `*Service`").
- **Head premodification**: `.Authored()` prefixes the current head rather than replacing it or
  trailing the phrase — "authored types in `MyApp.*`", and "authored interfaces in `MyApp.*`" where
  `OfKind` has substituted the head. Chain position does not matter, and the prefix distributes
  into the operands of a union that does not collapse exactly as a head substitution does, so the
  filter the checker applies always reaches the sentence. Prefixing is what keeps the fact attached
  to the noun it narrows. A member subject renders its type selection in *reference* position
  (below), so a trailing clause would land between the types and the member's own adjectives —
  "…layers, excluding source-generated types returning `Task`" reads as narrowing the generated
  `Task`-returning types, which is not the set the checker uses.
- **Union collapse** (§5.1): a union of two or more operands, each adjective-free and all of the
  same noun kind, hoists one head and one locative and or-joins the operand names — *"Types in
  projects `A`, `B`, `C` or `D`"*, *"Types in `A.*` or `B.*`"*, *"The Domain or Web layers"*,
  *"`X` or `Y`"* — through the same no-Oxford-comma composer every other list uses. A union of
  type nouns joins through the shared anchor-list composer, so colliding simple names widen
  identically. **Otherwise the union falls back** to or-joining its operands' own phrases —
  *"Types in project `A` or types in `B.*`"* — which is what a heterogeneous union, a union with
  an adjective-carrying operand, and a single-operand union all render as. The fallback is a
  pinned decision in both directions, not an accident. A union's own adjectives then assemble
  against the hoisted head exactly as a single selection's do (head substitution, inline,
  sentence-final): *"Interfaces in projects `A` or `B`"*, *"Types in projects `A` or `B`, except
  `X`"*. On a union that does not collapse, a head adjective distributes across the operands —
  *"Interfaces in project `A` or interfaces in `B.*`"* — so the kind filter the checker applies
  always reaches the sentence. A bare (adjective-free) union reads as its own reference in both
  positions, which is what makes the single-operand identity hold in subject position too.
- **Scoped placement**: a rule earns a layer's per-directory card only when its subject's noun
  head *is* that layer, so a refinement (adjective / `Except`) still anchors. A union subject
  never does (§5.1) — it has no single home directory even when a `Layer` is one of its
  operands — so a union rule renders into the root block only.
- **Colliding simple names**: when two targets in one sentence share a simple name, both are
  qualified with the minimal distinguishing trailing namespace segments
  ("`Billing.Order` or `Sales.Order`"). Pinned rule. The negative hierarchy and attribute anchor
  lists (`MustNotImplement`/`MustNotDeriveFrom`/`MustNotBeAttributedWith`) widen by the same rule —
  the attribute form qualifies *inside* the brackets ("`[Billing.Audit]` or `[Sales.Audit]`") — so
  every multi-operand list disambiguates identically. Widening runs on the anchor's dot path,
  which a string attribute anchor (§5.2) supplies exactly as a `typeof` does — the dots inside
  a generic argument list belong to the argument's own path and never split — so string
  anchors collide and widen byte-identically to their `typeof` twins.
- **Member references** (§4.5) render as the backticked declaring type dot member —
  "`DateTime.Now`" — with `()` appended iff the member is a method ("`Task.Wait()`"; never
  a signature). Generic anchors use declared type-parameter names ("`Task<TResult>.Result`").
  Colliding declaring-type simple names widen by the same minimal-trailing-segments rule —
  including when the member names differ ("`Billing.Order.Total` or `Sales.Order.Refresh()`"),
  because the reader must see they are different `Order`s.
- **Member subjects** (§4.6) assemble as the member head prefixes + `{kind-plural} of
  {selection-reference}` + the inline adjectives in authoring order + the sentence-final
  `Where`. The kind-plural is the
  projection head ("methods", "properties", …); the `{selection-reference}` is the underlying
  type selection rendered in *reference* position (the same "types in `MyApp.Web.*`" / "the Web
  layer" form a target list uses), so a member subject reads "methods of types in `MyApp.Web.*`".
  Inline member adjectives (`WithSuffix`/`WithPrefix`/`WithNameMatching`/`Returning`) append in
  the order written; the member `Where` canonicalizes sentence-final exactly like the type-side
  `Where`. The flagship: `web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")` (where
  `web` is `MyApp.Web.*`) → *"Methods of types in `MyApp.Web.*` returning `Task` must be named
  `*Async`."* The member attribute adjective premodifies the kind-plural
  ("`[McpServerTool]`-attributed methods of types in `Zphil.LoadBearing.*`"), and stacked
  member prefixes concatenate in authoring order rather than replacing (§5.7), so both
  narrowing facts reach the sentence — the member-side twin of the `.Authored()` head
  premodification above, on the same grounds: a trailing clause would read as narrowing the
  wrong set.
- Posture voices consume these same fragments: Enforce renders as law; Migrate renders the
  counter-prior paragraph (slots: from-prose, to-sentence, policy, baseline burndown); Quarantine
  renders dragons + sanctioned surface. Full paragraph templates are pinned by the renderer's
  tests; the grammar carries every slot they need.

## 7. Quarantine desugaring (the single rule model, constructively)

`arch.Scope(id).Quarantine(sel).BoundaryOnlyVia(F).Baseline(p)` reifies to ordinary rule nodes:

- **`{id}/containment`** — internally `sel.Except(F).MustOnlyBeReferencedBy(sel ∪ F)`.
  (The `sel ∪ F` payload is the same union node `arch.AnyOf` mints, §5.1 — the desugaring
  predates the surface noun and is unchanged by it.) The
  formula holds whether the facade types live inside or outside the quarantined selection.
  **`.Baseline(p)` grandfathers existing inbound references** with the same ratchet semantics
  as Migrate — day-one adoption on a real legacy codebase must not be a wall of red; only
  *new* references into the scope are violations ("nothing **new** may reference the quarantined
  scope"). Burndown shows up in `loadbearing status` for free. An omitted
  `.Baseline(p)` fills the conventional default `arch/baselines/{scope-id}/containment.json` at
  build time, exactly like the Migrate default (§4.4) — the containment baseline path is never
  null post-build.
- **`{id}/tripwire`** — warning-severity, diff-aware touch check. With
  `loadbearing check --diff-base <ref>`, each changed file that declares a type in the quarantined
  selection yields one warning ("does the task actually require editing dragon territory?"); the
  rule itself passes and warnings never affect the exit code. Without diff context the rule is
  skipped, with a pointer at `--diff-base`. It carries the quarantined selection (not the boundary or a
  baseline) so it can map changed files to quarantined types.
- Scope children occupy the rule-ID namespace: duplicate detection runs over the
  **post-desugar** ID set, and a declared ID may not extend a scope ID — `{scope-id}/…` is
  reserved. Reserved suffixes today: `containment`, `tripwire`.
- Omitting `BoundaryOnlyVia` = hermetic quarantine (nothing outside may reference the scope).
  Because omission is legal, the verb deliberately stays plain `params` (not `(first, more)`)
  so that a zero-arg call reaches spec-build validation and gets the designed hint — "omit
  the call for a hermetic quarantine" (§8 item 8) — instead of an opaque compiler error. It grows
  **no** generic twin (decided against): a boundary is a variadic facade-plus-
  implementation list, which has no type-argument form; a single facade type is
  `BoundaryOnlyVia(typeof(IFacade))`.
- A project-headed quarantined selection follows §4.1's several-projects rule: a type the
  project co-declares is in the quarantine — for containment, for the tripwire's changed-file
  mapping, and for scope-card placement alike.
- Nested/overlapping quarantines compose as independent conjuncts; there is no scope precedence.
- **Practical note** (also for the derive prompt): `BoundaryOnlyVia` usually needs
  the facade *implementation* type(s) listed alongside the interface, or the composition
  root's DI registration of the concrete facade goes red on day one:
  `BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))`.

## 8. Spec-build validation catalog (all errors reported at once)

1. Duplicate ID over the **post-desugar** set (rules + scopes + generated children), across
   all spec classes; a declared ID may not extend a scope ID.
2. Dangling anchor — `Rule()`/`Scope()` without a posture verb.
3. Missing `Because` on any rule or quarantined scope.
4. Missing both `Dragons` and `DragonsDoc` on a quarantined scope.
5. Blank/whitespace prose anywhere; prose fields are single-line (no `\r`/`\n`, no leading
   markdown-structural characters — long-form prose links out via `DragonsDoc`).
6. Repeated trailer/option (`Because` twice, two `Baseline`s, …).
7. Malformed ID — must match `^[a-z0-9-]+(/[a-z0-9-]+)*$` (convention: `area/rule-name`).
8. `BoundaryOnlyVia()` with zero types (omit the call for a hermetic quarantine).
9. Duplicate layer name.
10. Selection minted on a different `Arch` instance ("selection not registered with this
    model").
11. Blank member name on an `arch.Member` used by a rule.
12. Member not **declared** on its anchored type (reflection `DeclaredOnly` typo guard);
    when the member is declared on a base type the error names that base and the `typeof`
    to use ("`System.Threading.Tasks.Task<TResult>` does not declare `Wait`; it is declared
    on base type `System.Threading.Tasks.Task` — use `typeof(Task)`").
13. `Member` minted on a different `Arch` instance (the §3.2 fresh-instance contract, member
    flavor).
14. Closed-generic `.Returning` anchor (§4.6): a `.Returning(typeof(Task<int>))` is refused
    because member return-type matching is definition-level — the error names the closed
    construction and the open definition to use ("`System.Threading.Tasks.Task<System.Int32>` is a closed generic;
    `.Returning` matches definition-level — use `typeof(Task<>)`"). A non-generic anchor
    (`typeof(Task)`) and an open-generic anchor (`typeof(Task<>)`) are both accepted. The
    checker carries a matching backstop (§4.6).
15. Blank/whitespace glob or affix — a namespace pattern, a type- or member-name pattern, or a
    suffix/prefix left empty. A blank affix is vacuously true and a blank glob throws at check
    time; both are almost always an authoring slip. Applies on the type and member sides alike,
    and to layer globs (reported spec-wide, named by layer). String anchors (§5.2) report
    through this same family — no new code — in every position of both families, adjective and
    verbs alike, under a label naming which kind of anchor was left empty: "attribute name"
    (`Blank attribute name on '{id}'.`), "interface name", "base type name". Blankness is the
    whole of a string anchor's well-formedness: a dotless or suffix-less spelling is a legal
    name that never matches (§5.2), and no other shape check exists.
16. Dead namespace subtree pattern — a trailing `.*` whose literal prefix carries a `*` (e.g.
    `MyApp.*.Controllers.*`). The subtree operator (§4.2) matches everything before the trailing
    `.*` literally, so the pattern never matches anything; the error names it and steers to a
    literal prefix ("… has a trailing `.*` subtree operator but its literal prefix contains a
    `*`, which never matches; anchor the subtree on a literal prefix"). An interior standalone
    `*` with no trailing subtree operator (`MyApp.*.Orders`, §4.2) is legitimate segment matching
    and accepted, as is a lone `*`. Type-name globs and affixes have no subtree operator, so this
    never applies to them — only the blank check (item 15) does. `NamespacePattern.Validate` owns
    the verdict, so the matcher and its build-time gate cannot drift.
17. Repeated posture — a rule given more than one posture verb (`.Enforce`/`.Migrate`), or a scope
    given `.Quarantine` more than once. The stage machine (§3.2) makes the *fluent* double-call
    uncompilable: the posture verbs live only on `IRuleBuilder`/`IScopeBuilder`, and the first call
    hands back a stage type without them. But those builders are mutable, so a **stored** builder
    reference (`var b = arch.Rule(id); b.Enforce(...); b.Migrate(...);`) can call a posture verb
    twice, and the second silently overwrites the first — the model keeps only the last posture.
    This item catches that stored-reference re-call: the count rides on the registration
    (`RuleRegistration.PostureCount` / `ScopeRegistration.QuarantineCount`) and a count > 1 is the error.
18. Unresolvable member-anchor expression (§4.5) — an `arch.Member<T>(x => ...)` /
    `arch.Member(() => ...)` lambda the resolver cannot reduce to a declared `(type, name)`. One
    code (`MemberExpressionUnresolvable`) carrying eight messages, each steering to the cure: a
    non-member body (`x => x.A + 1`), a method group (`x => x.M` or `() => T.M`, steered to the
    invocation form `x => x.M()` / `() => T.M()`), a receiver not reached directly on the lambda
    parameter through an identity cast — a chained access (`x => x.A.B`), a captured local or field,
    or a user-defined conversion — a static member reached through the typed instance form, an
    instance member reached through the parameterless static form, an indexer accessor (`get_Item`,
    `IsSpecialName`), a compile-time constant body (a `const` field, `enum` member, or literal
    the compiler inlines to its value), and an object-creation body (`() => new Foo()`, including
    target-typed `new()`) steered to the `MustNotConstruct` verb (§5.3). Reported **before** item 12 for the same member: an
    expression anchor is resolved from a real member and generic-normalized at mint, so item 12
    (and the check-time closed-generic refusal, §4.5) can never fire on it — those stay live only
    for the `typeof` form. The poison rides on the `Member` leaf itself: the diagnostic core is
    stored while the `DeclaringType`/`Name`/`IsMethod` backing fields stay default and now throw if
    read (fail closed — enforced, not merely documented), so it is collected in the same all-at-once
    pass as every other error.
19. Undefined `Lifetime` value on an `arch.Registered` noun used by a rule — a cast like
    `(Lifetime)7` names no defined lifetime; the error names the undefined value and the
    defined ones ("`(Lifetime)7` is not a defined `Lifetime` — use `Lifetime.Singleton`,
    `Lifetime.Scoped`, or `Lifetime.Transient`") and reports in the same all-at-once pass
    (`SpecValidationErrorCode.UndefinedLifetime`).
20. Closed-generic `MustAcceptParameter` anchor (§5.7): a
    `.MustAcceptParameter(typeof(IProgress<int>))` is refused because parameter-type matching
    is definition-level — the error names the closed construction and the open definition to
    use ("`System.IProgress<System.Int32>` is a closed generic; `MustAcceptParameter` matches
    definition-level — use `typeof(IProgress<>)`"). A non-generic anchor
    (`typeof(CancellationToken)`) and an open-generic anchor (`typeof(IProgress<>)`) are both
    accepted. The checker carries a matching backstop (§4.6).
21. Category-invalid hierarchy anchor, both polarities (§5.2, §5.3): a `Must[Not]Implement`
    anchor must be an interface; a `Must[Not]DeriveFrom` anchor must not be an interface; a
    `Must[Not]BeAttributedWith` anchor must derive from `System.Attribute` (`typeof(Attribute)`
    itself is refused — declared-attribute matching could never match it). A wrong-category
    anchor can never match, which makes a positive an always-red rule and a negative an
    always-pass — both silent authoring slips, so the error steers to the right verb
    ("`System.Exception` is not an interface; `MustNotImplement` requires an interface anchor —
    use `MustNotDeriveFrom` for a base class"; "`System.IDisposable` is an interface;
    `MustDeriveFrom` requires a non-interface anchor — use `MustImplement` for an interface";
    "`System.Attribute` does not derive from `System.Attribute`; `MustNotBeAttributedWith`
    requires an attribute anchor"). The member-side `Must[Not]BeAttributedWith` verbs (§5.7)
    carry the same check with the same messages. It applies to `typeof` anchors **only**, in
    both families: a string anchor (§5.2) carries no category to check — the name is just a
    string, extraction facts are the only authority on what it names, and refusing a spelling
    the host cannot load would break the escape hatch's whole point — so a wrong string is loud
    on a positive (always red) and silent on a negative, the honesty cost of the hatch. Blankness
    (item 15) is checked on a string anchor everywhere, including the adjectives; category is
    checked nowhere on one. The **adjectives**
    stay unchecked on both sides and across every hierarchy family, deliberately: a
    wrong-category adjective empties the subject and the fail-on-empty default (§4.1, §4.6)
    reds it loudly, whereas the silent slip this item exists to catch is the always-passing
    `MustNot` verb. Reported in the same all-at-once pass with the rule's
    spec-source `file:line`, like items 19/20.

Item 5 also reaches the member escape-hatch descriptions: a blank or multi-line member `Where`
(`Func<IMemberInfo,bool>`) or member `Must` description is caught by the same prose walk,
extended to descend through a `MemberConstraint`'s member subject and verb (§4.6).

Every walk in this catalog descends through a union (§5.1) on both axes — its operands *and* its
own adjectives — so `AnyOf(a, b).Where(p, "")` reaches item 5, `AnyOf(a, b).InNamespace("")`
reaches items 15–16, and a foreign operand or a foreign `Except` payload inside a union reaches
item 10. **Per-operand emptiness is not a §8 error**: whether an operand matches any type is a
fact about the codebase, not the spec, so it is check-time — one empty-subject violation naming
the operand (§5.1, §9).

Enforced at compile time instead (no catalog entry): zero-target dependency verbs,
zero-member `MustNotUse`, and zero-glob layers (`(first, more)` signatures); missing
escape-hatch descriptions (required parameters); trailers before postures (absent from stage
types); adjectives or modal verbs on a `Member` (a leaf outside the selection hierarchy,
§3.2); `.Returning` and `MustAcceptParameter` off any projection but `.Methods` (they live
only on `MethodSelection`, §3.2, §4.6, §5.7). Several member-anchor-expression mistakes are refused by the C# compiler outright,
so they never reach item 18: an event anchor (CS0070), an inaccessible member (CS0122), a
`ref`-returning member (CS8153), a `ref struct` receiver (CS8640), and a static member reached
through an instance expression (CS0176). A method-group body is the one that compiles (with
warning CS8974) and so is caught at spec build (item 18), not by the compiler.

Every error that names a rule, scope, or member additionally renders the offending anchor's
spec-source `file:line` (file name only, captured by `[CallerFilePath]`/`[CallerLineNumber]`
on the `Rule`/`Scope`/five `Member` factories) so each lands as a jump target — e.g.
`ArchSpec.cs:17: …`; the catalog verdicts above are otherwise unchanged, and duplicate-layer
and layer-glob errors stay location-free (a layer name is already a unique greppable string).

All-errors-at-once is a deliberate divergence from EF Core's fail-fast `ModelValidator`: an
agent fixing a spec sees every problem in one pass.

## 9. Deliberate divergences from the prior art

| Divergence | Prior-art contrast | Rationale |
|---|---|---|
| No `Check()`/`GetResult()` terminal | ArchUnit `check()`, NetArchTest `GetResult()` | rules are data; the CLI/adapter walks the finalized model |
| No violation-snapshot "freeze" | ArchUnit `FreezingArchRule` | "freeze" there means accept current violations as a baseline — that is `Migrate(...).Baseline(...)` here; `Quarantine` contains a scope, it does not accept its violations |
| No `noClasses()`, no `ShouldNot()` | Java ArchUnit, NetArchTest | lexical polarity; NetArchTest's gate+verb coexistence permits `.ShouldNot().NotBeSealed()` — ArchUnitNET already went negation-in-verb, we follow it |
| No `.As(...)` description override | ArchUnit/ArchUnitNET | free text never replaces structured prose; the ID names the rule, `Because` carries rationale |
| No constraint-level or whole-rule `And`/`Or` | `andShould()`, `IArchRule.And()` | one sentence per rule; compound requirements = multiple rules (atomic IDs for baselining/burndown); ArchUnit documents its own left-to-right precedence as a gotcha |
| No `When`/`Unless` | FluentValidation | specs are unconditional law; conditionality lives in the selection (FluentValidation's `ApplyConditionTo.AllValidators` retroactive rebinding is the cautionary tale) |
| No severity dial | FluentValidation `WithSeverity` | the posture is the severity |
| No conventions layer | EF Core | explicit only in v1 |
| Escape hatch hard-fails without description | FluentValidation `Must` defaults to *"The specified condition was not met for '{PropertyName}'."* | matches Java `DescribedPredicate`/`ArchCondition` (constructor-required description) and ArchUnitNET `FollowCustomPredicate(func, description)` |
| No source-text-derived descriptions | Shouldly `CallerArgumentExpression` | lambda source is not agent-consumable prose |
| All-errors spec validation | EF Core fail-fast `ModelValidator` | agents fix specs in one pass |
| Deterministic multi-spec discovery, loud failures | EF `ApplyConfigurationsFromAssembly` (order undefined, silent skips) | specs are law; law must load predictably |
| "reference", not "depend on" | ArchUnit family "depend on"/"access" | v1 edges are literally Roslyn type references; "depends on" over-claims for a type-level edge |

## 10. Naming morphology (style guide for vocabulary growth)

- **Nouns**: bare plurals or PascalCase names (`Types`, `Layer`, `Project`); the registration
  noun is a bare participle (`Registered`) — it names the set by the fact that admits
  membership.
- **Projections**: bare plurals naming the member kind (`Members`, `Methods`, `Properties`,
  `Fields`, `Events`) — they read as "{plural} of {selection}" (§4.6, §5.7).
- **Adjectives**: participles (`Implementing`, `DerivedFrom`, `Returning`) or prepositional
  phrases (`InNamespace`, `WithSuffix`, `OfKind`). A bare past participle (`Authored`) names the
  set by the fact that admits membership, the adjective twin of the `Registered` noun, and reads
  attributively in front of the head (§6). A verb-plus-object compound is not licensed:
  `ExceptGenerated` would both duplicate `Except` and stop reading as a modifier of the noun.
- **Constraints**: `Must[Not]` + verb phrase; polarity lexical; the noun rides along where a
  bare preposition would be ambiguous (`MustResideInNamespace`). The member-access verb is
  *use* (`MustNotUse`), glossary-pinned as *"use = a source-level member access"* — the
  member analog of "reference" (§4.1), chosen over "call"/"access" because it covers
  invocations, reads, writes, and subscriptions in one word. The constructor-ban verb is
  *construct* (`MustNotConstruct`), glossary-pinned as *"construct = a source-level object
  creation (`new`, including target-typed `new()`)"* — the third axis beside reference and use.
  The injection-ban verb is *inject* (`MustNotInject`), glossary-pinned as *"inject = a
  source-level constructor-parameter dependency (primary constructors included)"* — the fourth
  axis (§4.7). The catch verb is *catch* (`MustNotCatch`), glossary-pinned as *"catch = a
  source-level `catch` clause (a bare `catch` counts as `System.Exception`)"* — the fifth axis
  (§4.8). Its filter-aware sibling `MustNotCatchUnfiltered` narrows that axis rather than
  opening a new one: the qualifier rides the verb name as a bare past participle and spells
  itself out in the fragment (*"must not catch {list} without a `when` filter"*), because a
  `when` filter is not a type and so cannot be an operand (§3.3). Its rethrow-aware sibling
  `MustNotSwallow` narrows the same axis again, and takes a **verb word of its own** rather than
  a third participle on `catch`, because what it bans is a composite of three facts — the caught
  type, the absent filter, and the absent terminal `throw` — and no bare participle names a
  composite: `MustNotCatchUnfilteredUnrethrown` enumerates the conditions instead of naming what
  they add up to, and reads as a fourth qualifier rather than a law. *Swallow* is the word the
  code review already uses for holding a failure and continuing, and it is a verb, so it takes
  the `Must[Not]` + verb-phrase form directly. The glossary clause does **not** move with it: the
  clause glosses the *fact* (*"catch = a source-level `catch` clause"*), not the verb, so a spec
  that swaps any catch verb for another renders that clause byte-identically — the same
  byte-identical-without-it discipline the axis gating already has, one level down.
  The throw verb is *throw*
  (the strict allow-list `MustOnlyThrow` and its ban twin `MustNotThrow`), glossary-pinned as
  *"throw = a source-level `throw` of the thrown expression's type (bare rethrows `throw;`
  are not recorded)"* — the sixth axis (§4.8). The exposure verb is *expose* (`MustNotExpose`),
  glossary-pinned as *"expose = a public signature position (return, parameter, or
  property/field/event type) on a public member of an externally visible type"* — the seventh
  axis (§4.9). The glossary line composes from these per-axis clauses (reference always; use iff a
  member-target rule; construct iff a ctor rule; inject iff an injection rule; catch iff a
  catch rule; throw iff a throw rule; expose iff an exposure rule) joined with "; "
  and closed by the drill-down tail, so each axis
  gates independently and a spec without a given axis renders byte-identically to before that axis
  existed (§4.1/§4.5's byte-identical-without-it discipline, generalized). A `Registered` noun
  anywhere in a rule — subject or operand — additionally gates its own glossary line on the same
  byte-identical-without-it terms: *"registered = named in a source-level container registration
  (`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/`AddDbContext`/
  `AddHttpClient<TClient>`); registrations made by assembly scanning, factory internals, or
  framework defaults are not seen."*
- **Posture verbs**: imperative (`Enforce`, `Migrate`, `Quarantine`). **Options**: nouns
  (`Baseline`) or deliberate idiom (`WhileYoureThere` — it names the boy-scout rule).
  **Trailers**: conjunctions (`Because`) / nouns (`Fix`).
- `(first, params more)` signatures wherever an empty list would be meaningless.
  `MustAcceptParameter` is deliberately single-`Type`: over several parameter anchors one
  sentence cannot say whether ALL are required or ANY suffices, so a second required
  parameter type is a second rule. The same ambiguity keeps the positive hierarchy verbs
  single-`Type`, but it does not bite a negation, so their `MustNot*` twins take
  `(Type first, params Type[] more)` — "must not implement `A` or `B`" is unambiguous none-of.
- **Anchor-form triples.** A single-type anchor position ships `Type` / `string` / `<T>`
  together — the compile-checked `typeof`, the §5.2 no-reference escape hatch, and the
  generic sugar — all reifying to one internal anchor, so form choice is invisible to the
  model and the sentence. It holds for both families, attribute and hierarchy, and one union
  serves them: what differs between the two is which extracted facts the matcher reads and
  whether the prose brackets the name, neither of which is a property of the anchor. A new
  anchor position ships the whole triple or it is not an anchor position.
  The generic twin is `TSelf`-generic where inference allows and
  **receiver-typed where it does not**: C# has no partial type inference, so a
  `TSelf`-generic member adjective twin would force both type arguments at every call site
  (`AttributedWith<MethodSelection, MyAttribute>()`); it ships instead as an overload pair on
  `MemberSelection` and `MethodSelection`, the second keeping `.Returning` and
  `MustAcceptParameter` reachable after the sugar.
- Named arguments are the documentation convention for prose parameters (`from:`, `to:`,
  `description:`).
- **Admission rule**: a new vocabulary member ships with model node + fragment(s) + pinned
  string test + checker semantics (or an explicit "reifies now, checks later" note) — or it
  does not ship. The member-subject vocabulary (§4.6, §5.7) used that clause and shipped
  complete: every projection, member adjective, and member verb landed with reification, a
  pinned string, and checker semantics (member inventory, the shape/naming evaluators, the
  ratchet) together.

## 11. Growth paths (designed-for, not built)

Member-level *targets* shipped first (`arch.Member` + `MustNotUse`, §4.5); then member
*subjects* (the `.Members`/`.Methods`/`.Properties`/`.Fields`/`.Events` projections with
member adjectives and shape/naming verbs, §4.6); then expression member anchors and generic
type sugar (`arch.Member<T>(x => x.M)` / `arch.Member(() => X.M)` and `arch.Type<X>()` / the
`Implementing`/`DerivedFrom`/`AttributedWith` twins, §4.5, §5.2–§5.3); then member attribute
facts with the member attribute vocabulary (§4.6, §5.7). Still growth on the member axis: the
`MustOnlyUse` / `MustNotBeUsedBy` verb twins; member-granular *source* attribution on the
dependency verbs (today a violation names the using *type*, not the using member); string-FQN
*member* anchoring — the last of the anchoring residue, now that both single-type anchor
families carry a string form (§5.2): the escape hatch for a member that neither `typeof` +
`nameof` nor an expression lambda can name (a member on a type the spec project cannot
reference);
indexer/operator bans (the syntax-walk boundary moves deliberately, §4.5); and subject-side
member *shape* adjectives (`.Methods.ThatAreVirtual()`, …)
— the member constraint-side verbs and the `IMemberInfo` flags shipped (§5.7, §5.6), only the
adjective position remains, exactly mirroring the type-side gap below. One entry shipped off
this list whole, and a second narrowed twice: member-level attribute facts landed with the
member-side `AttributedWith` / `MustNotBeAttributedWith` pair — carrying the positive
`MustBeAttributedWith` the entry never promised, per the admission rule (§10) — and string-FQN
anchoring shipped as §5.2's escape hatch in two passes, the *attribute* positions first and the
*hierarchy* positions after, each carrying its family's whole triple (§10). Members really are
the only residue now.

On the parameter facts (§4.6): the `WithParameterOfType` adjective — the adjective-position
twin of `MustAcceptParameter`, selecting rather than constraining; the `MustNotAcceptParameter`
negative twin; multi-`Type` arity (refused in v1 — one sentence cannot say whether ALL anchors
are required or ANY suffices, §10); richer `IParameterInfo` facts (ref kind, optionality,
default values, `params`, ordinal position); hierarchy- or assignability-aware parameter
matching (the exception-axis bar applies: an explicitly named new semantic, never a widening of
definition-level-exact); accessibility-scoped member subjects (`.Methods.ThatArePublic()` — the
canon's literal "public surface" scope; the escape hatch reaches `Accessibility` today); and
`graph` parameter data.

On the injection axis (§4.7): the `MustOnlyInject` / `MustNotBeInjectedBy` twins; property- and
method-injection edges (constructor parameters are the recorded form); recognition growth beyond
the pinned call table — keyed services, `ServiceDescriptor`/`TryAddEnumerable`, assembly-scanning
registrars — the table is the fence, everything outside it the documented honesty boundary;
lifetime facts on `ITypeInfo` (registration membership stays model-side, resolved at evaluation);
and `graph` registration data.

On the exception axis (§4.8): the `MustOnlyCatch` allow-list and the `MustNotBeCaughtBy` /
`MustNotBeThrownBy` passive twins; hierarchy-aware operand matching
(exact definition-level FQN is pinned, so a ban on `Exception` deliberately
does not reach derived catches — a hierarchy-matching form would be a new, explicitly named
semantic, not a widening of this one); and `graph` catch/throw data. Three entries shipped off
this list on exactly the terms the hierarchy item states (a new, explicitly named semantic,
never a widening): `MustNotThrow`; `when`-filter awareness — a filter still never
suppresses the catch edge; what is new is the recorded fact beside it, the unfiltered sites
`MustNotCatchUnfiltered` reads as its evidence (§4.8) — and, on the same template,
**rethrow-aware refinement**, which was ratified *against* for v1 on the grounds that edges are
facts and a sanctioned log-and-rethrow site is excepted or grandfathered deliberately. That
rationale held until its cost was measured: in this repository's own spec, type-granular
exemption of nine rethrow sites that suppressed nothing was holding nine already-compliant
filtered sites out of the law with them. The edge is still a fact and its identity still does
not move; what is new is a second recorded fact beside filter presence — the swallowing sites —
and `MustNotSwallow`, the verb that reads it.

Also declined in writing, and this one stays declined: **member-granular source attribution on
catch edges**, and member subjects for the catch verbs. It would buy nothing measurable — after
that burndown, the residual sanctioned handlers are small single-role boundary classes
where the type effectively *is* the site, and there is nothing left for finer granularity to
recover. The designed-for entry stays under the admission rule, and a consumer arriving with
evidence reopens it, exactly as this rethrow entry was reopened. **Site-granular sanctioning as
a spec surface is declined outright**: a per-site ledger inside a spec re-invents baseline
mechanics under another name, and the candour argument that puts an exemption in the spec where
it is read cuts equally against a ledger in disguise.

On the exposure axis (§4.9): the `MustOnlyExpose` allow-list complement — the §4.1
`MustOnly*` external exemption would apply to it naturally, but v1's canon for surface
policy is the Not-form — and the `MustNotBeExposedBy` passive twin; accessibility variants
(a protected-inclusive surface, or minting from declared rather than effective visibility —
v1 mints only from effectively-public members, because an internal type has no external
contract); generic-constraint-clause positions (`where T : Order` mints nothing in v1 — a
constraint clause names a type without placing it in a signature position); member-granular
*source* attribution (the edge keys the exposing *type*, not the exposing member — the same
residue the dependency verbs carry above); laundering awareness — a signature declared
`object` exposes `System.Object` and nothing more (the §4.9 honesty boundary: the edge is a
static-signature fact, and internal members are not surface), so a flow- or cast-aware form
would be a new, explicitly named semantic, never a widening of the exact match; and `graph`
exposure data (exposure edges stay out of the graph).

On the string anchors (§5.2): **constructed-generic** anchoring — a string naming a
construction rather than a definition (`"MyApp.Web.IHandler<MyApp.Web.InvoiceCreated>"`)
renders, and matches nothing. Making it match that construction, the way a closed `typeof`
anchor does, would be a new and explicitly named semantic on the exception axis's terms, never
a widening of the definition-level-exact one pinned here: today's silence is what makes a
string anchor's meaning independent of whether the spec author happened to spell type
arguments. Also unbuilt: a string form for the *set*-valued anchor positions — the dependency
verbs' `Type` sugar and `arch.Type(...)`, where a namespace pattern is already the no-load
form (§5.6) and a string would only duplicate it.

Elsewhere: `MustBeAcyclic()` on namespace slices (ArchUnit `slices()` analog); an intersection
or difference combinator (`arch.AnyOf` is union only — `Except` already covers difference);
unions of *member* selections (the projections compose over a union subject already); a
layered-architecture macro (deferred:
it would mint N rules under one ID, muddying baselines); assembly-anchored layers;
subject-side shape adjectives (`.ThatAreSealed()`, `.ThatAreStatic()`, …) — the
constraint-side verbs and the `ITypeInfo` flags shipped (§5.3, §5.6); only the adjective
position remains; a `.MayMatchNothing()` opt-out from the fail-on-empty default (§4.1); a
`MustBeOfKind` constraint twin.

## 12. Canonical sample spec

Compiles as pinned test code.

```csharp
public sealed class ArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
        Layer web    = arch.Layer("Web",    "MyApp.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Domain is UI-agnostic; transaction boundaries live in services.")
            .Fix("Define an abstraction in Domain and implement it in Web.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*")
                         .MustHavePrefix("I"))
            .Because("House naming convention; agents grep by I-prefix.");

        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                from: "Controllers open SqlConnection directly (legacy Active Record style).",
                to: web.WithSuffix("Controller").MustNotReference(typeof(SqlConnection)))
            .Baseline("arch/baseline.json")
            .WhileYoureThere(MigrationPolicy.MigrateIfSmall)
            .Because("Repository pattern for testability — ADR-012.")
            .Fix("Inject the repository; see OrdersRepository for the pattern.");

        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Baseline("arch/baseline.json")
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");

        arch.Rule("naming/handlers")
            .Enforce(arch.Types.Implementing(typeof(IHandler<>)).MustHaveSuffix("Handler"))
            .Because("Handler discovery is convention-based (see HandlerRegistry).");

        arch.Rule("di/handlers-via-registry")
            .Enforce(arch.Types.Except(arch.Type<HandlerRegistry>())
                         .MustNotConstruct(arch.Types.Implementing(typeof(IHandler<>))))
            .Because("Handlers are resolved through HandlerRegistry; direct construction bypasses discovery.");

        arch.Rule("style/type-name-length")
            .Enforce(arch.Types.InNamespace("MyApp.*")
                         .Must(t => t.Name.Length <= 40,
                               description: "keep type names at or under 40 characters"))
            .Because("Long type names break the generated architecture tables.");
    }
}
```

Refinements vs the founding-session sketch: `Layer` is itself a selection (no `.Types` hop);
`Implementing` auto-detects open generics (no `ImplementingOpenInterface`); naming rules use
closed vocabulary; escape hatches are `Where`/`Must`; Quarantine shows facade-implementation
listing and the grandfathering baseline. The founding dogfood rule was one line —
`arch.Project("Zphil.LoadBearing").MustNotReference(arch.Project("Zphil.LoadBearing.Roslyn"))` —
since grown into the three-posture self-spec (`LoadBearingArchSpec`: three Enforce rules plus a
Migrate and a Quarantine).

## 13. Composition: rule packs

A rule pack is an ordinary class library. It exports static methods that take a caller's `Arch`
and declare rules on it, and a consuming spec project references it and calls the ones it wants.
There is no plugin host, no manifest, no include directive, and no discovery: a rule lands only
where a spec calls for it. Every claim below is pinned by a test.

**Many spec classes, one `Arch`.** A build runs every discovered spec's `Define` against a single
`Arch` instance, and rules land in the order the specs ran (§3.2). A pack call inside one spec and
a hand-written rule inside another compose into one model with no coordination between them.

**IDs dedupe across every source.** The duplicate-ID check (§8 item 1) runs over the whole
post-desugar ID set, so taking a rule from a pack and also writing it locally is a spec-build
error rather than two silent reports of the same law. This is why packs need no ID-prefix scheme:
a prefix would trade one loud duplicate for two quiet near-duplicates, which is the failure the
check exists to prevent.

**A pack composes with the public vocabulary only.** `Selection` and `Constraint` are closed
hierarchies (§3), so a pack has nothing private to build with. A pack-declared rule and the
hand-written equivalent therefore reify to the same nodes and render the same sentence; a pack
cannot produce a rule an inline spec could not.

**Invocation is always explicit.** Referencing a pack adds no rules. Spec discovery runs over one
assembly and never reaches into what that assembly references (§9, "no conventions layer"), so a
referenced pack contributes exactly what the spec asked for. Opting out of a pack rule is not
calling its method; there is no suppression mechanism because none is needed.

**Provenance is free.** Rule anchors capture `[CallerFilePath]`/`[CallerLineNumber]` at the call
site (§8), and inside a pack that site is the pack's own source. A pack forwards nothing and
declares nothing extra, and its rules still report at a `file:line` a reader can open. Where an ID
collides, the report lands at the *first authored* occurrence, so which file is named follows the
order the specs ran.

**Posture belongs to the consumer.** The same pack rule is `Enforce` in a codebase that already
keeps it and `Migrate`, against its own baseline, in one that does not. A pack that shipped a
posture would be asserting a fact about code it has never seen. Baseline paths stay conventional
(§4.4), so a pack never names a path either.

**The pack owns `Because`; the consumer may override `Fix`.** The reason a rule exists is the same
everywhere, so it ships with the rule. Remediation names local types, so it does not. The
override is a parameter rather than a trailer: a pack method returns `void`, which makes exactly
one `Because` and one `Fix` reach the rule and a second trailer uncompilable rather than a
repeated-trailer error (§8 item 6).

**Anchor doctrine for a self-hosting pack.** A pack that ships inside a codebase it governs writes
its member anchors as `arch.Member(typeof(X), nameof(X.M))`, never the expression form. An
expression anchor is real syntax: it mints a use edge attributed to the pack's own type, which
then appears as a violation of the pack's own rule. `nameof` operands mint nothing, and the two
forms reify identically (§4.5), so the doctrine costs nothing but has to be deliberate.
