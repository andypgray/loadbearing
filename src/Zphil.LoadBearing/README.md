# Zphil.LoadBearing

`Zphil.LoadBearing` is the LoadBearing spec contract: the fluent builder and reified rule model
an architecture spec is written against. It is the one package a spec project references.

[LoadBearing](https://github.com/andypgray/loadbearing) is one C# architecture spec with two
render targets: deterministic enforcement (CLI/CI/agent hooks) and generated AI-agent context
(a managed `AGENTS.md` block, scoped rules, MCP tools). Every rule carries a posture: `Enforce`
for the law, `Migrate` for ratcheted tech debt with a grandfathered baseline, `Freeze` for
"here be dragons" code that must be contained rather than touched.

## Writing a spec

A spec is a small class library with one class implementing `IArchitectureSpec`:

```csharp
using Zphil.LoadBearing;

public sealed class ArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
        Layer web    = arch.Layer("Web",    "MyApp.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Domain is UI-agnostic; transaction boundaries live in services.")
            .Fix("Define an abstraction in Domain and implement it in Web.");
    }
}
```

Add the spec project to the target solution (`dotnet sln add arch/MyApp.ArchSpec/MyApp.ArchSpec.csproj`)
and the tooling discovers it by convention: the unique solution project that references this
package. The spec project itself is excluded from the checked universe.

Pick the spec project's target framework by one rule: it must be able to reference the product
projects it will `typeof()`. This package is `netstandard2.0` with zero dependencies, so a spec
project can target anything from `net48` up.

## This package is the contract only

Nothing in it checks or renders anything. That is done by:

- [`Zphil.LoadBearing.Cli`](https://www.nuget.org/packages/Zphil.LoadBearing.Cli), the
  `loadbearing` global tool: `check`, `render`, `explain`, `status`, `graph`, `baseline`, and
  the MCP server (`loadbearing mcp`).
- [`Zphil.LoadBearing.Xunit`](https://www.nuget.org/packages/Zphil.LoadBearing.Xunit), the
  adapter that runs every rule in the spec as an individually named xUnit test.

## Documentation

- [Repository and full README](https://github.com/andypgray/loadbearing)

## License

[MIT](https://github.com/andypgray/loadbearing/blob/main/LICENSE)
