LoadBearing exposes this codebase's architecture spec — one reified model, enforced and explained. Five read-only tools:

- `arch_check` — run the whole spec against the solution and return the JSON check report (schemaVersion 3). Call before finishing work that touched architecture-relevant code, to confirm no rule went red. Params: `diffBase` (optional git ref; changed files in a frozen scope raise a tripwire warning).
- `arch_status` — return the JSON burndown (schemaVersion 2): per-rule grandfathered/stale counts and promotion suggestions. Call to see how much migration debt remains. No params.
- `arch_explain` — return one rule's because / fix / posture payload / linked prose as text. Call when a violation or a rendered rule ID needs its full rationale. Params: `ruleId` (a post-desugar rule ID, e.g. `layering/domain-independent` or `legacy/billing/containment`).
- `arch_context` — return the architecture scope card(s) covering a path — a frozen scope's dragons + sanctioned surface, or a layer's local rules — or a pointer line when none apply. Call before editing an unfamiliar directory to learn its architecture rules or whether it is dragon territory. Params: `path` (a file or directory, absolute or solution-relative).
- `arch_graph` — return the JSON codebase survey (schemaVersion 1): projects with namespace inventories, declared vs observed project→project reference edges, and external references grouped by namespace root. Call it to orient on an unfamiliar solution or to plan new rules — it is the one tool that needs no spec. No params.

One prompt: `derive_spec` — the onboarding recipe for a solution with no spec project yet: survey with `arch_graph`, scaffold the spec project, draft candidate rules, validate with `arch_check`, then the human curates and baselines. Start there when spec resolution reports no spec project found.

Cross-cutting:

- **Violations are data, not errors.** `arch_check` returns its report even when rules fail — read the `summary` counts and the `rules[]` array; a red rule is a finding, never a tool failure.
- The server is bound to **one solution + one spec** (set when it was started); the tools take no solution argument.
- Every call **loads the workspace fresh** — expect several seconds on a large solution. There is no caching in v1.
- The server **never builds.** Build the solution yourself before checking; a stale build yields stale results.
- Drill down with `arch_explain <rule-id>`; the always-on architecture summary lives in the repository's root `AGENTS.md` managed block.
