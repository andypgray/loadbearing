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

Seven rules of ordinary C# in [arch/Meridian.Interchange.ArchSpec/InterchangeArchSpec.cs](arch/Meridian.Interchange.ArchSpec/InterchangeArchSpec.cs). Each carries a posture, a reason ending in its citation URL, and a fix.

| Rule | Posture | What it says |
|---|---|---|
| `di/construct-via-container` | Enforce | construct partner clients only in the root |
| `http/reuse-httpclient` | Enforce | do not `new HttpClient()` |
| `di/no-service-locator` | Enforce | do not resolve from the provider |
| `di/no-buildserviceprovider` | Enforce | no `BuildServiceProvider` while configuring |
| `async/no-sync-over-async` | Migrate | do not block on a `Task` |
| `di/hosted-services-scope-their-work` | Enforce | a hosted service captures no scoped service |
| `naming/async-suffix` | Enforce | `Task`-returning methods end in `Async` |

Six rules are already true, so they are law (`Enforce`). One describes the blocking corner with a target, so it ratchets (`Migrate`): the current blocking is grandfathered, and any new blocking is red. Two of the rules read like this in the spec, the citation URL sitting at the end of each `Because`:

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
pass naming/async-suffix
Checked 7 rules: 7 passed, 0 failed, 0 skipped. Burndown: 4 grandfathered remaining, 0 fixed awaiting acceptance.
```

The four blocking calls are recorded, not accepted. New blocking anywhere in the worker is red on sight, and when the legacy SDK exposes an async surface and the corner is rewritten, the count drops to zero and the rule is ready to promote to `Enforce`.

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
| `naming/async-suffix` | "Asynchronous methods in TAP include the Async suffix" | [Task-based asynchronous pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) |

Three rules share the DI guidelines page, which is one document covering construction, the service-locator anti-pattern, and `BuildServiceProvider` in its recommendations and anti-patterns sections.

## Not yet enforceable

The pack encodes the guidance the current vocabulary can express. Five neighboring rules from the same Microsoft docs are out of reach in v1. Each is named here with the workaround that holds the line today, no roadmap attached.

- Captive dependency in its general form is out of reach. v1 does not read DI lifetime registrations (`AddSingleton`, `AddScoped`, `AddTransient`), so the general rule (no singleton may depend on a scoped service) is not expressible. What holds the line: `di/hosted-services-scope-their-work` covers the case knowable from the type hierarchy (a `BackgroundService` is a singleton by construction), and the container's own scope validation throws on the rest at startup.
- A scoped exception-catch policy is out of reach. v1 sees type references and member accesses, not `catch` and `throw` statements, so "only top-level handlers may catch base `Exception`" cannot be stated. What holds the line: CA1031 flags the blanket base-`Exception` catch, and the top-level-only scoping the guidance describes stays a review convention.
- CancellationToken presence cannot be required. v1 knows a member by its name and return type, not its parameters, so "a public `Task`-returning method should accept a `CancellationToken`" is inexpressible. What holds the line: `naming/async-suffix` enforces the name, and CA1068 keeps a token in the right position wherever one is already present.
- Signature exposure cannot be told apart from a reference. v1 treats a reference and an exposure as the same edge, so "domain entities must not surface on the presentation layer; return DTOs" is not expressible. What holds the line: the layer reference rules (`MustOnlyReference`, shown in `Meridian.Operations`) constrain which namespaces may see the domain at all.
- Persistence ignorance has no negative verb. v1 has no negative hierarchy or attribute combinator, so "a persisted type carries no ORM base class or `[Table]`/`[Column]` attribute" is expressible only through the general `.Must(predicate, description:)` escape hatch. What holds the line: that predicate, since a whole-namespace ban would also block the `DataAnnotations` validation attributes the domain legitimately uses.

## Pairing with analyzers

Several rules have a neighbor in the analyzer ecosystem. What none of those neighbors offers is turnkey, whole-solution, scoped enforcement with the citation and the ratchet, which is the gap this pack fills. Where a neighbor exists, here is what it covers and where the rule differs.

- `http/reuse-httpclient`: no analyzer bans `new HttpClient()`. The guideline is documented and widely cited, and nothing enforces it in the general case. This rule does, everywhere outside the composition root.
- `di/no-buildserviceprovider`: ASP0000 warns on `BuildServiceProvider`, but only in the ASP.NET Core `Startup`/`ConfigureServices` shape. This rule is solution-wide and independent of that shape.
- `async/no-sync-over-async`: VSTHRD002, VSTHRD103, MA0045, and AsyncFixer02 all detect blocking on a `Task`, blanket-wide. The delta is the posture: this rule grandfathers the one legacy corner on a counted baseline and holds the line against new blocking, where the analyzers give one global on/off with no scoped exemption and no ratchet.
- `di/hosted-services-scope-their-work`: the container's `ValidateOnBuild`/`ValidateScopes` catches a captured scoped service, but at runtime, when the host builds. This rule catches the capture statically, in CI and in the agent's edit loop, before the app runs.
- `naming/async-suffix`: VSTHRD200 enforces the `Async` suffix, blanket-wide. The delta is scoping (this rule names the `Meridian.Interchange.*` cone) and the citation carried in the reason.
- `di/no-service-locator`: one honesty note. This rule bans the `GetService`/`GetRequiredService` members, and a ban on an interface member catches a call through that interface, not a call through a concrete type that re-declares the member. In this worker every resolve goes through `IServiceProvider` or the `ServiceProviderServiceExtensions` methods, so the ban is complete here; a codebase that resolved through a concrete container type would need that type named too.

## Introduce a violation

Each posture goes red on a small edit and back to green on revert. The `new HttpClient()` edit is [the silent win](#the-silent-win) above. Two more show the rest.

Change `OutboxDispatcher`'s constructor parameter from `IOptions<InterchangeOptions>` to `IOptionsSnapshot<InterchangeOptions>`. It compiles, and it would even resolve, which is the trap: `IOptionsSnapshot` is scoped, the dispatcher is a singleton, and the snapshot would be captured for the whole process. `OutboxProcessor`, being scoped, uses `IOptionsSnapshot` correctly; the singleton dispatcher must not. The rule reads that from the type hierarchy alone:

```text
FAIL di/hosted-services-scope-their-work — Types derived from `BackgroundService` must not reference `IOptionsSnapshot<TOptions>` or `IOutboxStore`.
  because: A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope — resolve per work item from an IServiceScopeFactory scope — https://learn.microsoft.com/dotnet/core/extensions/scoped-service
  fix: Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it; see OutboxDispatcher and ScopedDispatchRunner.
  src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:12 — Meridian.Interchange.Dispatch.OutboxDispatcher references Microsoft.Extensions.Options.IOptionsSnapshot<TOptions>
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

`check` exits 0 here: `Checked 7 rules: 7 passed, 0 failed, 0 skipped (0 violations, 0 warnings)`. Without the global tool, run the CLI from source: `dotnet run --project src/Zphil.LoadBearing.Cli -- check examples/Meridian.Interchange/Meridian.Interchange.slnx`. `loadbearing status` prints the burndown, `loadbearing render` regenerates the `AGENTS.md` block from the spec, and `loadbearing explain <rule-id>` expands any rule, as does the `arch_context` MCP tool. Introduce the `new HttpClient()` edit above and `check` exits 1 with the block shown.

## From here

[`../Meridian/`](../Meridian/) carries all three postures on one migrating codebase, and [`../Meridian.Operations/`](../Meridian.Operations/) draws module boundaries and renders a card into every module directory. Meridian.Interchange keeps the same fluent vocabulary and points it at a single question: for each rule, which Microsoft page says so. The answer travels with the rule into the code review and into the agent's context.
