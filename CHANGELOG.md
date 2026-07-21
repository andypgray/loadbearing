# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Warm MCP server: the workspace loads on the first tool call, then is held warm and
  reconciled against disk per call, so a post-edit `arch_check` answers in milliseconds
  (opt out with `LOADBEARING_DISABLE_WARM_WORKSPACE=true`). The one-shot CLI gains a
  persisted per-solution extraction cache: a clean tree checks without a design-time
  build; `--no-cache` bypasses it.
- `check`/`status`/`graph` `--binlog <path>`: replay a real build's binlog instead of running a design-time build (via Basic.CompilerLog). The capture persists per solution, and later runs replay it automatically while it stays structurally valid; `--no-cache` now bypasses both the fragment cache and the build capture. Known v1 limit: replayed models can omit source-generator output (measured: ASP.NET Razor generated types), so on generator-heavy solutions the design-time path remains the fidelity reference.
- Member-access bans: `arch.Member(...)` targets and the `MustNotUse` verb. "The Web layer
  must not use `DateTime.Now`" is now a one-line rule, checked at source-level member use
  with `file:line` and ratcheted like any other rule (`baseline --add --target` accepts
  member names and symbol IDs).
- Member subjects: `.Methods`/`.Properties`/`.Fields`/`.Events`/`.Members` projections
  with member adjectives (`.Returning(...)`, name affixes, `.Where(...)`) and member
  verbs (`MustHaveSuffix`, `MustBePrivate`, `MustBeVirtual`, …): "methods returning
  `Task` must be named `*Async`" in closed vocabulary.
- Compile-checked anchors and generic sugar: `arch.Member<T>(x => x.M)` /
  `arch.Member(() => X.M)` expression member anchors, `arch.Type<X>()`, generic
  adjective/constraint twins (`.Implementing<T>()`, `MustImplement<T>()`, …), and the
  static forms directly on the verb, `MustNotUse(() => DateTime.Now, () => DateTime.UtcNow)`.
  All pure authoring sugar reifying to the identical model.
- Spec-source locations in validation errors: every spec-build error that names a rule, scope, or
  member now renders with the `file:line` of the offending statement — captured via
  `[CallerFilePath]`/`[CallerLineNumber]` on the anchor factories (`arch.Rule`, `arch.Scope`, the five
  `arch.Member` forms) — so all-errors-at-once lands each one at a jump target. Rendered file-name-only
  (never the machine-specific full path), keeping goldens deterministic; an unlocated error degrades to
  the un-prefixed message. The optional caller-info parameters are invisible at call sites but are
  binary-breaking for a spec DLL compiled against the previous Core (acceptable pre-publish at lockstep
  0.1.0).
- Constructor bans: the `MustNotConstruct` verb. "Types must not construct types implementing
  `IHandler<T>`" forbids direct `new` of types meant to arrive via DI — a service you may reference
  but may not construct — recorded at source-level object-creation sites (`new`, including
  target-typed `new()`) with `file:line`, reported human (`{source} constructs {target}`) and JSON
  (kind `"construction"`), and ratcheted on the (source, constructed) type pair like any other rule.
  Carries the same `(first, params more)` selection and type-sugar overloads as the reference verbs.
  The canonical §12 sample adopts it as `di/handlers-via-registry` — every type except the
  sanctioned `HandlerRegistry` must not construct an `IHandler<T>` implementor — the first rule added
  to the founding sample.
- DI registration facts and the captive-dependency ban: the `arch.Registered(Lifetime.Singleton)` /
  `arch.Registered()` noun selects types named in source-visible container registrations
  (`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/`AddDbContext`/
  `AddHttpClient<TClient>` — service and implementation types alike), and the `MustNotInject`
  verb bans constructor-parameter dependencies (primary constructors included): "Singleton-registered
  types must not inject scoped-registered types or transient-registered types" is now a one-line,
  ratcheted rule with `file:line` sites — the general captive-dependency check that no
  whole-solution static tool ships. Reported human (`{source} injects {target}`) and JSON (kind
  `"injection"`); registrations made by assembly scanning, factory internals, or framework
  defaults are the documented honesty boundary. The `Meridian.Interchange` guidance pack now
  enforces it as `di/no-captive-dependencies` with the Microsoft DI-guidelines citation.

## [0.1.0] - 2026-07-14

Initial public release, pre-alpha. One fluent C# architecture spec, two render targets:
deterministic enforcement and generated AI-agent context.

### Added

- The reified model and fluent builder (`Zphil.LoadBearing`, netstandard2.0): layers, scopes,
  and rules, each carrying a posture: `Enforce` (the law), `Migrate` (ratcheted tech debt with
  a grandfathered baseline), `Freeze` (contained legacy with "here be dragons" prose).
- Roslyn/MSBuildWorkspace extraction with `file:line` violation locations
  (`Zphil.LoadBearing.Roslyn`).
- The `loadbearing` global tool (`Zphil.LoadBearing.Cli`): `check` (with `--json` and the
  diff-aware `--diff-base` freeze tripwire), `graph`, `render` (the managed AGENTS.md block),
  `explain`, `status`, and `baseline` (`--init` / `--accept-reductions` / `--add`).
- MCP stdio server (`loadbearing mcp`): `arch_check`, `arch_status`, `arch_explain`,
  `arch_context`, `arch_graph` with CLI-identical output, plus the `derive_spec` onboarding
  prompt; ships as an MCP server package (`PackageType=McpServer`) with `.mcp/server.json`.
- xUnit adapter (`Zphil.LoadBearing.Xunit`): every rule in the spec as an individually named
  xUnit test, failure text identical to the CLI.

[Unreleased]: https://github.com/andypgray/loadbearing/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/andypgray/loadbearing/releases/tag/v0.1.0
