LoadBearing exposes this codebase's architecture spec — one reified model, enforced and explained. Five read-only tools:

- `arch_check` — the spec's verdict as a JSON report. Call before finishing work that touched architecture-relevant code; narrow with `rules` globs.
- `arch_status` — the migration burndown: per-rule grandfathered/stale counts and promotion suggestions.
- `arch_explain` — one rule's because / fix / posture rationale, when a violation or a rule ID needs it.
- `arch_context` — the scope card(s) covering a path. Call before editing an unfamiliar directory to learn its rules or whether it is dragon territory.
- `arch_graph` — the codebase survey: projects, namespace inventories, reference edges. Needs no spec — orient on an unfamiliar solution or plan new rules; narrow with `overview` or `projects`.

One prompt, `derive_spec` — the onboarding recipe to run when spec resolution reports no spec project yet.

- Violations are data, not errors — a red rule is a finding in the report, never a tool failure.
- Documents are the CLI verbs' `--json` output byte for byte: camelCase, optional fields absent rather than null, check entries keyed by `id` (`ruleId` is SARIF-only).
- A response over the client budget wants a narrower call (`rules`; `overview`/`projects`), not paging.
- The server is bound to one solution + one spec at start; the tools take no solution argument.
- The first call loads the workspace — seconds on a large solution — then it stays warm, reconciled against disk per call. The server never builds: build first or results are stale.
- A failed project load makes the model wrong, not smaller: `arch_graph` errors naming the projects (a build precondition, not a fault); `arch_check`/`arch_status` return their document stamped `modelIncomplete: true` + `failedProjects` — report that, never plain green. A filter makes it smaller: `uncheckedProjects`.
- Drill down with `arch_explain <rule-id>`; the always-on summary lives in the root `AGENTS.md` managed block.
