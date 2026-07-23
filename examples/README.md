# Examples

Four worked codebases demonstrate one spec model with two render targets: deterministic
enforcement and generated agent context. The three postures are spread across the set:
`Enforce` law, ratcheted `Migrate` debt, and `Freeze` containment. CI builds each example,
runs `check` against its committed baselines, and re-runs `render` to prove the committed
agent context matches the code.

All four share one fiction: Meridian, a freight-forwarding company, so the examples can
cross-reference one another as parts of one business.

- [All three postures on one codebase](Meridian/): the `Meridian` monolith mid-migration.
  The law it keeps, a ratcheted migration with its burndown, and a frozen scope with dragons.
- [Enforce-only clean architecture](Meridian.Quoting/): the greenfield `Meridian.Quoting`
  subsystem. The generated `AGENTS.md` block beside the spec that produced it, and every rule
  as an individually named xUnit test.
- [Module isolation as law](Meridian.Operations/): the `Meridian.Operations` modular
  monolith. A scoped rules card rendered into every module directory, and one module frozen
  behind its facade.
- [Microsoft guidance, enforced and cited](Meridian.Interchange/): the `Meridian.Interchange`
  outbound worker. Every rule's `Because` ends in the learn.microsoft.com page it enforces:
  canon sentence, spec excerpt, real violation.
- [Day-one adoption on an existing codebase](Meridian/ADOPTING.md): the `Meridian/ADOPTING.md`
  walkthrough. The full derive flow from real code to a first spec, every step a real command
  with real output.
- [The agent loop, closed by a hook](Meridian/hooks/): the `Meridian/hooks` showcase. A Claude
  Code hook runs `check` after each edit, so new code in a retired pattern goes red at the
  moment of creation; the storyboard walks one task from the old pattern to self-correction.

Read them in list order: later pages build on earlier ones, and each ends with the
cross-links that say so.
