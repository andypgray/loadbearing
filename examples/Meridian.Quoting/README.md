# Meridian.Quoting

Meridian.Quoting is a greenfield customer-quoting service, the strangler step after the Meridian monolith migrated its quoting controllers. It looks like the incumbents' clean-architecture READMEs on purpose: the nine rules below are all `Enforce`, and Enforce-only layering and naming are exactly what ArchUnitNET already shows. The difference is the generated agent context beside the spec, not the rules. Same fiction as [`../Meridian/`](../Meridian/): one freight-forwarding company, one subsystem it has already pulled clean.

## The subsystem

Four layers run one direction. `Domain` holds the quote and rate-card model; `Application` holds the use cases and the ports they depend on; `Infrastructure` implements those ports; `Api` is the composition root and the HTTP surface. Nine rules hold that shape, every one `Enforce`, zero baselines: the subsystem was born conforming.

Writes go through a command bus that opens a unit of work only around a handler marked `[Transactional]`. `RequestQuoteHandler` writes twice: it reserves a quote number, then persists the quote. An unmarked command handler would commit each write on its own, so a failure between the two burns a quote number with no quote to show for it.

Time enters through `IClock`. `SystemClock` is the single adapter that reads the machine clock, so a quote's 14-day validity window is computed from an injected instant and can be checked at a fixed moment in a test.

## The block beside the rules

Three rules from [arch/Meridian.Quoting.ArchSpec/QuotingArchSpec.cs](arch/Meridian.Quoting.ArchSpec/QuotingArchSpec.cs):

```csharp
        arch.Rule("layering/application-boundaries")
            .Enforce(application.MustOnlyReference(application, domain))
            .Because("Use cases depend on the Domain and on abstractions they own, never on a concrete adapter; keeping Infrastructure and Api out of Application is what lets persistence and transport be swapped or faked in a test.")
            .Fix("Depend on a port (an interface in Domain or Application) instead of the concrete type; wire the implementation in the Api composition root.");

        arch.Rule("handlers/transactional")
            .Enforce(arch.Types.Implementing(typeof(ICommandHandler<>)).MustBeAttributedWith<TransactionalAttribute>())
            .Because("Every command here mutates the store, and the command bus opens a unit of work only around a handler marked `[Transactional]`; an unmarked command handler would commit each write on its own and leave a half-written quote if it failed midway.")
            .Fix("Add `[Transactional]` to the command handler (see RequestQuoteHandler); if the work is read-only, make it a query handler instead.");

        arch.Rule("time/injected-clock")
            .Enforce(arch.Types.InNamespace("Meridian.Quoting.*")
                         .Except(arch.Types.WithNameMatching("SystemClock"))
                         .MustNotUse(
                             () => DateTime.Now,
                             () => DateTime.UtcNow))
            .Because("A quote's validity window is computed from the current instant; read straight from the wall clock it cannot be tested at a fixed moment, so time enters through IClock and SystemClock is the one adapter that reads the machine clock.")
            .Fix("Take IClock in the constructor and read clock.UtcNow; see SystemClock for the single sanctioned wall-clock read.");
```

The exact lines those three produce in [AGENTS.md](AGENTS.md):

```markdown
- `layering/application-boundaries` — The Application layer must reference only the Application layer or the Domain layer (external packages are not constrained by this rule). Use cases depend on the Domain and on abstractions they own, never on a concrete adapter; keeping Infrastructure and Api out of Application is what lets persistence and transport be swapped or faked in a test.
- `handlers/transactional` — Types implementing `ICommandHandler<TCommand>` must be attributed with `[Transactional]`. Every command here mutates the store, and the command bus opens a unit of work only around a handler marked `[Transactional]`; an unmarked command handler would commit each write on its own and leave a half-written quote if it failed midway.
- `time/injected-clock` — Types in `Meridian.Quoting.*`, except types whose name matches `SystemClock` must not use `DateTime.Now` or `DateTime.UtcNow`. A quote's validity window is computed from the current instant; read straight from the wall clock it cannot be tested at a fixed moment, so time enters through IClock and SystemClock is the one adapter that reads the machine clock.
```

Each rendered rule opens with a sentence generated from the fluent call, then carries its `Because` string verbatim. `loadbearing render` writes the whole block into `AGENTS.md`; CI re-runs `render` on every push and fails on any diff, so the block an agent reads is provably the spec the build enforces. `layering/application-boundaries` renders its own honesty caveat: `MustOnlyReference` bounds the Application layer against the other layers, and the parenthetical `(external packages are not constrained by this rule)` says so, because a use case still depends on framework types.

## Every rule as a named test

The `Zphil.LoadBearing.Xunit` adapter turns each rule into its own xUnit test with the rule ID as the display name. `dotnet test` on the adapter project:

```text
  Passed naming/controllers(ruleId: "naming/controllers") [19 s]
  Passed time/injected-clock(ruleId: "time/injected-clock") [< 1 ms]
  Passed handlers/naming(ruleId: "handlers/naming") [< 1 ms]
  Passed handlers/transactional(ruleId: "handlers/transactional") [< 1 ms]
  Passed naming/interfaces(ruleId: "naming/interfaces") [< 1 ms]
  Passed persistence/repository-ports(ruleId: "persistence/repository-ports") [< 1 ms]
  Passed layering/application-boundaries(ruleId: "layering/application-boundaries") [< 1 ms]
  Passed layering/domain-independent(ruleId: "layering/domain-independent") [< 1 ms]
  Passed messaging/immutable-messages(ruleId: "messaging/immutable-messages") [< 1 ms]

Test Run Successful.
Total tests: 9
     Passed: 9
 Total time: 24.7687 Seconds
```

The one-time MSBuild workspace load bills to whichever case runs first (here `naming/controllers`, at 19 s); the other eight read the shared verdict in under a millisecond.

Delete the `[Transactional]` attribute from `RequestQuoteHandler` and the test named `handlers/transactional` is the one that fails, with the message:

```text
FAIL handlers/transactional — Types implementing `ICommandHandler<TCommand>` must be attributed with `[Transactional]`.
  because: Every command here mutates the store, and the command bus opens a unit of work only around a handler marked `[Transactional]`; an unmarked command handler would commit each write on its own and leave a half-written quote if it failed midway.
  fix: Add `[Transactional]` to the command handler (see RequestQuoteHandler); if the work is read-only, make it a query handler instead.
  src/Meridian.Quoting.Application/Handlers/RequestQuoteHandler.cs:13 — Meridian.Quoting.Application.Handlers.RequestQuoteHandler
```

That text is byte-identical to the CLI's FAIL block by construction: the adapter and the CLI share one renderer, and a product test pins them equal. Restore the attribute and the test is green.

## One edit, four message components

Layers here are namespace patterns (`Meridian.Quoting.Application.*`), not project references. You cannot break the boundary with a direct `Application -> Infrastructure` project reference: Infrastructure already references Application, and MSBuild rejects the cycle. A namespace violation compiles anyway, and the checker catches what the project graph cannot see.

The agent-failure story is an agent that wires the concrete repository instead of the port. Drop one file into the Api project (which already references Infrastructure, so it compiles) declaring the Application namespace and newing up `InMemoryQuoteRepository`. The layer is the namespace, so the type lands in Application while the file sits in Api, and `check` goes red:

```text
FAIL layering/application-boundaries — The Application layer must reference only the Application layer or the Domain layer (external packages are not constrained by this rule).
  because: Use cases depend on the Domain and on abstractions they own, never on a concrete adapter; keeping Infrastructure and Api out of Application is what lets persistence and transport be swapped or faked in a test.
  fix: Depend on a port (an interface in Domain or Application) instead of the concrete type; wire the implementation in the Api composition root.
  src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs:12 — Meridian.Quoting.Application.Handlers.ExpireQuotesHandler references Meridian.Quoting.Infrastructure.Persistence.InMemoryDatabase
  src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs:16 — Meridian.Quoting.Application.Handlers.ExpireQuotesHandler references Meridian.Quoting.Infrastructure.Persistence.InMemoryQuoteRepository
  src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs:17 — Meridian.Quoting.Application.Handlers.ExpireQuotesHandler references Meridian.Quoting.Infrastructure.Persistence.InMemoryQuoteRepository
```

`check` exits 1 and the summary reads `Checked 9 rules: 8 passed, 1 failed, 0 skipped (2 violations, 0 warnings)`: exactly one rule red. The block carries the ID, the reason, the fix, and the exact `file:line`, and no `grandfathered:` line, because this is day-one law with nothing baselined. That is the contrast with the ratcheted blocks in [`../Meridian/`](../Meridian/), where every current violation sits on a counted baseline. Delete the file, rebuild, and `check` is green again.

## Run it yourself

LoadBearing ships as a .NET global tool (it needs the .NET 10 runtime):

```bash
dotnet tool install -g Zphil.LoadBearing.Cli
```

The test leg is a package too: a consumer adds `Zphil.LoadBearing.Xunit` to its own test project and derives one class per spec, as `tests/Meridian.Quoting.ArchTests` does here. From a checkout of this repository, build the subsystem and check it:

```bash
dotnet build examples/Meridian.Quoting/Meridian.Quoting.slnx
loadbearing check examples/Meridian.Quoting/Meridian.Quoting.slnx
```

`check` exits 0: every rule holds. `loadbearing render` regenerates the `AGENTS.md` block from the spec, and `dotnet test examples/Meridian.Quoting/tests/Meridian.Quoting.ArchTests/Meridian.Quoting.ArchTests.csproj` runs the nine named rule tests.

## From here

[`../Meridian/`](../Meridian/) states the same law against a codebase still working toward it. What that monolith ratchets as `Migrate` debt, this subsystem enforces from day one. Its `data-access/no-inline-sql` target is this pair, `layering/application-boundaries` and `persistence/repository-ports`, which together keep data access out of the request path behind a repository; its `time/inject-clock` target is `time/injected-clock` here, the same ban on `DateTime.Now` and `DateTime.UtcNow` with no baseline to grandfather. Two rules match by name across both specs, domain independence and controller naming, `Enforce` in each. Meridian is that target state met by a codebase that has not reached it yet.
