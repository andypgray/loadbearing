# Agent-loop storyboard: the rule that corrects the agent

This is the script for the hero demo behind LoadBearing's thesis: a coding agent, working a
routine task, writes the pattern Meridian is retiring, and the architecture rule reaches it at
the moment of creation, so the agent self-corrects before the change ever lands.

Every fenced block below is real captured output from the wrapper in this directory, run against
Meridian. Nothing here is mocked. A screencast of this scenario is planned but not yet recorded
(examples open question (c)); until then this page is its shooting script, and the reproduction
steps make the loop runnable today at the command line.

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
violation the agent is told to fix.

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
pass naming/controllers — Types in `Meridian.Web.Controllers.*` must be named `*Controller`.
FAIL data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  because: Data access behind a repository can be tested and swapped; SQL in the request path cannot.
  fix: Move the SQL into a repository; see BookingRepository.
  src/Meridian.Web/Controllers/BookingsController.cs:72 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/BookingsController.cs:73 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/BookingsController.cs:74 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/BookingsController.cs:75 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/BookingsController.cs:77 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)
pass time/inject-clock — Types in the Web layer, except types whose name matches `SystemClock` must not use `DateTime.Now` or `DateTime.UtcNow`.
  grandfathered: 7 (baselined; run 'loadbearing status' for burndown)
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway` must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  grandfathered: 1 (baselined; run 'loadbearing status' for burndown)
pass clearance/engine/tripwire

Checked 6 rules: 5 passed, 1 failed, 0 skipped (2 violations, 0 warnings).
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
public async Task<IActionResult> Lookup(string reference)
{
    Booking? booking = await bookings.Get(reference);
    return booking is null ? NotFound() : Ok(booking);
}
```

The next `Edit` runs the hook again. The check is green, the wrapper exits 0, and the edit proceeds:

```text
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types in `Meridian.Web.Controllers.*` must be named `*Controller`.
pass data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)
pass time/inject-clock — Types in the Web layer, except types whose name matches `SystemClock` must not use `DateTime.Now` or `DateTime.UtcNow`.
  grandfathered: 7 (baselined; run 'loadbearing status' for burndown)
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway` must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  grandfathered: 1 (baselined; run 'loadbearing status' for burndown)
pass clearance/engine/tripwire

Checked 6 rules: 6 passed, 0 failed, 0 skipped (0 violations, 0 warnings).
```

The endpoint is done, the retired pattern never reached the tree, and the correction was the tool's
own fix line, not a reviewer catching it later.

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

That `--diff-base HEAD` is what the wrapper adds; it only evaluates the frozen-scope tripwire (the
extra `pass clearance/engine/tripwire` line) and does not change the failing rule. To drive the
wrapper the way the hook does, install the [global tool](../README.md#run-it-yourself) so
`loadbearing` resolves, then run `sh hooks/arch-hook.sh` (or `arch-hook.ps1`): it runs that same
check, prints the report and exits 2 on a red rule, and prints nothing and exits 0 once you switch
to the Beat 4 version. Revert `BookingsController` when you are done so the example tree stays clean.

## For the screencast (when it is recorded)

The scenario above is the storyboard for the deferred screencast. Recording notes, following the
publicity storyboard convention:

- Three beats on screen: the task, the red block, the green re-check. Let the red report sit long
  enough to read the four components.
- Capture a neutral pane. No home-directory path, employer or client name, or notification toast in
  any frame; the file paths on screen are Meridian's (`src/Meridian.Web/...`), nothing above the
  repository root. Scrub the last frame too, since a loop returns to it.
- Keep the tool output verbatim. The value of the demo is that the block is real, so it is captured,
  not typeset.
