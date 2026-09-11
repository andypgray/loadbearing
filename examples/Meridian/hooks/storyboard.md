# Agent-loop storyboard: the rule that corrects the agent

A coding agent, working a routine task, writes the pattern Meridian is retiring, and the
architecture rule reaches it at the moment of creation, so the agent self-corrects before the
change ever lands. This page walks that loop beat by beat, then walks the other half of it: an
edit the rules allow, into code the spec has marked as dangerous, where the hook informs rather
than blocks.

Every fenced block below is real captured output from the wrapper in this directory, run against
Meridian. Nothing here is mocked, and [Reproduce it](#reproduce-it) walks the same loop by hand
at the command line.

## The files in this directory

The repository does not commit `.claude/` (it is a local dev aid), so the hook config lives here
as a paste-in rather than a live `.claude/` folder. Lift these into your own repo:

| File | What it is |
|---|---|
| [`arch-hook.ps1`](arch-hook.ps1) | The PowerShell wrapper: runs `loadbearing check` and maps its exit code to Claude Code's blocking convention. |
| [`arch-hook.sh`](arch-hook.sh) | The POSIX variant, same contract. |
| [`settings.snippet.json`](settings.snippet.json) | Paste into your `.claude/settings.json`: a `PostToolUse` hook on `Edit\|Write`, plus a deny rule that keeps `baseline` a human decision. |

The wrappers are the Meridian instantiation of LoadBearing's agent-hook recipe, filled in with
Meridian's solution, spec assembly, and diff base. The one thing the wrapper exists to get right
is the exit code:
a clean check returns 0 and the edit proceeds; a red rule returns 2, which is how a Claude Code
hook blocks, carrying the violation report on stderr so the agent reads it and fixes the code;
LoadBearing's own errors return 1, a config problem the user sees rather than an architecture
violation the agent is told to fix. Returning 0 is not the same as saying nothing: where the check
warned, the wrapper hands the report back as hook context, which is Beat 5.

## Beat 1: the task

> Add a lookup endpoint to `BookingsController` that returns a booking by its reference, or 404
> when there is no such booking.

`BookingsController` is one of the two controllers already migrated to a repository and an
injected clock. The other six open a `SqlConnection` and run inline SQL in the request path.

## Beat 2: the agent writes the old pattern

Six of the eight controllers do data access with inline SQL, so an agent reading the codebase for
house style finds the retired pattern in the majority and copies it. It adds `Microsoft.Data.SqlClient`,
takes an `IConfiguration` to reach the connection string, and writes the query straight into the
controller, the same shape `CustomsController` already uses:

```csharp
[HttpGet("lookup/{reference}")]
public IActionResult Lookup(string reference)
{
    string connectionString = configuration.GetConnectionString("Meridian")!;
    const string sql =
        """
        SELECT Reference, CustomerName, Lane, ContainerNumbers, CutoffUtc
        FROM Bookings
        WHERE Reference = @reference
        """;

    using var connection = new SqlConnection(connectionString);
    using var command = new SqlCommand(sql, connection);
    command.Parameters.AddWithValue("@reference", reference);
    connection.Open();

    using SqlDataReader reader = command.ExecuteReader();
    if (!reader.Read()) return NotFound();

    var booking = new Booking
    {
        Reference = reader.GetString(0),
        CustomerName = reader.GetString(1),
        Lane = reader.GetString(2),
        ContainerNumbers = reader.GetString(3).Split(','),
        CutoffUtc = reader.GetDateTime(4)
    };

    return Ok(booking);
}
```

This compiles and runs. It is also exactly the debt `data-access/no-inline-sql` is ratcheting down,
and it is new code, so the ratchet must go red on it.

## Beat 3: the hook fires

The `Edit` that wrote the method triggers the `PostToolUse` hook. The wrapper runs `check`, sees a
red rule, and exits 2, feeding this report to the agent on stderr:

```text
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types derived from `ControllerBase` must be named `*Controller`.
FAIL data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  because: Data access behind a repository can be tested and swapped; SQL in the request path cannot.
  fix: Move the SQL into a repository; see BookingRepository.
  src/Meridian.Web/Controllers/BookingsController.cs:85 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/BookingsController.cs:86 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/BookingsController.cs:87 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/BookingsController.cs:88 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/BookingsController.cs:90 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)
pass time/inject-clock — Types in the Web layer, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`.
  grandfathered: 7 (baselined; run 'loadbearing status' for burndown)
pass naming/async-suffix — Methods of authored types in the Domain or Web layers returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must be named `*Async`.
  grandfathered: 13 (baselined; run 'loadbearing status' for burndown)
pass di/no-buildserviceprovider — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`.
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  grandfathered: 1 (baselined; run 'loadbearing status' for burndown)
pass clearance/engine/tripwire

Checked 8 rules: 7 passed, 1 failed, 0 skipped (2 violations, 0 warnings).
```

The report carries the four things an agent needs to act: the rule ID (`data-access/no-inline-sql`),
the reason, the fix that names the exemplar to copy, and the exact `file:line` of every offending
reference. The twelve inline-SQL sites already on the record stay quiet, reported as a passing rule
with `grandfathered: 12`; only the new code is red. New code in the old pattern is blocked; the
existing debt is not.

## Beat 4: the agent self-corrects

The fix line points at `BookingRepository`, and the agent finds that `IBookingRepository` already
has the method this endpoint needs. The inline SQL collapses to a call through the injected
repository:

```csharp
[HttpGet("lookup/{reference}")]
public async Task<IActionResult> LookupAsync(string reference)
{
    Booking? booking = await bookings.Get(reference);
    return booking is null ? NotFound() : Ok(booking);
}
```

The `Async` suffix on the new method is the second ratchet doing the same job as the first. Write it
as `Lookup` and `naming/async-suffix` goes red on that one method while its thirteen grandfathered
sites stay quiet, with the same shape of report: a rule ID, a reason, a fix, and one `file:line`. The
repository method it calls is one of those thirteen, so the old name and the new one sit a line
apart, and only the new one is blocked.

The next `Edit` runs the hook again. The check is green, the wrapper exits 0, and the edit proceeds:

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
pass clearance/engine/tripwire

Checked 8 rules: 8 passed, 0 failed, 0 skipped (0 violations, 0 warnings).
```

The endpoint is done, the retired pattern never reached the tree, and the correction was the tool's
own fix line, not a reviewer catching it later.

## Beat 5: a different task, and the warning that does not block

> Ops report that container numbers from one partner are rejected as invalid. ISO 6346 allows `J`
> and `Z` in the category position as well as `U`; `ContainerNumberValidator` accepts only `U`.

That is a real bug, the fix is one line, and it lives inside `Meridian.Clearance`, the scope the
spec quarantines. The agent widens the check:

```csharp
if (containerNumber[3] is not ('U' or 'J' or 'Z')) return false;
```

Nothing here breaks a law. Containment governs who may *reference* the scope from outside, and this
edit is inside it, so the check is clean and the wrapper exits 0. What the check does say is that
the edit landed in dragon territory:

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

Checked 8 rules: 8 passed, 0 failed, 0 skipped (0 violations, 1 warning).
```

Getting that paragraph in front of the agent is the whole of the tripwire. A warning never moves the
exit code, and a hook that exits 0 has no stderr channel to the agent, so the wrapper passes
`--hook-json` and hands this report back as `hookSpecificOutput.additionalContext`, the one exit-0
output Claude Code turns into a transcript message. The agent reads it, follows the line it ends
with, and gets the dragons:

```text
clearance/engine/tripwire (quarantine/tripwire)
  because: The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it.
  scope: clearance/engine
  dragons: ISO 6346 check digit: the letter-value table skips every multiple of 11 (A=10, B=12 … U=32); the gaps are load-bearing — linearizing the table breaks every real container number. Call in only through IClearanceGateway.
```

The category fix ships. What does not happen is the next edit: an agent one file away from a
letter-value table with three gaps in it, told that the gaps are load-bearing before it decides they
are a typo. That is the difference between the two postures on one codebase: Beat 3's ratchet
blocks new code in a retired pattern, and the tripwire lets a legitimate edit through while making
sure nobody makes it uninformed.

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

Add the Beat 2 method to `BookingsController` (with `using Microsoft.Data.SqlClient;` and an
`IConfiguration configuration` constructor parameter), rebuild `Meridian.Web`, and check again:

```bash
dotnet build examples/Meridian/src/Meridian.Web/Meridian.Web.csproj
loadbearing check examples/Meridian/Meridian.slnx --diff-base HEAD   # exit 1: the Beat 3 board
```

That `--diff-base HEAD` is what the wrapper adds; it only evaluates the quarantined-scope tripwire (the
extra `pass clearance/engine/tripwire` line) and does not change the failing rule. To drive the
wrapper the way the hook does, install the [global tool](../README.md#run-it-yourself) so
`loadbearing` resolves, then run `sh hooks/arch-hook.sh` (or `arch-hook.ps1`): it runs that same
check, prints the report and exits 2 on a red rule, and prints nothing and exits 0 once you switch
to the Beat 4 version. Revert `BookingsController` when you are done so the example tree stays clean.

Beat 5 is the same loop with the tripwire armed. Widen the category check in
`ContainerNumberValidator` as the beat does, rebuild, and check:

```bash
dotnet build examples/Meridian/src/Meridian.Clearance/Meridian.Clearance.csproj
loadbearing check examples/Meridian/Meridian.slnx --diff-base HEAD              # exit 0: the Beat 5 board
loadbearing check examples/Meridian/Meridian.slnx --diff-base HEAD --hook-json  # the same board, as hook context
```

The second command is what the wrapper actually runs, and its output is the JSON object the hook
passes through: one `hookSpecificOutput`, carrying that board escaped into `additionalContext`. Add
`--rules 'clearance/*'` to read it without the eight-rule board inside the string. Run either
against the reverted tree and the first prints a clean board while the second prints nothing at all:
a clean check with no warnings says nothing to the agent.
