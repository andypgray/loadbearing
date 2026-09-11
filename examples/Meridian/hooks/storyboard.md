# Agent-loop storyboard: the rule that corrects the agent

A coding agent, working a routine task, writes something the architecture rules do not allow, and
the rule reaches it before the turn ends, so the agent self-corrects before the work is handed
back. This page walks that loop beat by beat, then walks the other half of it: an edit the rules
allow, into code the spec has marked as dangerous, where the hook informs rather than blocks.

Every fenced block below is real output, captured either from the hook's own channel in the
session transcript or from the same check run by hand against Meridian. Nothing here is mocked,
and [Reproduce it](#reproduce-it) walks the same loop at the command line.

**How it was captured (2026-09-09).** Two `claude -p` runs against this repository with the
`Stop` and `SubagentStop` entries below wired and nothing else, both given the Beat 1 brief and
one steer: follow the prevailing data-access style of the other controllers. Beats 2 to 4 are the
Haiku 4.5 run; Beat 5 is the Opus run. What neither run did is written into Beat 2, because it is
the more interesting half of the result.

## The files in this directory

The repository does not commit `.claude/` (it is a local dev aid), so the hook config lives here
as a paste-in rather than a live `.claude/` folder. Lift these into your own repo:

| File | What it is |
|---|---|
| [`arch-hook.ps1`](arch-hook.ps1) | The PowerShell wrapper: runs `loadbearing check` and maps its exit code to Claude Code's blocking convention. |
| [`arch-hook.sh`](arch-hook.sh) | The POSIX variant, same contract. |
| [`settings.snippet.json`](settings.snippet.json) | Paste into your `.claude/settings.json`: a `Stop` entry and a `SubagentStop` twin, plus a deny rule that keeps `baseline` a human decision. |

The wrappers are the Meridian instantiation of LoadBearing's agent-hook recipe, filled in with
Meridian's solution, spec assembly, and diff base. The one thing the wrapper exists to get right
is the exit code:
a clean check returns 0 and the turn ends; a red rule returns 2, which is how a Claude Code hook
refuses a stop, carrying the violation report on stderr so the agent reads it and fixes the code;
LoadBearing's own errors return 1, a config problem the user sees rather than an architecture
violation the agent is told to fix. Returning 0 is not the same as saying nothing: where the check
warned, the wrapper hands the report back as hook context, which is Beat 5.

## Beat 1: the task

> Add a lookup endpoint to `BookingsController` that returns a booking by its reference, or 404
> when there is no such booking.

`BookingsController` is one of the two controllers already migrated to a repository and an
injected clock. The other six open a `SqlConnection` and run inline SQL in the request path.

## Beat 2: what the agent got right, and what it did not

Six of the eight controllers do data access with inline SQL, so an agent reading the codebase for
house style has a majority to copy. Neither run copied it. Both read the committed
[`AGENTS.md`](../AGENTS.md) block, which says in the spec's own words that the twelve inline-SQL
sites are grandfathered debt and that new code must not add to them, and both wrote the lookup as
a call through the already-injected `IBookingRepository`:

```csharp
[HttpGet("{reference}")]
public async Task<IActionResult> GetByReference(string reference)
{
    Booking? booking = await bookings.Get(reference);
    if (booking is null) return NotFound();

    return Ok(booking);
}
```

That is the rendered block doing its job: it steered the first attempt away from the pattern
Meridian is retiring, with no check involved. What the block did not say is what to call the
method. `GetByReference` returns a `Task`, `naming/async-suffix` is a second ratchet with thirteen
sites already on the record, and `IBookingRepository.Get`, one line above in the same file, is one
of the thirteen. So the agent copied a name that is grandfathered rather than allowed, and this
compiles, runs, and is new debt.

## Beat 3: the turn ends, and the hook refuses the stop

The agent finished and tried to hand the work back. The `Stop` hook ran `check` over the working
tree, the ratchet went red on the one new site, and the wrapper exited 2 with this report on
stderr:

```text
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types derived from `ControllerBase` must be named `*Controller`.
pass data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)
pass time/inject-clock — Types in the Web layer, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`.
  grandfathered: 7 (baselined; run 'loadbearing status' for burndown)
FAIL naming/async-suffix — Methods of authored types in the Domain or Web layers returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must be named `*Async`.
  because: Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a method must be awaited.
  citation: https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
  fix: Rename the method to end in Async and update its callers; see the interface and its implementation together.
  src/Meridian.Web/Controllers/BookingsController.cs:60 — Meridian.Web.Controllers.BookingsController.GetByReference()
  grandfathered: 13 (baselined; run 'loadbearing status' for burndown)
pass di/no-buildserviceprovider — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`.
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  grandfathered: 1 (baselined; run 'loadbearing status' for burndown)
pass clearance/engine/tripwire

Checked 8 rules: 7 passed, 1 failed, 0 skipped (1 violation, 0 warnings).
```

Claude Code turns exit 2 into a refused stop and hands that stderr back as the reason, which is how
it arrives in the transcript:

```text
Stop hook feedback:
[sh "${CLAUDE_PROJECT_DIR}/examples/Meridian/hooks/arch-hook.sh"]: pass layering/domain-independent — …
```

The report carries the five things an agent needs to act: the rule ID (`naming/async-suffix`), the
reason, the page that reason rests on, the fix, and the exact `file:line`. The sites already
on the record stay quiet, reported as a passing rule with `grandfathered: 13`; only the new name is
red. Two lines above it, the inline-SQL sites are quiet for the same reason, and that is the debt
the block had already talked the agent out of adding to.

## Beat 4: the agent self-corrects

The agent read the report and renamed the method, in one edit and with no help from anyone:

> The arch hook caught the async naming violation. I need to rename the method to follow the
> async suffix convention.

```csharp
[HttpGet("{reference}")]
public async Task<IActionResult> GetByReferenceAsync(string reference)
```

Then it stopped again. The hook ran the check a second time, found the tree clean, wrote nothing,
and exited 0, so the turn ended. Run the same check by hand and it prints the board with
`naming/async-suffix` back among the passes:

```text
Checked 8 rules: 8 passed, 0 failed, 0 skipped (0 violations, 0 warnings).
```

Nothing shipped in the old shape, and the correction was the tool's own fix line rather than a
reviewer catching it later. Between the two stops the hook cost one check, not one per edit.

## Beat 5: a different task, and the warning that does not block

> Ops report that container numbers from one partner are rejected as invalid. ISO 6346 allows `J`
> and `Z` in the category position as well as `U`; `ContainerNumberValidator` accepts only `U`.

That is a real bug, the fix is one line, and it lives inside `Meridian.Clearance`, the scope the
spec quarantines. The agent widened the check:

```csharp
// ISO 6346 category identifiers: U freight, J detachable equipment, Z trailers and chassis.
if (containerNumber[3] is not ('U' or 'J' or 'Z')) return false;
```

Nothing here breaks a law. Containment governs who may *reference* the scope from outside, and this
edit is inside it, so the check is clean and the wrapper exits 0. What the check does say is that
the edit landed in dragon territory, and a warning has only exit 0 to travel on. So the wrapper
passes `--hook-json --hook-event Stop` and the tool writes the report as the one exit-0 object
Claude Code reads, naming the event that fired:

```json
{
  "hookSpecificOutput": {
    "hookEventName": "Stop",
    "additionalContext": "pass layering/domain-independent — The Domain layer must not reference …"
  }
}
```

The board inside it, in full:

```text
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types derived from `ControllerBase` must be named `*Controller`.
pass data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)
pass time/inject-clock — Types in the Web layer, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`.
  grandfathered: 7 (baselined; run 'loadbearing status' for burndown)
pass naming/async-suffix — Methods of authored types in the Domain or Web layers returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must be named `*Async`.
  grandfathered: 13 (baselined; run 'loadbearing status' for burndown)
pass di/no-buildserviceprovider — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`.
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  grandfathered: 1 (baselined; run 'loadbearing status' for burndown)
warn clearance/engine/tripwire
  warning: Changed file 'src/Meridian.Clearance/ContainerNumberValidator.cs' is inside quarantined scope 'clearance/engine' — does the task actually require editing dragon territory? Dragons: loadbearing explain clearance/engine/tripwire.
  dragons: ISO 6346 check digit: the letter-value table skips every multiple of 11 (A=10, B=12 … U=32); the gaps are load-bearing — linearizing the table breaks every real container number. Call in only through IClearanceGateway.

Checked 8 rules: 8 passed, 0 failed, 0 skipped (0 violations, 1 warning).
```

Claude Code delivers that as a `Stop hook additional context` message and lets the turn continue
once, which is the whole of the tripwire: the dragons arrive under the warning that fired, so the
agent reads them where it is standing rather than paying a round trip to `explain`. It answered
them point by point, and this is the part worth reading twice:

> **The dragons warn about the letter-value table specifically** — `A=10, B=12 … U=32`, every
> multiple of 11 skipped, gaps load-bearing. I didn't touch `ContainerCheckDigit`. The table is
> what makes `J` and `Z` score correctly without any change, which is why the fix is one line in
> the validator and nothing in the check digit.

The category fix ships. What does not happen is the next edit: an agent one file away from a
letter-value table with three gaps in it, told that the gaps are load-bearing before it decides they
are a typo. That is the difference between the two postures on one codebase: Beat 3's ratchet
refuses a stop that would have left new code in a retired shape, and the tripwire lets a legitimate
edit through while making sure nobody makes it uninformed.

## Reproduce it

From a checkout, build the CLI and the example once, then walk the loop by hand. This repository is
a source checkout, so each `loadbearing …` below runs as
`dotnet run --no-build --project src/Zphil.LoadBearing.Cli -- …` from the repository root; install
the [global tool](../README.md#run-it-yourself) to use the real `loadbearing` command.

```bash
dotnet build src/Zphil.LoadBearing.Cli
dotnet build examples/Meridian/Meridian.slnx
loadbearing check examples/Meridian/Meridian.slnx      # exit 0: the committed baseline is clean
```

Add the Beat 2 method to `BookingsController` above its `sample` endpoint, and check again. The
check reads source, so no rebuild is needed for this one:

```bash
loadbearing check examples/Meridian/Meridian.slnx --diff-base HEAD   # exit 1: the Beat 3 board
```

That `--diff-base HEAD` is what the wrapper adds; it only evaluates the quarantined-scope tripwire
(the extra `pass clearance/engine/tripwire` line) and does not change the failing rule. To drive the
wrapper the way the hook does, install the [global tool](../README.md#run-it-yourself) so
`loadbearing` resolves, then run `sh hooks/arch-hook.sh` (or `arch-hook.ps1`): it runs that same
check, prints the report and exits 2 on a red rule, and prints nothing and exits 0 once you switch
to the Beat 4 name. Revert `BookingsController` when you are done so the example tree stays clean.

Beat 5 is the same loop with the tripwire armed. Widen the category check in
`ContainerNumberValidator` as the beat does, rebuild, and check:

```bash
dotnet build examples/Meridian/src/Meridian.Clearance/Meridian.Clearance.csproj
loadbearing check examples/Meridian/Meridian.slnx --diff-base HEAD                            # exit 0: the Beat 5 board
loadbearing check examples/Meridian/Meridian.slnx --diff-base HEAD --hook-json --hook-event Stop  # the same board, as hook context
```

The second command is what the wrapper actually runs, and its output is the JSON object the hook
passes through: one `hookSpecificOutput`, naming the event that fired and carrying that board
escaped into `additionalContext`. Add `--rules 'clearance/*'` to read it without the eight-rule
board inside the string. Run either against the reverted tree and the first prints a clean board
while the second prints nothing at all: a clean check with no warnings says nothing to the agent.
