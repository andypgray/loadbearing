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
   `Rule().Enforce/Migrate(...)` and `Scope().Freeze(...)` on the surface; Freeze desugars to
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
             | arch.Member(typeof(X), nameof(X.M))
             | arch.Member<T>(x => x.M) | arch.Member(() => X.M)
rule       :=  arch.Rule(id) . posture-verb . trailer*
scope      :=  arch.Scope(id) . Freeze(selection) . freeze-clause* . trailer*

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
freeze-clause:=  BoundaryOnlyVia(types...) | Dragons(prose) | DragonsDoc(path) | Baseline(path)
trailer      :=  Because(prose) | Fix(prose)
```

Modal-verb targets are selections for the dependency verbs (§3.3) and members for the
member-access verb `MustNotUse` (§4.5). The member-modal verbs (§4.6) take no target — they
are shape/naming assertions over the projected member set, so `member-selection` is itself the
whole subject side of a member-shape sentence.

### 3.2 Stage machine

```
Arch
 ├─ .Layer(name, string glob, params string[] more) → Layer (: Selection)
 ├─ .Namespace(glob)  → Selection
 ├─ .Project(name)    → Selection
 ├─ .Type(Type)       → Selection      (single type; there is deliberately no
 │                                      arch.Types(params Type[]) — it cannot coexist with
 │                                      the arch.Types property (CS0102); multi-type nouns
 │                                      arrive with the future AnyOf union, §11)
 ├─ .Type<T>()        → Selection      (generic sugar: ≡ .Type(typeof(T)); §5.2 note)
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
MemberSelection — member adjectives (.WithSuffix / .WithPrefix / .WithNameMatching / .Where)
               → the SAME concrete member-selection type; member modal verbs → Constraint (terminal)
MethodSelection — a MemberSelection minted by .Methods that additionally offers
               .Returning(Type first, params Type[] more) → MethodSelection (§4.6)
IRuleBuilder — ONLY .Enforce(Constraint) → IEnforceRule | .Migrate(from:, to:) → IMigrateRule
IEnforceRule — .Because / .Fix
IMigrateRule — .Because / .Fix / .Baseline(path) / .WhileYoureThere(MigrationPolicy)
IScopeBuilder — ONLY .Freeze(Selection) → IFrozenScope
IFrozenScope — .BoundaryOnlyVia(params Type[]) / .Dragons(prose) / .DragonsDoc(path)
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
  `.Returning` stays reachable in any adjective order. Because `.Returning` lives only on
  `MethodSelection` (the `.Methods` projection's type), calling it off `.Properties` / `.Fields`
  / `.Events` / `.Members` is uncompilable by construction — a structural consequence, not a
  validated one.

### 3.3 Dependency-verb overloads (pinned)

```csharp
Constraint MustNotReference(Selection first, params Selection[] more)
Constraint MustNotReference(Type first, params Type[] more)      // sugar for arch.Type(...)
```

Same shape for `MustOnlyReference`, `MustNotBeReferencedBy`, `MustOnlyBeReferencedBy`, the
constructor-ban verb `MustNotConstruct`, and the injection-ban verb `MustNotInject`: each
carries the identical pair — a `Selection`
list and the `Type` sugar — and deliberately **no expression overload**. Constructor-ness lives in
the verb, not the anchor, so ordinary selections name what may not be `new`ed
(`arch.Types.Implementing(typeof(IHandler<>))`), which scales to "all registered services" where a
per-constructor anchor would be dummy-argument noise. Injection-ness lives in the verb the same
way, and its natural operands are the registration-fact selections
(`arch.Registered(Lifetime.Scoped)`, §4.7), though any selection or bare type works. The
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
`nameof` leaves unverified. There is deliberately no string-FQN sugar overload: string-FQN
anchoring is named growth (§11).

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

- **Subjects** range over **solution-declared types** — the set extraction walks.
- **Targets** range over **all referenced types, including metadata references** —
  `MustNotReference(typeof(SqlConnection))` and `arch.Namespace("System.Web.*")` as a target
  both work against BCL/NuGet types.
- **`MustOnly*` complement universe = solution-declared types.** BCL/NuGet references are
  exempt, and the fragment states it: *"must reference only {list} (external packages are not
  constrained by this rule)"*. Without this pin every `MustOnlyReference` rule is either 100%
  violated (`System.String`) or renders a false sentence — both fatal to "the prose is
  provably true".
- **`MustOnly*` is strict — no implicit self-allowance.** A subject's reference to another
  member of its own selection is a violation unless that selection is itself among the allowed
  targets; authors list their own layer when they mean it. Internal precedent: the Freeze
  `{id}/containment` desugaring explicitly lists the frozen selection in its own allowed set
  (§7). (Self-edges never arise — extraction drops them.)
- `MustOnlyBeReferencedBy` needs no caveat: only solution types can be observed referencing.
- Checker behavior: an empty *subject* selection **fails** the rule by default
  (ArchUnit and ArchUnitNET precedent, with a pinned message). An empty resolved *operand* set
  warns **"rule is inert"** only on a forbidden-set dependency verb (`MustNotReference` /
  `MustNotBeReferencedBy`) whose operand is a **pattern selection** (Layer / Namespace / Project
  or a refined `Types`); a bare `typeof(...)` target absent from the codebase is the *win
  condition* and stays silent, and the `MustOnly*` verbs never warn (an empty allow-set is loud
  on its own).
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
  directly in `MyApp.Legacy.Billing` falls outside its own freeze — the silent-hole bug.)
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
- **Shape/naming/inheritance/attribute verbs and escape hatches**:
  `(ruleId, subject symbol ID)`.
- Symbol IDs are Roslyn `DocumentationCommentId` strings — stable across file moves and
  formatting.

### 4.4 Migrate defaults

- `.Baseline(path)` omitted ⇒ a deterministic conventional path derived from the rule ID:
  `arch/baselines/<rule-id>.json`,
  where the rule ID's `/` separators become subdirectories (IDs match §8's
  `^[a-z0-9-]+(/[a-z0-9-]+)*$`, so the result is filesystem-safe). The path is filled into the model
  at build time — `MigrateData.BaselinePath` is never null post-build — stored forward-slash, and
  resolved by the CLI against the solution directory (an absolute spec path wins).
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
that reaches through `Constraint.Subject` — foreign-`Arch` detection (§8 item 10) and Freeze
desugaring (§7) — keeps working unchanged on the type side.

- **The inventory universe** is the **declared members of solution-declared types** — the same
  subject universe as type selections (§4.1), read one level down. Extraction inventories, per
  declared type, its declared methods, properties, fields, and events. **Excluded** (ratified):
  property/event accessors (they fold into the property/event, matching the §4.5 member-use
  normalization), constructors including static constructors, operators and conversions,
  finalizers, indexers, and any compiler-generated or implicitly-declared member. **Enum and
  delegate types contribute no inventory** — an enum's fields are its values (an enum-value read
  stays a recorded field *use*, §4.5, untouched by this section) and a delegate's `Invoke`/
  `BeginInvoke` are runtime plumbing, not authored surface. **External (metadata) types carry no
  inventory** — the member axis is solution-declared-only, exactly as external types carry a
  shallow type hierarchy (§5.2).
- **An empty member subject fails the rule** by default, the member analog of the empty-type
  subject (§4.1), with its own pinned message *"The subject selection matched no
  solution-declared members."* (a selection that matches types none of whose members survive the
  kind filter is the ordinary way to hit it).
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
| `arch.Member(typeof(DateTime), nameof(DateTime.Now))` / `arch.Member(() => DateTime.Now)` | "`DateTime.Now`" — member leaf, target-only (§4.5); parens iff method: "`Task.Wait()`" (`arch.Member<Task>(t => t.Wait())`) |

### 5.2 Adjectives (reduced relative clauses)

| Combinator | Fragment |
|---|---|
| `.InNamespace("MyApp.Web.*")` | "in `MyApp.Web.*`" |
| `.OfKind(TypeKind.Interface)` | noun substitution: "interfaces" (kinds: Class, Interface, Struct, Enum, Delegate; records via escape hatch in v1) |
| `.WithSuffix("Controller")` | "named `*Controller`" |
| `.WithPrefix("Legacy")` | "named `Legacy*`" |
| `.WithNameMatching("*Repo*")` | "whose name matches `*Repo*`" |
| `.Implementing(typeof(IHandler<>))` | "implementing `IHandler<T>`" |
| `.DerivedFrom(typeof(ControllerBase))` | "derived from `ControllerBase`" |
| `.AttributedWith(typeof(ApiControllerAttribute))` | "attributed with `[ApiController]`" — `Attribute` suffix stripped, bracketed |
| `.Except(selection)` | ", except {ref}" — canonicalized to sentence-final (§6) |
| `.Where(pred, description:)` | description verbatim — canonicalized to sentence-final (§6) |

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
name). `AttributedWith` is **declared attributes only** — no inheritance. External (metadata)
types carry a **shallow** hierarchy: extraction records their identity but not their
bases/interfaces/attributes, so the hierarchy adjectives never match an external type (a
documented boundary; pattern and name adjectives still work on externals). The same three
matchers back the `MustImplement` / `MustDeriveFrom` / `MustBeAttributedWith` constraint verbs.

**Generic sugar.** Each hierarchy adjective carries a thin generic twin —
`Implementing<T>()` ≡ `Implementing(typeof(T))`, likewise `DerivedFrom<T>()` and
`AttributedWith<T>()` (the attribute twin constrained `where T : Attribute`) — that desugars to
the identical adjective, changing nothing in the model. `arch.Type<X>()` ≡ `arch.Type(typeof(X))`
is the same idea on the noun. An **open** generic has no type-argument form, so it stays `typeof`
(`Implementing(typeof(IHandler<>))`); the sugar is for the closed/non-generic single-type case.

### 5.3 Modal constraints (verb phrases)

| Combinator | Fragment |
|---|---|
| `.MustNotReference(target, ...)` | "must not reference {list}" |
| `.MustOnlyReference(target, ...)` | "must reference only {list} (external packages are not constrained by this rule)" |
| `.MustNotBeReferencedBy(source, ...)` | "must not be referenced by {list}" |
| `.MustOnlyBeReferencedBy(source, ...)` | "must be referenced only by {list}" |
| `.MustNotUse(member, ...)` | "must not use {list}" — member targets (§4.5) |
| `.MustNotConstruct(target, ...)` | "must not construct {list}" — selection/type targets; the DI-construction verb (§3.3) |
| `.MustNotInject(target, ...)` | "must not inject {list}" — selection/type targets; the captive-dependency verb (§3.3, §4.7). Never warns: an empty `Registered` operand means no such registrations exist — the win condition, the §4.1 bare-`typeof` precedent |
| `.MustResideInNamespace(glob)` | "must reside in `{glob}`" |
| `.MustHaveSuffix("Handler")` | "must be named `*Handler`" |
| `.MustHavePrefix("I")` | "must be named `I*`" |
| `.MustHaveNameMatching(glob)` | "must have a name matching `{glob}`" |
| `.MustImplement(type)` | "must implement `{X}`" |
| `.MustDeriveFrom(type)` | "must derive from `{X}`" |
| `.MustBeAttributedWith(type)` | "must be attributed with `[{X}]`" |
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

### 5.4 Posture verbs and options

| Member | Notes |
|---|---|
| `.Enforce(constraint)` | the law; violation = red |
| `.Migrate(from: prose, to: constraint)` | `from` is descriptive prose (the OLD pattern); `to` is the checkable target constraint |
| `.Freeze(selection)` | scope statement; desugars per §7 |
| `.Baseline(path)` | Migrate **and** Freeze; ratcheted grandfather store |
| `.WhileYoureThere(MigrationPolicy)` | `MigrateIfSmall` (default) \| `AlwaysMigrate` \| `NeverExpand` |
| `.BoundaryOnlyVia(params Type[])` | the sanctioned surface; omit entirely for a hermetic freeze |
| `.Dragons(prose)` / `.DragonsDoc(path)` | load-bearing-weirdness prose / linked long-form doc |

### 5.5 Trailers

| Member | Notes |
|---|---|
| `.Because(prose)` | **required** on every rule and frozen scope (§8 item 3) |
| `.Fix(prose)` | optional; for Freeze containment it is auto-derived from `BoundaryOnlyVia` ("use `IBillingFacade`") and deliberately not author-overridable — `IFrozenScope` carries no `.Fix` |

### 5.6 Escape hatches

Predicate input contract (`ITypeInfo`): `Name`, `Namespace`, `Kind`, `ProjectName`,
`Accessibility`, `IsSealed`, `IsStatic`, `IsAbstract`, `IsRecord`, `FilePaths` (declaration
file paths; empty for external types), attributes, base type, implemented interfaces. The
contract grows additively as extraction learns new facts. The flags carry C# declaration
semantics — a static class is neither sealed nor abstract, interfaces are abstract,
structs/enums/delegates are sealed — and `IsRecord` is how record rules are written in v1
(§5.2).

Member-predicate input contract (`IMemberInfo`, the input to a member `.Where`/`.Must`, §4.6):
`Name`, `Kind` (`MemberKind`: Method / Property / Field / Event), `DeclaringType` (an
`ITypeInfo` — the type-side contract, reused so a member predicate can reach its declaring
type's facts), `Accessibility`, `IsStatic`, `IsAbstract`, `IsVirtual`, `IsAsync`,
`ReturnTypeFullName` (methods; `System.Void` for a void method; null otherwise),
`MemberTypeFullName` (the property/field/event type; null for methods), and `FilePaths`
(declaration file paths). The flags carry the same C# declaration semantics as the member axis
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

### 5.7 Member vocabulary (member subjects, §4.6)

**Projections** (mint a `MemberSelection`; the fragment is the subject head, §6):

| Combinator | Fragment (subject head) |
|---|---|
| `.Members` | "members of {ref}" |
| `.Methods` | "methods of {ref}" — the selection is a `MethodSelection`, so `.Returning` is available |
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
| `.Must(pred, description:)` | "must {description}" — `pred` is `Func<IMemberInfo, bool>` (§5.6) |

The naming verbs reuse the type-side "must be named" / "must have a name matching" strings
verbatim; `MustBePrivate` and `MustBeVirtual` are new member-only vocabulary. The whole member
vocabulary shipped complete per the admission rule (§10): reification, pinned fragments, and
the checker semantics (member inventory, the shape/naming evaluators, the ratchet) landed
together.

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
- **Colliding simple names**: when two targets in one sentence share a simple name, both are
  qualified with the minimal distinguishing trailing namespace segments
  ("`Billing.Order` or `Sales.Order`"). Pinned rule.
- **Member references** (§4.5) render as the backticked declaring type dot member —
  "`DateTime.Now`" — with `()` appended iff the member is a method ("`Task.Wait()`"; never
  a signature). Generic anchors use declared type-parameter names ("`Task<TResult>.Result`").
  Colliding declaring-type simple names widen by the same minimal-trailing-segments rule —
  including when the member names differ ("`Billing.Order.Total` or `Sales.Order.Refresh()`"),
  because the reader must see they are different `Order`s.
- **Member subjects** (§4.6) assemble as `{kind-plural} of {selection-reference}` +
  the inline adjectives in authoring order + the sentence-final `Where`. The kind-plural is the
  projection head ("methods", "properties", …); the `{selection-reference}` is the underlying
  type selection rendered in *reference* position (the same "types in `MyApp.Web.*`" / "the Web
  layer" form a target list uses), so a member subject reads "methods of types in `MyApp.Web.*`".
  Inline member adjectives (`WithSuffix`/`WithPrefix`/`WithNameMatching`/`Returning`) append in
  the order written; the member `Where` canonicalizes sentence-final exactly like the type-side
  `Where`. The flagship: `web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")` (where
  `web` is `MyApp.Web.*`) → *"Methods of types in `MyApp.Web.*` returning `Task` must be named
  `*Async`."*
- Posture voices consume these same fragments: Enforce renders as law; Migrate renders the
  counter-prior paragraph (slots: from-prose, to-sentence, policy, baseline burndown); Freeze
  renders dragons + sanctioned surface. Full paragraph templates are pinned by the renderer's
  tests; the grammar carries every slot they need.

## 7. Freeze desugaring (the single rule model, constructively)

`arch.Scope(id).Freeze(sel).BoundaryOnlyVia(F).Baseline(p)` reifies to ordinary rule nodes:

- **`{id}/containment`** — internally `sel.Except(F).MustOnlyBeReferencedBy(sel ∪ F)`.
  (Model-level union exists internally; there is no surface union combinator in v1.) The
  formula holds whether the facade types live inside or outside the frozen selection.
  **`.Baseline(p)` grandfathers existing inbound references** with the same ratchet semantics
  as Migrate — day-one adoption on a real legacy codebase must not be a wall of red; only
  *new* references into the scope are violations ("nothing **new** may reference the frozen
  scope"). Burndown shows up in `loadbearing status` for free. An omitted
  `.Baseline(p)` fills the conventional default `arch/baselines/{scope-id}/containment.json` at
  build time, exactly like the Migrate default (§4.4) — the containment baseline path is never
  null post-build.
- **`{id}/tripwire`** — warning-severity, diff-aware touch check. With
  `loadbearing check --diff-base <ref>`, each changed file that declares a type in the frozen
  selection yields one warning ("does the task actually require editing dragon territory?"); the
  rule itself passes and warnings never affect the exit code. Without diff context the rule is
  skipped, with a pointer at `--diff-base`. It carries the frozen selection (not the boundary or a
  baseline) so it can map changed files to frozen types.
- Scope children occupy the rule-ID namespace: duplicate detection runs over the
  **post-desugar** ID set, and a declared ID may not extend a scope ID — `{scope-id}/…` is
  reserved. Reserved suffixes today: `containment`, `tripwire`.
- Omitting `BoundaryOnlyVia` = hermetic freeze (nothing outside may reference the scope).
  Because omission is legal, the verb deliberately stays plain `params` (not `(first, more)`)
  so that a zero-arg call reaches spec-build validation and gets the designed hint — "omit
  the call for a hermetic freeze" (§8 item 8) — instead of an opaque compiler error. It grows
  **no** generic twin (decided against): a boundary is a variadic facade-plus-
  implementation list, which has no type-argument form; a single facade type is
  `BoundaryOnlyVia(typeof(IFacade))`.
- Nested/overlapping freezes compose as independent conjuncts; there is no scope precedence.
- **Practical note** (also for the derive prompt): `BoundaryOnlyVia` usually needs
  the facade *implementation* type(s) listed alongside the interface, or the composition
  root's DI registration of the concrete facade goes red on day one:
  `BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))`.

## 8. Spec-build validation catalog (all errors reported at once)

1. Duplicate ID over the **post-desugar** set (rules + scopes + generated children), across
   all spec classes; a declared ID may not extend a scope ID.
2. Dangling anchor — `Rule()`/`Scope()` without a posture verb.
3. Missing `Because` on any rule or frozen scope.
4. Missing both `Dragons` and `DragonsDoc` on a frozen scope.
5. Blank/whitespace prose anywhere; prose fields are single-line (no `\r`/`\n`, no leading
   markdown-structural characters — long-form prose links out via `DragonsDoc`).
6. Repeated trailer/option (`Because` twice, two `Baseline`s, …).
7. Malformed ID — must match `^[a-z0-9-]+(/[a-z0-9-]+)*$` (convention: `area/rule-name`).
8. `BoundaryOnlyVia()` with zero types (omit the call for a hermetic freeze).
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
    and to layer globs (reported spec-wide, named by layer).
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
    given `.Freeze` more than once. The stage machine (§3.2) makes the *fluent* double-call
    uncompilable: the posture verbs live only on `IRuleBuilder`/`IScopeBuilder`, and the first call
    hands back a stage type without them. But those builders are mutable, so a **stored** builder
    reference (`var b = arch.Rule(id); b.Enforce(...); b.Migrate(...);`) can call a posture verb
    twice, and the second silently overwrites the first — the model keeps only the last posture.
    This item catches that stored-reference re-call: the count rides on the registration
    (`RuleRegistration.PostureCount` / `ScopeRegistration.FreezeCount`) and a count > 1 is the error.
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

Item 5 also reaches the member escape-hatch descriptions: a blank or multi-line member `Where`
(`Func<IMemberInfo,bool>`) or member `Must` description is caught by the same prose walk,
extended to descend through a `MemberConstraint`'s member subject and verb (§4.6).

Enforced at compile time instead (no catalog entry): zero-target dependency verbs,
zero-member `MustNotUse`, and zero-glob layers (`(first, more)` signatures); missing
escape-hatch descriptions (required parameters); trailers before postures (absent from stage
types); adjectives or modal verbs on a `Member` (a leaf outside the selection hierarchy,
§3.2); `.Returning` off any projection but `.Methods` (it lives only on `MethodSelection`,
§3.2, §4.6). Several member-anchor-expression mistakes are refused by the C# compiler outright,
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
  phrases (`InNamespace`, `WithSuffix`, `OfKind`).
- **Constraints**: `Must[Not]` + verb phrase; polarity lexical; the noun rides along where a
  bare preposition would be ambiguous (`MustResideInNamespace`). The member-access verb is
  *use* (`MustNotUse`), glossary-pinned as *"use = a source-level member access"* — the
  member analog of "reference" (§4.1), chosen over "call"/"access" because it covers
  invocations, reads, writes, and subscriptions in one word. The constructor-ban verb is
  *construct* (`MustNotConstruct`), glossary-pinned as *"construct = a source-level object
  creation (`new`, including target-typed `new()`)"* — the third axis beside reference and use.
  The injection-ban verb is *inject* (`MustNotInject`), glossary-pinned as *"inject = a
  source-level constructor-parameter dependency (primary constructors included)"* — the fourth
  axis (§4.7). The glossary line composes from these per-axis clauses (reference always; use iff a
  member-target rule; construct iff a ctor rule; inject iff an injection rule) joined with "; "
  and closed by the drill-down tail, so each axis
  gates independently and a spec without a given axis renders byte-identically to before that axis
  existed (§4.1/§4.5's byte-identical-without-it discipline, generalized). A `Registered` noun
  anywhere in a rule — subject or operand — additionally gates its own glossary line on the same
  byte-identical-without-it terms: *"registered = named in a source-level container registration
  (`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/`AddDbContext`/
  `AddHttpClient<TClient>`); registrations made by assembly scanning, factory internals, or
  framework defaults are not seen."*
- **Posture verbs**: imperative (`Enforce`, `Migrate`, `Freeze`). **Options**: nouns
  (`Baseline`) or deliberate idiom (`WhileYoureThere` — it names the boy-scout rule).
  **Trailers**: conjunctions (`Because`) / nouns (`Fix`).
- `(first, params more)` signatures wherever an empty list would be meaningless.
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
`Implementing`/`DerivedFrom`/`AttributedWith` twins, §4.5, §5.2–§5.3). Still growth on the
member axis: the
`MustOnlyUse` / `MustNotBeUsedBy` verb twins; member-granular *source* attribution on the
dependency verbs (today a violation names the using *type*, not the using member); string-FQN
member anchoring — now the remaining *anchoring* residue, the escape hatch for a member that
neither `typeof` + `nameof` nor an expression lambda can name (a member on a type the spec
project cannot reference); indexer/operator bans (the syntax-walk boundary moves
deliberately, §4.5); and subject-side member *shape* adjectives (`.Methods.ThatAreVirtual()`, …)
— the member constraint-side verbs and the `IMemberInfo` flags shipped (§5.7, §5.6), only the
adjective position remains, exactly mirroring the type-side gap below.

On the injection axis (§4.7): the `MustOnlyInject` / `MustNotBeInjectedBy` twins; property- and
method-injection edges (constructor parameters are the recorded form); recognition growth beyond
the pinned call table — keyed services, `ServiceDescriptor`/`TryAddEnumerable`, assembly-scanning
registrars — the table is the fence, everything outside it the documented honesty boundary;
lifetime facts on `ITypeInfo` (registration membership stays model-side, resolved at evaluation);
and `graph` registration data.

Elsewhere: `MustBeAcyclic()` on namespace slices (ArchUnit `slices()` analog); surface union
`arch.AnyOf(...)` (also the future multi-type noun); a layered-architecture macro (deferred:
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
            .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
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
closed vocabulary; escape hatches are `Where`/`Must`; Freeze shows facade-implementation
listing and the grandfathering baseline. The founding dogfood rule was one line —
`arch.Project("Zphil.LoadBearing").MustNotReference(arch.Project("Zphil.LoadBearing.Roslyn"))` —
since grown into the three-posture self-spec (`LoadBearingArchSpec`: that Enforce rule plus a
Migrate and a Freeze).
