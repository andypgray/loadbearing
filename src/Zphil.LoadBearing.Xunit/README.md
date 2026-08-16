# Zphil.LoadBearing.Xunit

`Zphil.LoadBearing.Xunit` is the [LoadBearing](https://github.com/andypgray/loadbearing) xUnit
adapter: every rule in an architecture spec runs as an individually named xUnit test. The rule
ID is the test's display name, and a failing rule's message is the exact human block the
`loadbearing` CLI prints.

## Usage

Derive a class from `ArchRuleTests<TSpec>` in your test project and point it at the solution
to check:

```csharp
using Zphil.LoadBearing.Xunit;

public sealed class ArchitectureTests : ArchRuleTests<MyApp.ArchSpec.ArchSpec>
{
    protected override string SolutionPath => FindSolutionUp("MyApp.sln");
}
```

`SolutionPath` is the only required override. `FindSolutionUp` climbs from the test output
directory to the first ancestor holding the named file and returns its absolute path, throwing
`FileNotFoundException` naming the start directory on a miss. Resolve the path however suits
your repo; relative values resolve against the test process's working directory, not the output
directory `FindSolutionUp` starts from. The test explorer lists one case per rule ID; the
workspace load, extraction, and check run once per spec type, and every rule case reads its
verdict from that shared run. A `Quarantine` tripwire rule reports as skipped (a test run has no
diff context); everything else passes or fails like any other test.

When the spec project is a member of the checked solution it is excluded from the checked
universe automatically (mirroring the CLI), along with any project only it pulls in: a library
the spec references that the solution file does not declare, such as a shared rule pack.
Projects the solution declares stay in the universe even when the spec references them, so a
spec may reference the very code it governs. If your spec lives outside the target solution,
override `ExcludeProjectName` to return `null`.

## When the workspace does not load completely

A project that fails to load would otherwise vanish from the checked universe: every rule
would be measured over a codebase missing whole projects, and nothing in a green verdict would
say what was missing. The adapter refuses to let that read as green. One named test,
`Workspace_LoadedCompletely`, fails carrying the load diagnostics and the MSBuild selection
that produced them, and every rule case skips rather than report a verdict that was never
reached. Restore and build the target solution, then rerun.

To check whatever did load anyway, opt in:

```csharp
public sealed class ArchitectureTests : ArchRuleTests<MyApp.ArchSpec.ArchSpec>
{
    protected override string SolutionPath => FindSolutionUp("MyApp.sln");
    protected override bool AllowWorkspaceDiagnostics => true;
}
```

Rule verdicts then come from the partial model, and `Workspace_LoadedCompletely` reports as
skipped, still carrying the diagnostics, rather than pass under a name the run cannot vouch
for.

## When a solution filter narrows the run

A `.slnf` solution path checks the projects the filter selects plus everything they reference,
which can be fewer than the solution declares. Rule cases still report: a narrowed universe is
a smaller true answer, and every verdict reached is real. What cannot pass is the completeness
claim: `Workspace_LoadedCompletely` reports as skipped, naming the declared projects the run
never checked, rather than pass under a name the filtered run cannot vouch for. Point
`SolutionPath` at the solution the filter references to get the whole answer. A filter whose
selection pulls in every declared project narrows nothing, and the run is indistinguishable
from one over the solution.

## Requirements

- **xunit.v3 3.2.2 or later.** The adapter is built against the xunit.v3 authoring libraries;
  a consumer on an older metapackage hits a package-downgrade error. Your test project keeps
  its own `xunit.v3` metapackage and runner references; the adapter brings only the authoring
  pair.
- **A .NET SDK on the test host.** The checker loads the target solution through
  MSBuildWorkspace (via MSBuildLocator), so plain runtime-only environments cannot run these
  tests.
- The target solution must be restored and built before the tests run. The checker never
  builds; stale builds give stale verdicts.

## Writing the spec

The spec itself is authored against the
[`Zphil.LoadBearing`](https://www.nuget.org/packages/Zphil.LoadBearing) contract package; see
its README for the fluent surface. The
[`Zphil.LoadBearing.Cli`](https://www.nuget.org/packages/Zphil.LoadBearing.Cli) global tool
runs the same rules at the command line, in CI, and as an MCP server for coding agents; the
adapter and the CLI produce identical failure text by construction. All four LoadBearing
packages ship one version in lockstep; reference the adapter and the contract at the same one.

## License

[MIT](https://github.com/andypgray/loadbearing/blob/main/LICENSE)
