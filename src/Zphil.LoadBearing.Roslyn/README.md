# Zphil.LoadBearing.Roslyn

`Zphil.LoadBearing.Roslyn` is [LoadBearing](https://github.com/andypgray/loadbearing)'s Roslyn
extraction and workspace infrastructure: MSBuildWorkspace loading, type-level dependency-edge
extraction, and the baseline-file host layer.

**Not intended for direct reference.** This package exists because it is a dependency of the
packages you actually use:

- Writing an architecture spec? Reference
  [`Zphil.LoadBearing`](https://www.nuget.org/packages/Zphil.LoadBearing) (the contract
  package) and nothing else.
- Running rules as xUnit tests? Reference
  [`Zphil.LoadBearing.Xunit`](https://www.nuget.org/packages/Zphil.LoadBearing.Xunit), which
  pulls this in transitively.
- Checking, rendering, or serving MCP? Install the
  [`Zphil.LoadBearing.Cli`](https://www.nuget.org/packages/Zphil.LoadBearing.Cli) global tool,
  which bundles this assembly.

Its API is internal-first and moves with the tool, not with a compatibility promise.

## License

[MIT](https://github.com/andypgray/loadbearing/blob/main/LICENSE)
