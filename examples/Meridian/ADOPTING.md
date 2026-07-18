# Adopting LoadBearing on an existing codebase

This page replays the derivation of a real spec, one real command at a time.

You have a working codebase and no architecture spec. Where do you start? You survey what is there, scaffold a spec project, draft candidate rules, run them to gather evidence, assign each rule a posture from that evidence, baseline the acknowledged debt, and render the agent context. Meridian is the worked instance: a freight-forwarding monolith of three projects and eight controllers. The walk keeps its shape at three projects or three hundred, so nothing here depends on Meridian being small.

This is [derive-spec.md](../../src/Zphil.LoadBearing.Cli/Mcp/Prompts/derive-spec.md) applied to real code, step for step (the MCP server serves that same text to agents as the `derive_spec` prompt). The [README](README.md) beside this file owns the destination: the finished spec table, the rendered block, the failure-mode demos, and the burndown board. This page owns the journey to it. To run the whole thing yourself against a scratch copy, jump to [Try it yourself](#try-it-yourself).

## What you'll do

1. Survey the estate
2. Scaffold the spec project
3. Draft candidate rules
4. Check as the evidence pass
5. Assign postures from the evidence
6. Curate, then baseline
7. Render and commit

Two things to hold onto before you start. First: violations during the derive are data, not failures. You will author rules your code breaks on purpose, because the count of what breaks is the measurement that assigns the posture; a red check in the middle of this flow means the evidence pass is working. Second: the tool proposes nothing and you decide everything. It surveys, it checks, it renders. The curation, the `baseline --init`, and the commit belong to you.

This recipe was proven before any example existed. A cold agent, given only the recipe and LoadBearing's own test fixture, walked it end to end: it produced a compiling spec, and the curated result became a committed test fixture. The walk also caught a real defect in spec discovery. The page you are reading is that same walk, run against Meridian and written down.

## Step 1: survey the estate

On your solution, build first, then start with the one verb that needs no spec. `loadbearing graph` reads the compiled code the way extraction does and returns projects, namespace inventories, project-to-project edges, and external references. On a large estate this is how you find the layers without opening every file: the edge matrix shows what references what, and the namespace inventory is the raw material for layer globs. Read all of it as hypotheses, not conclusions.

```text
$ loadbearing graph examples/Meridian/Meridian.slnx --json
{
  "schemaVersion": 1,
  "solution": "Meridian.slnx",
  "projects": [
    {
      "name": "Meridian.Clearance",
      "projectReferences": [],
      "types": 5,
      "namespaces": [
        {
          "namespace": "Meridian.Clearance",
          "types": 5
        }
      ]
    },
    {
      "name": "Meridian.Domain",
      "projectReferences": [],
      "types": 7,
      "namespaces": [
        {
          "namespace": "Meridian.Domain",
          "types": 7
        }
      ]
    },
    {
      "name": "Meridian.Web",
      "projectReferences": [
        "Meridian.Clearance",
        "Meridian.Domain"
      ],
      "types": 18,
      "namespaces": [
        {
          "namespace": "(global)",
          "types": 1
        },
        {
          "namespace": "Meridian.Web.Controllers",
          "types": 8
        },
        {
          "namespace": "Meridian.Web.Data",
          "types": 4
        },
        {
          "namespace": "Meridian.Web.Models",
          "types": 5
        }
      ]
    }
  ],
  "projectEdges": [
    {
      "source": "Meridian.Web",
      "target": "Meridian.Clearance",
      "references": 4
    },
    {
      "source": "Meridian.Web",
      "target": "Meridian.Domain",
      "references": 23
    }
  ],
  "externalEdges": [
    {
      "source": "Meridian.Web",
      "targetNamespaceRoot": "Microsoft.Data",
      "references": 35
    },
    {
      "source": "Meridian.Web",
      "targetNamespaceRoot": "System.Data",
      "references": 8
    },
    {
      "source": "Meridian.Web",
      "targetNamespaceRoot": "System.Threading",
      "references": 7
    }
  ]
}
exit: 0
```

The type counts frame the estate: Clearance 5, Domain 7, Web 18, with Web split across `Controllers` (8 types), `Data` (4), `Models` (5), and one type at global scope. Two candidate layers fall out immediately, `Meridian.Domain.*` and `Meridian.Web.*`. The `projectEdges` show Web reaching Domain (23) and Clearance (4) with nothing pointing back: Domain never references Web, and a direction that is already clean is the cheapest law you will ever write, so it goes on the list. Eight types under `Controllers` is a naming hypothesis (`*Controller`). The external edges carry the smell: Web reaches `Microsoft.Data` 35 times and `System.Data` 8, the shape of inline SQL, though grouped per project the survey cannot yet say how many of those land in controllers (a question for `check`). The wall clock is invisible at this altitude, a hypothesis to test later. Clearance sits behind one thin inbound edge with five types of its own, the module-behind-a-facade shape and the candidate dragon zone. The capture is trimmed: the external roots are cut to the two SQL-shaped ones plus `System.Threading`, dropping the framework roots (`Microsoft.AspNetCore` at 47, `Microsoft.Extensions` at 20) and the `System.*` rows on Clearance and Domain.

## Step 2: scaffold the spec project

On your solution, the spec is a small class library in an `arch/` folder next to the solution file. There is no registration step: discovery is by convention, the one solution project that references the LoadBearing contract assembly. Because the spec project is itself a member of the solution, it is excluded from the universe `check` inspects, so its own types never trip your rules.

Here is Meridian's spec project, exactly as committed:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\..\..\src\Zphil.LoadBearing\Zphil.LoadBearing.csproj" />
        <ProjectReference Include="..\..\src\Meridian.Clearance\Meridian.Clearance.csproj" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.2" />
    </ItemGroup>

</Project>
```

The one line worth calling out is `CopyLocalLockFileAssemblies`: it stages NuGet package assemblies (here SqlClient) into the spec's build output, so `check` can load the `typeof()` targets that live in those packages. It is harmless when every target is a project type or a namespace pattern.

The class starts empty and grows in step 3:

```csharp
using Zphil.LoadBearing;

namespace Meridian.ArchSpec;

public sealed class MeridianArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
    }
}
```

Add it to the solution, then build it before you check. In a source checkout the add step prints two lines rather than one:

```text
$ dotnet sln examples/Meridian/Meridian.slnx add examples/Meridian/arch/Meridian.ArchSpec/Meridian.ArchSpec.csproj --solution-folder arch
Project `arch\Meridian.ArchSpec\Meridian.ArchSpec.csproj` added to the solution.
Project `..\..\src\Zphil.LoadBearing\Zphil.LoadBearing.csproj` added to the solution.
exit: 0
```

In a source checkout like this repository, `dotnet sln add` follows the spec's project references and also adds the LoadBearing contract library (the second `added` line above). The recipe's step 2 lists the errors you may see in that setup, verbatim; [Try it yourself](#try-it-yourself) below shows the exact removal. A published-package setup references LoadBearing as a `PackageReference` and sees none of this.

## Step 3: draft candidate rules

On your solution, turn every hypothesis from the survey into a rule, and draft them all as `Enforce`. Do not pre-judge the posture; the check in the next step supplies the evidence that decides it. Write the already-true directions too, the ones the edge matrix showed clean, because a clean direction made law is the cheapest rule you will ever own. Candidate `Because` notes are fine at this stage; you will upgrade them once the rules are real. The one exception is a dragon zone: a boundary has no `Enforce` form, so you draft it as a `Freeze` scope directly.

Meridian's survey produced four direction-and-convention candidates plus one frozen scope:

```csharp
using Meridian.Clearance;
using Microsoft.Data.SqlClient;
using Zphil.LoadBearing;

namespace Meridian.ArchSpec;

public sealed class MeridianArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "Meridian.Domain.*");
        Layer web = arch.Layer("Web", "Meridian.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Candidate: Domain looked self-contained in the survey; nothing should pull it up into Web.");

        arch.Rule("naming/controllers")
            .Enforce(arch.Types.InNamespace("Meridian.Web.Controllers.*").MustHaveSuffix("Controller"))
            .Because("Candidate: every type under the Controllers namespace looks like an MVC controller.");

        arch.Rule("data-access/no-inline-sql")
            .Enforce(arch.Namespace("Meridian.Web.Controllers.*")
                .MustNotReference(typeof(SqlConnection), typeof(SqlCommand)))
            .Because("Candidate: externalEdges shows Microsoft.Data.SqlClient reached straight from controllers.");

        arch.Rule("time/inject-clock")
            .Enforce(web.MustNotUse(
                arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
            .Because("Candidate: the Web layer looks like it reads the ambient clock directly.");

        arch.Scope("clearance/engine")
            .Freeze(arch.Namespace("Meridian.Clearance.*"))
            .BoundaryOnlyVia(typeof(IClearanceGateway), typeof(ClearanceGateway))
            .Dragons("ISO 6346 check digit: the letter-value table skips every multiple of 11 " +
                     "(A=10, B=12 … U=32); the gaps are load-bearing — linearizing the table breaks " +
                     "every real container number. Call in only through IClearanceGateway.")
            .Because("Candidate: a self-contained clearance module already sits behind a gateway facade.");
    }
}
```

That is five candidate rules. The frozen scope desugars into two checkable rules (a containment rule and a diff-aware tripwire), so `check` will report six. Notice the clock rule bans `DateTime.Now` and `DateTime.UtcNow` across the whole Web layer with no exception yet. The draft states the blunt hypothesis; letting the check find the one type that legitimately reads the clock is the whole job of the next step.

The CLI never builds your code; it reads compiled assemblies. Build the spec before every check, or the check reads a stale build and reports stale results.

## Step 4: check as the evidence pass

On your solution, run `check`. Exit 1 is the expected outcome here, because the reds are the data you came for. Every violation arrives with its rule ID, the reason you wrote, and the exact `file:line` of each offending site. The counts are the measurements: they are what you assign a posture from in the next step.

```text
$ loadbearing check examples/Meridian/Meridian.slnx
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types in `Meridian.Web.Controllers.*` must be named `*Controller`.
FAIL data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  because: Candidate: externalEdges shows Microsoft.Data.SqlClient reached straight from controllers.
  src/Meridian.Web/Controllers/CustomsController.cs:26 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/CustomsController.cs:27 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/CustomsController.cs:28 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/CustomsController.cs:29 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/CustomsController.cs:32 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlCommand
FAIL time/inject-clock — The Web layer must not use `DateTime.Now` or `DateTime.UtcNow`.
  because: Candidate: the Web layer looks like it reads the ambient clock directly.
  src/Meridian.Web/Controllers/CustomsController.cs:16 — Meridian.Web.Controllers.CustomsController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/DriversController.cs:15 — Meridian.Web.Controllers.DriversController uses System.DateTime.Now
  src/Meridian.Web/Controllers/InvoicesController.cs:34 — Meridian.Web.Controllers.InvoicesController uses System.DateTime.Now
  src/Meridian.Web/Controllers/InvoicesController.cs:35 — Meridian.Web.Controllers.InvoicesController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/ManifestsController.cs:14 — Meridian.Web.Controllers.ManifestsController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/RatesController.cs:15 — Meridian.Web.Controllers.RatesController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/ShipmentsController.cs:15 — Meridian.Web.Controllers.ShipmentsController uses System.DateTime.UtcNow
  src/Meridian.Web/Data/SystemClock.cs:7 — Meridian.Web.Data.SystemClock uses System.DateTime.UtcNow
FAIL clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway` must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  because: Candidate: a self-contained clearance module already sits behind a gateway facade.
  fix: use `IClearanceGateway`
  src/Meridian.Web/Controllers/CustomsController.cs:52 — Meridian.Web.Controllers.CustomsController references Meridian.Clearance.ContainerNumberValidator
  src/Meridian.Web/Controllers/CustomsController.cs:53 — Meridian.Web.Controllers.CustomsController references Meridian.Clearance.ContainerNumberValidator
  hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations
skip clearance/engine/tripwire
  skipped: Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this frozen scope.

Checked 6 rules: 2 passed, 3 failed, 1 skipped (21 violations, 0 warnings).
exit: 1
```

The two direction-and-naming rules pass with zero violations, which already tells you their posture. The three that fail are the evidence. `data-access/no-inline-sql` reports twelve violations across six controllers (thirty-one reference sites in the full output; the capture above shows CustomsController's five and drops the other five controllers). That answers the survey's open question: the `Microsoft.Data` references do land in controllers, and in six of the eight. `time/inject-clock` reports eight sites, shown in full, and one of them is the answer to step 3's setup: `src/Meridian.Web/Data/SystemClock.cs:7`, a type whose job is to read the clock. `clearance/engine/containment` reports one inbound reference the gateway does not sanction, with the hint that no baseline exists yet. The tripwire reports `skip`, which is expected: it is diff-aware and does nothing without `--diff-base`. Twenty-one located, named violations, ready to price.

## Step 5: assign postures from the evidence

On your solution, the violation count decides the honest posture for each rule. Zero violations means the code already obeys, so the rule becomes `Enforce` law that costs nothing. Violations plus a target state the team actually wants means `Migrate`: the current sites are grandfathered and new code goes red. A region with no target state, code no one will fix, means `Freeze`: hold the boundary and document what is dangerous inside it. A rule nothing on the team stands behind gets dropped, because an unratified rule is the stale doc this tool exists to kill.

Meridian's evidence assigns cleanly:

| Rule | Evidence | Posture |
|---|---|---|
| `layering/domain-independent` | 0 violations | Enforce |
| `naming/controllers` | 0 violations | Enforce |
| `data-access/no-inline-sql` | 12 pairs, 6 controllers | Migrate |
| `time/inject-clock` | 8 clock reads | Migrate |
| `clearance/engine` | 1 non-facade inbound | Freeze |

The two zero-violation rules become law. The inline-SQL rule has twelve violations (six controllers, each referencing both `SqlConnection` and `SqlCommand`), and the team wants repositories, so it is a `Migrate`: the majority pattern is the one being retired, and the two already-migrated controllers show the target. The clock rule has eight sites and the same shape, so `Migrate` again, with one refinement. The single non-facade reach into Clearance has no target state (the ISO 6346 table stays as it is), so the scope is `Freeze`.

The refinement is where the evidence earns its keep. The draft flagged `SystemClock.cs:7` alongside the seven controller reads. But `SystemClock` implements `IClock`: it is the one sanctioned seam that must read the wall clock, so nothing else has to. The blunt draft rule surfaced the seam; the curated rule keeps it by adding `.Except(arch.Types.WithNameMatching("SystemClock"))`, which leaves seven grandfathered controller reads and one type doing its job. That signal is authoring feedback, not code evidence: an empty subject or a glob that matched nothing would speak the same way, telling you to fix the rule rather than measure the code.

## Step 6: curate, then baseline

On your solution, this is the gate the tool will not cross for you. Go rule by rule and decide each one: accept it, edit it, or drop it. Then upgrade every `Because` from a candidate note into the real reason a reviewer would sign in a design review, because that text renders into the agent context and into every violation message as the team's stated rationale. Only then run `baseline --init`, which grandfathers exactly what is red at that moment. Curation comes first for a reason: day zero is the one moment when current and accepted are the same set, and the baseline is your signature on the debt.

For Meridian the curation is a small set of edits against the draft (the finished result is [the committed spec](arch/Meridian.ArchSpec/MeridianArchSpec.cs)):

- The two data rules move from `Enforce` to `Migrate`, each gaining a factual `from:` line describing the old pattern ("Controllers open SqlConnection and run inline SQL directly"; "Code reads the ambient clock directly"), a real `Because`, and a `Fix` that names the exemplar to copy (`BookingRepository` for the SQL, `BookingsController` for the clock).
- The clock rule gains the `.Except(SystemClock)` refinement from step 5.
- The frozen scope keeps its dragons prose and gains its real `Because`.

With the curated spec built, `baseline --init` captures the debt:

```text
$ loadbearing baseline examples/Meridian/Meridian.slnx --init
data-access/no-inline-sql: captured 12 grandfathered violations.
wrote arch/baselines/data-access/no-inline-sql.json
time/inject-clock: captured 7 grandfathered violations.
wrote arch/baselines/time/inject-clock.json
clearance/engine/containment: captured 1 grandfathered violation.
wrote arch/baselines/clearance/engine/containment.json
exit: 0
```

Three baselines, one per ratcheted rule, holding twelve inline-SQL references, seven clock reads, and one inbound reach. Re-run `check` and the same code is green, because every red is now grandfathered:

```text
$ loadbearing check examples/Meridian/Meridian.slnx
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types in `Meridian.Web.Controllers.*` must be named `*Controller`.
pass data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
pass time/inject-clock — Types in the Web layer, except types whose name matches `SystemClock` must not use `DateTime.Now` or `DateTime.UtcNow`.
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway` must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
skip clearance/engine/tripwire
  skipped: Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this frozen scope.

Checked 6 rules: 5 passed, 0 failed, 1 skipped (0 violations, 0 warnings).
exit: 0
```

Each of the three ratcheted passes also prints its grandfathered count (12, 7, and 1); those sub-lines are dropped above. Exit 0, zero live violations, and the debt is on the record rather than in the way. These are the only two `check` fences on this page, the draft red and the post-baseline green; the curated-but-unbaselined run in between is the one `--init` captured. From here `loadbearing status` prints the per-rule burndown, and the [README's status board](README.md#the-burndown) shows what it reports as the numbers fall.

## Step 7: render and commit

On your solution, `render` turns the spec into agent-readable context. It writes one managed block into the root `AGENTS.md`, and drops a scoped card into each frozen scope's directory (the dragons) and each layer's directory (the local rules). Everything outside the managed markers is preserved byte for byte, so `render` is safe to run over hand-written files.

```text
$ loadbearing render examples/Meridian/Meridian.slnx
unchanged AGENTS.md
unchanged src/Meridian.Domain/AGENTS.md
unchanged src/Meridian.Web/AGENTS.md
unchanged src/Meridian.Clearance/AGENTS.md
exit: 0
```

Read that carefully, because on your codebase the first `render` writes these files. Here every line says `unchanged` because the committed example already carries the rendered blocks, and that is exactly the property CI holds: `render` must be a no-op against the committed tree, or the build fails. The block itself is Meridian's spec in the words an agent reads before it writes; the [README shows what it contains](README.md#what-the-agent-reads) rather than repeating it here.

Then commit, as one reviewable diff: the spec project, `arch/baselines/**`, and the rendered `AGENTS.md` files together. A reviewer reads that single change as three things at once: the proposed law, the acknowledged debt, and the generated context that will keep them true. That is the whole point of doing it in one commit.

## Try it yourself

Everything above is reproducible against a scratch copy of this repository. Work in a throwaway worktree so your checkout stays clean:

```bash
git worktree add ../meridian-sandbox
```

Reset to day zero inside the sandbox: delete `examples/Meridian/arch/` and remove the `Meridian.ArchSpec` entry (and the now-empty `arch` folder) from `examples/Meridian/Meridian.slnx`. Keep all four `AGENTS.md` files: `render` only rewrites the managed blocks between its markers, and the root file's hand-written preamble cannot be regenerated. Build the example solution (`dotnet build examples/Meridian/Meridian.slnx`), then walk steps 1 through 7.

One substitution applies throughout. This repository is a source checkout rather than a package install, so each `loadbearing <verb> …` line above runs as `dotnet run --no-build --project src/Zphil.LoadBearing.Cli -- <verb> …` from the repository root (build the CLI first: `dotnet build src/Zphil.LoadBearing.Cli`). To use the real `loadbearing` command instead, install the global tool per the [README](README.md#run-it-yourself).

Two SDK behaviors the walk hits; neither affects what `check` reports. First, `dotnet sln add` follows the spec's project references and adds the LoadBearing contract library to the solution; remove it and confirm four projects remain:

```bash
dotnet sln examples/Meridian/Meridian.slnx remove src/Zphil.LoadBearing/Zphil.LoadBearing.csproj
```

The path resolves against your working directory, not the solution file, and a path that matches nothing is reported and ignored; re-run `dotnet sln examples/Meridian/Meridian.slnx list` and expect exactly four projects. Second, `dotnet sln add` reformats the solution file, so a little `Meridian.slnx` churn in your final diff is expected.

At the end, `git diff` lands on the committed spec and baselines byte for byte, content digests included. In the sandbox replay the only change left is that solution-file formatting:

```text
$ git status --porcelain
 M examples/Meridian/Meridian.slnx
exit: 0
```

The spec project, the three baselines, and the four `AGENTS.md` files are absent from the diff because the walk reproduced them exactly. That byte-identical landing is the proof the recipe is deterministic: same code, same evidence, same spec.
