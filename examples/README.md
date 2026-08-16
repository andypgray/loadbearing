# Examples

Six worked examples demonstrate one spec model with two render targets: deterministic
enforcement and generated agent context. Four are solutions: CI builds each one, runs
`check` against its committed baselines, and re-runs `render` to prove that the committed
agent context and the committed architecture drawings both still match the code. The other
two walk a flow with captured output. The three postures are spread across the set:
`Enforce` law, ratcheted `Migrate` debt, and `Quarantine` containment.

Each of the four solutions ships an `ARCHITECTURE.md` holding a pair of drawings: a codebase
survey of the projects and the references between them, drawn from what the code does, beside
a law fence of the places its spec names and the references it forbids, drawn from the spec.
Nobody draws either one, and the same zero diff that keeps the agent context honest keeps
them honest too.

All six share one fiction: Meridian, a freight-forwarding company, so the examples can
cross-reference one another as parts of one business.

- [All three postures on one codebase](Meridian/): the `Meridian` monolith mid-migration.
  The law it keeps, a ratcheted migration with its burndown, and a quarantined scope with dragons.
  Its [drawings](Meridian/ARCHITECTURE.md) put all three postures on one page as shapes: a solid
  ban, two dotted debt arrows, and a box you can only enter through its facades.
- [Enforce-only clean architecture](Meridian.Quoting/): the greenfield `Meridian.Quoting`
  subsystem. The generated `AGENTS.md` block beside the spec that produced it, and every rule
  as an individually named xUnit test. Its [drawings](Meridian.Quoting/ARCHITECTURE.md) are the
  tidiest law in the set with the emptiest fence, because seven of its nine rules are about names
  and shapes rather than directions.
- [Module isolation as law](Meridian.Operations/): the `Meridian.Operations` modular
  monolith. A scoped rules card rendered into every module directory, and one module quarantined
  behind its facade. Its [drawings](Meridian.Operations/ARCHITECTURE.md) are a two-node survey
  beside a ten-node law, because the modules are namespaces and only the spec can see them.
- [Microsoft guidance, enforced and cited](Meridian.Interchange/): the `Meridian.Interchange`
  outbound worker. Every rule's `Because` ends in the learn.microsoft.com page it enforces:
  canon sentence, spec excerpt, real violation. Its [drawings](Meridian.Interchange/ARCHITECTURE.md)
  are the honest-limits page: canonical guidance is almost entirely non-spatial, so eleven of the
  twelve rules are listed under the fence rather than drawn in it.
- [Day-one adoption on an existing codebase](Meridian/ADOPTING.md): the `Meridian/ADOPTING.md`
  walkthrough. The full derive flow from real code to a first spec, every step a real command
  with real output.
- [The agent loop, closed by a hook](Meridian/hooks/): the `Meridian/hooks` showcase. A Claude
  Code hook runs `check` after each edit, so new code in a retired pattern goes red at the
  moment of creation; the storyboard walks one task from the old pattern to self-correction.

Read them in list order: later pages build on earlier ones, and each ends with the
cross-links that say so.
