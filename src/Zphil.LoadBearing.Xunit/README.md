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
    protected override string SolutionPath => FindUp("MyApp.sln");

    private static string FindUp(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, name)))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, name);
    }
}
```

`SolutionPath` is the only required override. Resolve the path however suits your repo;
relative values resolve against the test process's working directory, so the example walks up
from the test output directory instead. The test explorer lists one case per rule ID; the
workspace load, extraction, and check run once per spec type, and every rule case reads its
verdict from that shared run. A `Freeze` tripwire rule reports as skipped (a test run has no
diff context); everything else passes or fails like any other test.

When the spec project is a member of the checked solution it is excluded from the checked
universe automatically (mirroring the CLI). If your spec lives outside the target solution,
override `ExcludeProjectName` to return `null`.

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
adapter and the CLI produce identical failure text by construction.

## License

[MIT](https://github.com/andypgray/loadbearing/blob/main/LICENSE)
