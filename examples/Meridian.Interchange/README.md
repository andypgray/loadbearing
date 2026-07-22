# Meridian.Interchange

Meridian.Interchange is an outbound integration worker whose architecture rules encode canonical Microsoft .NET guidance, each rule citing the learn.microsoft.com page it enforces. Every rule pairs a one-sentence reason with the Microsoft URL behind it, so the rendered agent context quotes the guidance with its provenance and a reviewer can follow the link to the source.

The other examples state house rules. This one states Microsoft's rules and shows its work. An agent dropped into the worker reads the rules before it writes, and here each reason ends in a canonical URL: "reuse HttpClient" arrives as the documented .NET guideline with the page that says so, not as one team's preference. The rule the agent reads is the rule the build enforces, and both carry the citation. It reads as a cookbook: canon sentence, then the spec line that encodes it, then the real violation output when the code breaks it.

The subsystem is Meridian's outbound interchange worker, the freight-forwarding fiction shared with [`../Meridian/`](../Meridian/). It is shaped like the systems the tool is built for: long-lived, business-critical, the transmit path other teams depend on.

## The worker

Meridian.Interchange drains an outbox and transmits booking confirmations, status updates, and customs filings to trading partners (carriers, customs systems, terminals) over HTTP. It is one `Microsoft.NET.Sdk.Worker` project; the modules are namespace subtrees inside it, the [`Meridian.Operations`](../Meridian.Operations/) shape.

The parts the rules act on:

- `Partners` holds the `IPartnerClient` family. `CarrierClient`, `CustomsFilingClient`, and `TerminalClient` are typed clients whose `HttpClient` comes from `IHttpClientFactory`. `LegacyManifestClient` is the one corner that still blocks.
- `Outbox` holds `OutboxMessage` and the scoped `IOutboxStore`. `Processing` holds the scoped `OutboxProcessor`, which reads its settings from an `IOptionsSnapshot<InterchangeOptions>` (the correct options type for a scoped consumer).
- `Dispatch` holds `OutboxDispatcher`, a `BackgroundService`. A hosted service is a singleton, so it captures no scoped service itself: it delegates each poll to a seam in the composition root.
- `Host` is that composition root. `InterchangeServiceCollectionExtensions` wires every service, and `ScopedDispatchRunner` opens a DI scope per poll and resolves the scoped processor inside it. Construction and service-location live here and nowhere else, which is why three of the rules except this one namespace.

That last point is the crux the app is built around. The dispatcher is a singleton that must run scoped work; the sanctioned way to do that is an `IServiceScopeFactory` scope, and the resolve inside it (`scope.ServiceProvider.GetRequiredService<IOutboxProcessor>()`) is the service-locator call rule 3 bans. Confining both to `Meridian.Interchange.Host.*` lets the ban stay total everywhere else while the one legitimate resolve site is excepted.

## The spec

Ten rules of ordinary C# in [arch/Meridian.Interchange.ArchSpec/InterchangeArchSpec.cs](arch/Meridian.Interchange.ArchSpec/InterchangeArchSpec.cs). Each carries a posture, a reason ending in its citation URL, and a fix.

| Rule | Posture | What it says |
|---|---|---|
| `di/construct-via-container` | Enforce | construct partner clients only in the root |
| `http/reuse-httpclient` | Enforce | do not `new HttpClient()` |
| `di/no-service-locator` | Enforce | do not resolve from the provider |
| `di/no-buildserviceprovider` | Enforce | no `BuildServiceProvider` while configuring |
| `async/no-sync-over-async` | Migrate | do not block on a `Task` |
| `di/hosted-services-scope-their-work` | Enforce | a hosted service captures no scoped service |
| `di/no-captive-dependencies` | Enforce | a singleton injects no scoped or transient service |
| `naming/async-suffix` | Enforce | `Task`-returning methods end in `Async` |
| `exceptions/no-general-catch` | Enforce | catch base `Exception` only in a top-level handler |
| `async/accept-cancellation` | Enforce | `Task`-returning methods accept a `CancellationToken` |

Nine rules are already true, so they are law (`Enforce`). One describes the blocking corner with a target, so it ratchets (`Migrate`): the current blocking is grandfathered, and any new blocking is red. Two of the rules read like this in the spec, the citation URL sitting at the end of each `Because`:

```csharp
arch.Rule("http/reuse-httpclient")
    .Enforce(arch.Types.Except(host).MustNotConstruct(typeof(HttpClient)))
    .Because("A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers — https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines")
    .Fix("Take a typed or named client from IHttpClientFactory; see how CarrierClient receives its HttpClient.");

arch.Rule("di/hosted-services-scope-their-work")
    .Enforce(arch.Types.DerivedFrom<BackgroundService>().MustNotReference(typeof(IOptionsSnapshot<>), typeof(IOutboxStore)))
    .Because("A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope — resolve per work item from an IServiceScopeFactory scope — https://learn.microsoft.com/dotnet/core/extensions/scoped-service")
    .Fix("Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it; see OutboxDispatcher and ScopedDispatchRunner.");
```

`loadbearing render` writes those into the managed block in [AGENTS.md](AGENTS.md), citation and all. CI re-renders on every push and fails on any diff, so the context an agent reads is provably the spec the build enforces:

```markdown
- `http/reuse-httpclient` — Types, except types in `Meridian.Interchange.Host.*` must not construct `HttpClient`. A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers — https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines
- `di/hosted-services-scope-their-work` — Types derived from `BackgroundService` must not reference `IOptionsSnapshot<TOptions>` or `IOutboxStore`. A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope — resolve per work item from an IServiceScopeFactory scope — https://learn.microsoft.com/dotnet/core/extensions/scoped-service
```

## The silent win

`http/reuse-httpclient` is green over a codebase that never breaks it. No type constructs an `HttpClient`: the three typed clients receive theirs from `IHttpClientFactory`, and the legacy adapter takes a named client from the factory in the composition root. The rule's target is absent, so the rule passes by guarding a state that is already clean. The point of a silent win is that it stays silent only while the code stays right, and it goes loud the moment an agent reaches for the shortcut the guideline warns about.

Add one line to `CarrierClient.SendAsync`, the shortcut a socket-exhaustion bug is made of:

```csharp
using var probe = new HttpClient();
```

`dotnet build` is green: the compiler has no quarrel with `new HttpClient()`. `check` is not:

```text
FAIL http/reuse-httpclient — Types, except types in `Meridian.Interchange.Host.*` must not construct `HttpClient`.
  because: A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers — https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines
  fix: Take a typed or named client from IHttpClientFactory; see how CarrierClient receives its HttpClient.
  src/Meridian.Interchange/Partners/CarrierClient.cs:16 — Meridian.Interchange.Partners.CarrierClient constructs System.Net.Http.HttpClient
```

The message carries all four components: the rule ID, the `because` with the Microsoft URL inline, the `fix` pointing at how `CarrierClient` already receives its client, and the exact `file:line`. An agent that reads this failure gets the guideline and its source in the same breath, then corrects the code. Revert the line and `check` is green again.

## The burndown

One rule ratchets. `LegacyManifestClient` adapts Meridian's own legacy manifest gateway, whose SDK exposes only synchronous entry points, so the adapter blocks at three points: serializing the manifest, taking the single-flight gate, and posting it. Those three lines touch four distinct blocking members (`Task<T>.GetAwaiter`, `TaskAwaiter<T>.GetResult`, `Task.Wait`, and `Task<T>.Result`), so the generated baseline carries four entries. Because they are grandfathered, `check` exits 0, and `loadbearing status` prints what is left to work off:

```text
pass di/construct-via-container
pass http/reuse-httpclient
pass di/no-service-locator
pass di/no-buildserviceprovider
pass async/no-sync-over-async (migrate) — 4 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass di/hosted-services-scope-their-work
pass di/no-captive-dependencies
pass naming/async-suffix
pass exceptions/no-general-catch
pass async/accept-cancellation
Checked 10 rules: 10 passed, 0 failed, 0 skipped. Burndown: 4 grandfathered remaining, 0 fixed awaiting acceptance.
```

The four blocking calls are recorded, not accepted. New blocking anywhere in the worker is red on sight, and when the legacy SDK exposes an async surface and the corner is rewritten, the count drops to zero and the rule is ready to promote to `Enforce`.

## The captive dependency

`di/no-captive-dependencies` reads two facts the other rules don't: which types are registered with which lifetime, and which types inject which. It states the general captive-dependency antipattern (a singleton must not inject a scoped or transient service), the case `di/hosted-services-scope-their-work` reaches only where the type hierarchy already reveals it.

```csharp
arch.Rule("di/no-captive-dependencies")
    .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(
        arch.Registered(Lifetime.Scoped), arch.Registered(Lifetime.Transient)))
    .Because("A singleton is created once and holds every dependency it injects for the whole process, so a scoped or transient service injected into it is captured past its lifetime and shared across all callers — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
    .Fix("Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope, as ScopedDispatchRunner does; take only singleton-safe dependencies in the constructor.");
```

The worker is green under it. Both singletons take only singleton-safe dependencies: `ScopedDispatchRunner` injects `IServiceScopeFactory`, and `OutboxDispatcher` injects that runner and an `IOptions<InterchangeOptions>` (`IOptions<T>` is a singleton, unlike the scoped `IOptionsSnapshot<T>` its processor uses). The scoped work stays behind the scope seam.

`OutboxDispatcher` reaches the rule through `AddHostedService<OutboxDispatcher>`: a hosted service registers as a singleton, so the dispatcher is a singleton-registered type even though no `AddSingleton` names it. Take the scoped `IOutboxStore` in the dispatcher and read the outbox directly, in place of the scoped runner, and the capture is on:

```csharp
internal sealed class OutboxDispatcher(IOutboxStore store, IOptions<InterchangeOptions> options, ILogger<OutboxDispatcher> logger) : BackgroundService
```

`dotnet build` stays green. `check` returns exit 1, with two failures on that one edit:

```text
FAIL di/no-captive-dependencies — Singleton-registered types must not inject scoped-registered types or transient-registered types.
  because: A singleton is created once and holds every dependency it injects for the whole process, so a scoped or transient service injected into it is captured past its lifetime and shared across all callers — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines
  fix: Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope, as ScopedDispatchRunner does; take only singleton-safe dependencies in the constructor.
  src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:16 — Meridian.Interchange.Dispatch.OutboxDispatcher injects Meridian.Interchange.Outbox.IOutboxStore
```

`di/no-captive-dependencies` reads the capture from the registrations, where `IOutboxStore` is registered scoped. `di/hosted-services-scope-their-work` reads the same edit from the hierarchy, because `IOutboxStore` is its named target and the dispatcher derives from `BackgroundService`. The two overlap on this dependency, and neither replaces the other. The registration rule also catches a captured scoped `IOutboxProcessor`, or a transient partner client, that the hierarchy rule never names. The hierarchy rule also catches `IOptionsSnapshot<InterchangeOptions>`, which is framework-registered and never spelled in a source-level registration, so it stays invisible to the registration rule: swap the parameter to that snapshot type and only the hierarchy rule fires. Revert the parameter and `check` is exit 0 again.

The registrations this rule reads are the ones the source spells: `AddSingleton`, `AddScoped`, `AddTransient`, their `TryAdd` twins, `AddHostedService`, `AddDbContext`, and `AddHttpClient<TClient>`. A registration made by assembly scanning, a factory body, or a framework default falls outside that boundary, and the rendered glossary names the boundary so an agent reading the context sees the edge of what the rule knows.

## The scoped catch

`exceptions/no-general-catch` is green over a worker that catches narrowly. `OutboxProcessor` retries a failed send inside `catch (HttpRequestException)` and lets everything else surface; no type in the worker wraps its work in a blanket `catch (Exception)`, except the one that is meant to. The rule bans catching base `Exception` across `Meridian.Interchange.*` and excepts the types that derive from `BackgroundService`, here the dispatcher alone.

That one exception is the load-bearing part. `OutboxDispatcher.ExecuteAsync` wraps each poll in `catch (Exception)` and logs, because on modern .NET an unhandled exception out of a `BackgroundService` stops the host. The poll loop is the worker's top-level handler, the one site the guidance sanctions a catch-all. CA1031 (Do not catch general exception types) would flag the dispatcher's site; the spec sanctions it by scope and holds the ban everywhere else.

Add the blanket catch one layer down, in `OutboxProcessor`, where a failed delivery should surface instead of vanishing. Wrap the per-message loop body in a catch-all:

```csharp
catch (Exception)
{
    // Keep draining the batch even if one message blows up.
}
```

`dotnet build` is green: a blanket `catch (Exception)` is valid C#. `check` is not:

```text
FAIL exceptions/no-general-catch — Types in `Meridian.Interchange.*`, except types derived from `BackgroundService` must not catch `Exception`.
  because: Catching base Exception outside a top-level handler swallows the faults you meant to see; the dispatcher's poll loop is that handler, so scope the catch-all there and let other code catch only the specific types it can handle — https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types
  fix: Catch the specific exception you can handle; the only sanctioned catch-all is the dispatcher's poll loop, where OutboxDispatcher logs and continues to the next poll.
  src/Meridian.Interchange/Processing/OutboxProcessor.cs:32 — Meridian.Interchange.Processing.OutboxProcessor catches System.Exception
```

The match is exact: `MustNotCatch(typeof(Exception))` flags a catch of base `Exception`, and a `catch (TimeoutException)` beside it stays invisible to the rule, which is the good state the guidance wants. Revert the catch and `check` is exit 0 again.

## The flowed token

`async/accept-cancellation` is green over a worker that carries a token the whole way down. Every `Task`-returning method in `Meridian.Interchange.*` accepts a `CancellationToken`: `OutboxDispatcher.ExecuteAsync` receives the host's `stoppingToken`, `ScopedDispatchRunner` and `OutboxProcessor` pass it to the calls they make, and the partner clients and the outbox store take it at the leaf. The rule requires the parameter on every one of them.

```csharp
arch.Rule("async/accept-cancellation")
    .Enforce(arch.Types.InNamespace("Meridian.Interchange.*").Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken)))
    .Because("Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the calls it makes, so a Task-returning method without one cannot take part in cooperative cancellation — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap")
    .Fix("Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain, as ScopedDispatchRunner and OutboxProcessor already do.");
```

The rule reads the declared parameter list. A method satisfies it by declaring a `CancellationToken` parameter; whether the body then forwards that token to the calls it makes is a separate question of flow this presence rule does not answer. What it guarantees is that the token is on the surface for a caller to pass.

Drop the token from `ScopedDispatchRunner.RunPendingAsync`, the seam the dispatcher's `stoppingToken` flows through. The parameter is load-bearing, so removing it takes three touches to compile: the signature loses the parameter, the interior `ProcessPendingAsync` call falls back to `CancellationToken.None`, and the `OutboxDispatcher` call site drops its argument.

```csharp
// ScopedDispatchRunner.cs
public async Task RunPendingAsync()
{
    using IServiceScope scope = scopeFactory.CreateScope();
    var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
    await processor.ProcessPendingAsync(CancellationToken.None);
}

// OutboxDispatcher.cs, in the poll loop
await runner.RunPendingAsync();
```

`dotnet build` is green: dropping a parameter is valid C#. `check` is not:

```text
FAIL async/accept-cancellation — Methods of types in `Meridian.Interchange.*` returning `Task` or `Task<TResult>` must accept a parameter of type `CancellationToken`.
  because: Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the calls it makes, so a Task-returning method without one cannot take part in cooperative cancellation — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
  fix: Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain, as ScopedDispatchRunner and OutboxProcessor already do.
  src/Meridian.Interchange/Host/ScopedDispatchRunner.cs:13 — Meridian.Interchange.Host.ScopedDispatchRunner.RunPendingAsync()
```

The violation is keyed to the method at its declaration site, `ScopedDispatchRunner.cs:13`, and it is the only red: `RunPendingAsync` still ends in `Async`, so `naming/async-suffix` stays green. Revert the three touches and `check` is exit 0 again.

## The citations

Every rule's reason ends in the page it enforces. The quoted phrase below is drawn from that page, verified against the live doc.

| Rule | Microsoft guidance | Source |
|---|---|---|
| `di/construct-via-container` | "Avoid direct instantiation of dependent classes within services" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `http/reuse-httpclient` | "reusing HttpClient instances for as many HTTP requests as possible" | [HttpClient guidelines](https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines) |
| `di/no-service-locator` | "Avoid using the service locator pattern" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `di/no-buildserviceprovider` | "Avoid calls to BuildServiceProvider when configuring services" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `async/no-sync-over-async` | "can result in deadlocks and blocked context threads" | [Asynchronous programming scenarios](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios) |
| `di/hosted-services-scope-their-work` | "the service is registered as a singleton" | [Scoped services in a BackgroundService](https://learn.microsoft.com/dotnet/core/extensions/scoped-service) |
| `di/no-captive-dependencies` | "A singleton can inadvertently capture scoped or transient dependencies" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `naming/async-suffix` | "Asynchronous methods in TAP include the Async suffix" | [Task-based asynchronous pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) |
| `exceptions/no-general-catch` | "AVOID catching System.Exception or System.SystemException, except in top-level exception handlers" | [Using standard exception types](https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types) |
| `async/accept-cancellation` | "consider adding a CancellationToken parameter" | [Task-based asynchronous pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) |

Four rules share the DI guidelines page, which is one document covering construction, the service-locator anti-pattern, `BuildServiceProvider`, and the captive-dependency anti-pattern across its recommendations and anti-patterns sections. The TAP page is cited twice, once for the `Async` suffix and once for the `CancellationToken` parameter, the two conventions it fixes for a `Task`-returning method.

## Not yet enforceable

The pack encodes the guidance the current vocabulary can express. Two neighboring rules from the same Microsoft docs are out of reach in v1. Each is named here with the workaround that holds the line today, no roadmap attached.

- Signature exposure cannot be told apart from a reference. v1 treats a reference and an exposure as the same edge, so "domain entities must not surface on the presentation layer; return DTOs" is not expressible. What holds the line: the layer reference rules (`MustOnlyReference`, shown in `Meridian.Operations`) constrain which namespaces may see the domain at all.
- Persistence ignorance has no negative verb. v1 has no negative hierarchy or attribute combinator, so "a persisted type carries no ORM base class or `[Table]`/`[Column]` attribute" is expressible only through the general `.Must(predicate, description:)` escape hatch. What holds the line: that predicate, since a whole-namespace ban would also block the `DataAnnotations` validation attributes the domain legitimately uses.

## Pairing with analyzers

Several rules have a neighbor in the analyzer ecosystem. What none of those neighbors offers is turnkey, whole-solution, scoped enforcement with the citation and the ratchet, which is the gap this pack fills. Where a neighbor exists, here is what it covers and where the rule differs.

- `http/reuse-httpclient`: no analyzer bans `new HttpClient()`. The guideline is documented and widely cited, and nothing enforces it in the general case. This rule does, everywhere outside the composition root.
- `di/no-buildserviceprovider`: ASP0000 warns on `BuildServiceProvider`, but only in the ASP.NET Core `Startup`/`ConfigureServices` shape. This rule is solution-wide and independent of that shape.
- `async/no-sync-over-async`: VSTHRD002, VSTHRD103, MA0045, and AsyncFixer02 all detect blocking on a `Task`, blanket-wide. The delta is the posture: this rule grandfathers the one legacy corner on a counted baseline and holds the line against new blocking, where the analyzers give one global on/off with no scoped exemption and no ratchet.
- `di/hosted-services-scope-their-work`: the container's `ValidateOnBuild`/`ValidateScopes` catches a captured scoped service, but at runtime, when the host builds. This rule catches the capture statically, in CI and in the agent's edit loop, before the app runs.
- `di/no-captive-dependencies`: two community analyzers, Excubo.Analyzers.DependencyInjectionValidation and georgepwall1991/DependencyInjection.Lifetime.Analyzers, flag a captive dependency statically, each within a single compilation, while the runtime scope validation named above throws only when the host builds. The delta is whole-solution reach across a registration and a constructor that can live in different projects, and the citation carried in the reason.
- `naming/async-suffix`: VSTHRD200 enforces the `Async` suffix, blanket-wide. The delta is scoping (this rule names the `Meridian.Interchange.*` cone) and the citation carried in the reason.
- `exceptions/no-general-catch`: CA1031 (Do not catch general exception types) flags every `catch (System.Exception)`, blanket-wide, and is widely turned off because the one place a catch-all belongs (the top-level handler) trips it too. The delta is scoping: this rule excepts `BackgroundService`-derived types, so the dispatcher's poll loop is sanctioned while the ban holds across the rest of the worker, and the citation rides in the reason.
- `async/accept-cancellation`: CA1068 (CancellationToken parameters must come last) governs the position of a token that is already present, not whether one is present. Meziantou's MA0032, MA0040, and MA0079 flag a call site that forwards no token when one is in scope. Both act only once a token exists on the surface; requiring the parameter there in the first place is the white space this rule fills across `Meridian.Interchange.*`. The match is definition-level and exact: a `CancellationToken?` or a `params CancellationToken[]` parameter is a different declared type and does not satisfy the rule.
- `di/no-service-locator`: one honesty note. This rule bans the `GetService`/`GetRequiredService` members, and a ban on an interface member catches a call through that interface, not a call through a concrete type that re-declares the member. In this worker every resolve goes through `IServiceProvider` or the `ServiceProviderServiceExtensions` methods, so the ban is complete here; a codebase that resolved through a concrete container type would need that type named too.

## Introduce a violation

Each posture goes red on a small edit and back to green on revert. The `new HttpClient()` edit is [the silent win](#the-silent-win), the captured store is [the captive dependency](#the-captive-dependency), the blanket `catch` is [the scoped catch](#the-scoped-catch), and the dropped token is [the flowed token](#the-flowed-token), all above. Two more show the rest.

Change `OutboxDispatcher`'s constructor parameter from `IOptions<InterchangeOptions>` to `IOptionsSnapshot<InterchangeOptions>`. It compiles, and it would even resolve, which is the trap: `IOptionsSnapshot` is scoped, the dispatcher is a singleton, and the snapshot would be captured for the whole process. `OutboxProcessor`, being scoped, uses `IOptionsSnapshot` correctly; the singleton dispatcher must not. The rule reads that from the type hierarchy alone:

```text
FAIL di/hosted-services-scope-their-work — Types derived from `BackgroundService` must not reference `IOptionsSnapshot<TOptions>` or `IOutboxStore`.
  because: A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope — resolve per work item from an IServiceScopeFactory scope — https://learn.microsoft.com/dotnet/core/extensions/scoped-service
  fix: Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it; see OutboxDispatcher and ScopedDispatchRunner.
  src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:15 — Meridian.Interchange.Dispatch.OutboxDispatcher references Microsoft.Extensions.Options.IOptionsSnapshot<TOptions>
```

Rename `IOutboxProcessor.ProcessPendingAsync` to `ProcessPending` (with its implementation and the one call site in `ScopedDispatchRunner`, so it compiles). Both the interface and the class now declare a `Task`-returning method without the suffix, so the rule fires on both:

```text
FAIL naming/async-suffix — Methods of types in `Meridian.Interchange.*` returning `Task` or `Task<TResult>` must be named `*Async`.
  because: Task-returning methods carry the Async suffix so callers see at the call site that a method must be awaited — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
  fix: Rename the method to end in Async.
  src/Meridian.Interchange/Processing/IOutboxProcessor.cs:7 — Meridian.Interchange.Processing.IOutboxProcessor.ProcessPending()
  src/Meridian.Interchange/Processing/OutboxProcessor.cs:19 — Meridian.Interchange.Processing.OutboxProcessor.ProcessPending()
```

Revert either edit and `check` is back to exit 0.

## Run it yourself

LoadBearing ships as a .NET global tool (it needs the .NET 10 runtime):

```bash
dotnet tool install -g Zphil.LoadBearing.Cli
```

From a checkout of this repository, build the worker and check it:

```bash
dotnet build examples/Meridian.Interchange/Meridian.Interchange.slnx
loadbearing check examples/Meridian.Interchange/Meridian.Interchange.slnx
```

`check` exits 0 here: `Checked 10 rules: 10 passed, 0 failed, 0 skipped (0 violations, 0 warnings)`. Without the global tool, run the CLI from source: `dotnet run --project src/Zphil.LoadBearing.Cli -- check examples/Meridian.Interchange/Meridian.Interchange.slnx`. `loadbearing status` prints the burndown, `loadbearing render` regenerates the `AGENTS.md` block from the spec, and `loadbearing explain <rule-id>` expands any rule, as does the `arch_context` MCP tool. Introduce the `new HttpClient()` edit above and `check` exits 1 with the block shown.

## From here

[`../Meridian/`](../Meridian/) carries all three postures on one migrating codebase, and [`../Meridian.Operations/`](../Meridian.Operations/) draws module boundaries and renders a card into every module directory. Meridian.Interchange keeps the same fluent vocabulary and points it at a single question: for each rule, which Microsoft page says so. The answer travels with the rule into the code review and into the agent's context.
