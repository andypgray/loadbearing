# Contributing to LoadBearing

LoadBearing is a fluent C# architecture spec with two render targets: deterministic enforcement and generated AI-agent context. Thanks for your interest in working on it. For what it does and how to install it, see the [README](README.md); this document is about working on the code.

## Development setup

- The .NET 10 SDK is required. The solution file is `Zphil.LoadBearing.slnx` (`.slnx`, not `.sln`), which needs a current SDK to load.
- The checker loads solutions through MSBuildWorkspace (via MSBuildLocator), so the machine running the tests needs a .NET SDK; plain runtime-only environments cannot run the workspace-tier tests.

```bash
dotnet build Zphil.LoadBearing.slnx
dotnet test Zphil.LoadBearing.slnx

# One test by fully-qualified name
dotnet test Zphil.LoadBearing.slnx --filter "FullyQualifiedName~SpecValidationTests"
```

## Test culture: pinned tests are the spec

The house stack is xUnit v3 + Shouldly. Beyond that, one convention carries this repo:

- **Pinned strings are the spec.** The fluent-API shape, the prose fragments every rule renders, the violation-message text, and the CLI/JSON golden outputs are pinned by string-equality tests. They are not incidental assertions; they are the contract. When a pin must move, move it deliberately as part of the change, with the reason in the PR. A diff that silently rewrites golden text to make a test pass will be asked to justify every changed byte.
- Extraction tests run in two tiers: fast `CSharpCompilation`-from-source unit tests, and an MSBuildWorkspace tier over the checked-in fixture solution (`tests/Zphil.LoadBearing.Tests/Fixtures/TestSolutions/MyApp`). Prefer adding coverage at the fast tier; touch the fixture solution only when the workspace path itself is the subject.
- CLI↔MCP output parity is pinned by table tests. If you change a runner's output, the CLI text, the MCP tool result, and (for check failures) the xUnit adapter's failure text move together; they are the same string by construction.

## The dogfood invariant

This repo governs itself: [`arch/Zphil.LoadBearing.ArchSpec`](arch/Zphil.LoadBearing.ArchSpec) is a real spec for this codebase, and the root [`AGENTS.md`](AGENTS.md) contains a block between `<!-- loadbearing:begin -->` / `<!-- loadbearing:end -->` markers that `loadbearing render` generated from it. A self-spec test keeps that block current, so:

- Never hand-edit inside the markers. If your change alters what the spec renders, regenerate the block (`loadbearing render` against this solution) and commit the result with your change, or the suite goes red.
- Content outside the markers is ordinary hand-written documentation; edit it freely.

## Pull request expectations

- Small and focused: one change per PR.
- A green `dotnet test Zphil.LoadBearing.slnx` on your machine.
- XML doc comments on public members; comments explain *why*, not *what*.
- Fluent-surface changes ship together with their pinned-test moves in the same PR.
- Cross-platform portability matters: goldens are forward-slash and CRLF-tolerant by design; a change that only passes on one OS is not done.

## Versioning and releases

The four packages (`Zphil.LoadBearing`, `.Roslyn`, `.Xunit`, `.Cli`) version in lockstep from a single `<Version>` in the root `Directory.Build.props`. Releases are cut by tag; the workflow runs the version-consistency checks, trusted publishing (OIDC), attestation, and a check that `.mcp/server.json` is packed (the MCP registry picks the release up from nuget.org). Release notes live on GitHub Releases and in [CHANGELOG.md](CHANGELOG.md).

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE), the same license as the project.
