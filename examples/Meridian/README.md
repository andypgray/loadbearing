# Meridian

Meridian is a freight-forwarding monolith caught mid-migration. It is the LoadBearing example where all three postures meet a codebase that does not yet match its own target: the law it already keeps, the debt it is working off, and the one module nobody should touch.

An agent dropped into a repository reads the files around its task and copies what it finds. In Meridian most of what it finds is the pattern being retired. The spec below is how the build tells the agent which patterns are the target and which are grandfathered, in words the agent reads before it writes and the same rules the build enforces after.

## The app

Meridian handles bookings, rating, customs, invoicing, and dispatch. Eight controllers sit at the front of it. Six of them (Shipments, Rates, Invoices, Customs, Drivers, and Manifests) open a `SqlConnection`, run inline SQL, and read the wall clock straight from `DateTime.Now` or `DateTime.UtcNow` for cutoffs, demurrage days, and ETA stamps. Two of them (Bookings and Quotes) have already moved to constructor-injected repositories and an `IClock`. That six-to-two split is the whole point: the majority pattern is the one being retired, which is exactly the arrangement an agent reads as house style.

Behind `IClearanceGateway`, the `Meridian.Clearance` module validates container numbers. Its `ContainerCheckDigit` computes the ISO 6346 check digit, the calculation that decides whether `CSQU3054383` is a real container number or a typo. That module is frozen.

Twenty current violations are grandfathered: twelve inline-SQL references, seven wall-clock reads, and one reach into the frozen module. The app builds and runs; the whole thing reads in about ten minutes. It is shaped like the systems the tool is built for: long-lived, business-critical, too important to rewrite on a whim.

## The spec

The architecture is six statements of ordinary C# in [arch/Meridian.ArchSpec/MeridianArchSpec.cs](arch/Meridian.ArchSpec/MeridianArchSpec.cs). Each one carries a posture, a reason, and a fix.

| Rule | Posture | What it says |
|---|---|---|
| `layering/domain-independent` | Enforce | Domain must not reference Web |
| `naming/controllers` | Enforce | controllers are named `*Controller` |
| `data-access/no-inline-sql` | Migrate | controllers must not open `SqlConnection` |
| `time/inject-clock` | Migrate | Web must not read `DateTime.Now` / `UtcNow` |
| `clearance/engine` | Freeze | reach the module only via `IClearanceGateway` |

Two rules are already true, so they are law (`Enforce`). Two describe debt with a target, so they ratchet (`Migrate`): the current violations are grandfathered, and anything new is red. One walls off a module with no target state (`Freeze`).

## What the agent reads

`loadbearing render` writes this block into [AGENTS.md](AGENTS.md) from the spec. CI re-renders on every push and fails on any diff, so the context an agent reads is provably the spec the build enforces.

```markdown
### Rules
- `layering/domain-independent` — The Domain layer must not reference the Web layer. Domain holds the booking and rate model the rest of the system depends on; it must not reach up into the web tier.
- `naming/controllers` — Types in `Meridian.Web.Controllers.*` must be named `*Controller`. Request handlers are found by their `*Controller` name — by routing and by agents reading the code; keep the convention total.

### Migrations
- `data-access/no-inline-sql` — Most existing code here follows the OLD pattern: Controllers open SqlConnection and run inline SQL directly. That is grandfathered debt, not house style. New code must follow: Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`. Data access behind a repository can be tested and swapped; SQL in the request path cannot. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.
- `time/inject-clock` — Most existing code here follows the OLD pattern: Code reads the ambient clock directly. That is grandfathered debt, not house style. New code must follow: Types in the Web layer, except types whose name matches `SystemClock` must not use `DateTime.Now` or `DateTime.UtcNow`. Cutoffs, demurrage, and ETA stamps read from the wall clock cannot be tested at a fixed instant; an injected IClock makes the moment an input. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.

### Frozen scopes
- `clearance/engine` — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway` must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`. The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it. Sanctioned surface: `IClearanceGateway`, `ClearanceGateway`.
```

## Three ways an agent goes wrong here

Each posture exists to defeat one agent failure mode. The blocks below are real `loadbearing check` output.

### The statistical prior

Six of the eight controllers open a `SqlConnection`. An agent reading them infers that direct SQL is how Meridian does data access, and writes its next controller the same way. The rendered Migration text counters that prior in the agent's context ("that is grandfathered debt, not house style. New code must follow..."), and the ratchet enforces it.

Add an inline-SQL method to `BookingsController`, one of the two migrated controllers, and `check` goes red on the new site while the twelve grandfathered ones stay quiet:

```text
FAIL data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  because: Data access behind a repository can be tested and swapped; SQL in the request path cannot.
  fix: Move the SQL into a repository; see BookingRepository.
  src/Meridian.Web/Controllers/BookingsController.cs:77 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/BookingsController.cs:78 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)
```

The message carries the rule ID, the reason, the fix, and the exact `file:line`. Revert the method and `check` is green again. New code in the old pattern is red; the existing debt is not.

### The helpful refactor

`ContainerNumberValidator` is public, so an agent tidying the code can call it directly and delete an "unnecessary" hop through `IClearanceGateway`. The frozen scope's containment rule stops that: the only sanctioned way into `Meridian.Clearance` is the gateway. Reach past it and the reference is red, with the fix naming the surface to use:

```text
FAIL clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway` must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  because: The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it.
  fix: use `IClearanceGateway`
  src/Meridian.Web/Controllers/BookingsController.cs:75 — Meridian.Web.Controllers.BookingsController references Meridian.Clearance.ContainerNumberValidator
  grandfathered: 1 (baselined; run 'loadbearing status' for burndown)
```

One reach into the module already exists (`CustomsController` news up the validator), and it is grandfathered on the record. Every new one is red. The dragons stay contained.

### Misreading load-bearing weirdness

The ISO 6346 check-digit table looks broken. It assigns A=10, B=12, C=13, and skips a value every so often, so K=21 is followed by L=23. An agent that "corrects" the gap to make the table contiguous breaks the check digit for every real container number in the system. This is the weirdness a `Freeze` scope documents rather than defends against, because agents still have to call into the code. `render` drops this card into the module's own directory, next to the file:

```markdown
## Frozen scope `clearance/engine`

This directory holds the frozen `clearance/engine` scope. Here be dragons — do not spread references into it.

Dragons: ISO 6346 check digit: the letter-value table skips every multiple of 11 (A=10, B=12 … U=32); the gaps are load-bearing — linearizing the table breaks every real container number. Call in only through IClearanceGateway.
```

The gaps are the standard: the letter values skip every multiple of 11 (11, 22, 33), which is why the table is not contiguous. The prose says what the code does, which part is load-bearing, and how to interact with it.

## The burndown

Because the Migrate and Freeze baselines are counted, `loadbearing status` reports what is left to work off:

```text
pass layering/domain-independent
pass naming/controllers
pass data-access/no-inline-sql (migrate) — 12 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass time/inject-clock (migrate) — 7 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass clearance/engine/containment (freeze) — 1 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
skip clearance/engine/tripwire (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'
Checked 6 rules: 5 passed, 0 failed, 1 skipped. Burndown: 20 grandfathered remaining, 0 fixed awaiting acceptance.
```

Move a controller onto a repository and its grandfathered count drops. When a Migrate rule reaches zero, the tool suggests promoting it to `Enforce`.

## Run it yourself

LoadBearing ships as a .NET global tool (it needs the .NET 10 runtime):

```bash
dotnet tool install -g Zphil.LoadBearing.Cli
```

From a checkout of this repository, build the example and check it:

```bash
dotnet build examples/Meridian/Meridian.slnx
loadbearing check examples/Meridian/Meridian.slnx
```

`check` exits 0 here, because every current violation is on the baseline. `loadbearing status` prints the burndown above, and `loadbearing render` regenerates the `AGENTS.md` block from the spec. Introduce one of the violations from this page and `check` exits 1 with the message shown.

## From here

The clean-architecture on-ramp, [`Meridian.Quoting`](../Meridian.Quoting/), is a greenfield subsystem that meets these ratchets' target state: the same rules as `Enforce` law from day one. Meridian is that target state met by a codebase that has not reached it yet: same law, stated honestly against the debt.
