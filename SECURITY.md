# Security

This document covers reporting a vulnerability in LoadBearing, the tool's security model, and verifying the packages you install.

## Supported versions

Only the latest release on nuget.org receives security fixes.

## Reporting a vulnerability

Report privately through [GitHub security advisories](https://github.com/andypgray/loadbearing/security/advisories/new). Do not open a public issue for a security report. You can expect an acknowledgment within 7 days.

## Security model

LoadBearing is a local analysis tool, and its threat model follows from that:

- It runs with your user privileges, either as the `loadbearing` CLI or as a stdio MCP subprocess launched by your MCP client (`loadbearing mcp`). It opens no network listener, makes no outbound calls, and sends no telemetry.
- Analysis is read-only. `check`, `graph`, `explain`, `status`, and every MCP tool read the solution and write nothing to it. The verbs that do write are explicit workspace mutations you invoke yourself: `render` (the managed `AGENTS.md` block and scoped context files) and `baseline` (the ratchet files); neither has an MCP surface.
- The tool never builds your code, but it does two things that deserve the same caution you would apply to opening a solution in an IDE. First, loading a solution through MSBuildWorkspace reads and evaluates its project files. Second, the architecture spec is a prebuilt DLL you point it at (or the one it discovers by convention), loaded in a collectible `AssemblyLoadContext` and executed. A spec runs with your privileges, so treat a spec from an untrusted source as you would any code you run.
- Logs under `%LOCALAPPDATA%\Zphil.LoadBearing\logs` (platform-equivalent path elsewhere) can contain absolute paths and rule/violation text read from your solution. The sink keeps 7 daily rolling files, and nothing leaves the machine.

## Verify what you install

Each release ships a signed build provenance attestation covering all four packages. Download a `.nupkg` from the GitHub release and verify it as built:

```bash
gh attestation verify Zphil.LoadBearing.Cli.<version>.nupkg --repo andypgray/loadbearing
```

The release also carries the Sigstore bundle (`attestation.intoto.jsonl`) for offline verification with `gh attestation verify --bundle`.

The nuget.org copy differs. nuget.org appends a repository signature (`.signature.p7s`) after upload, which changes the file hash, so the attestation matches the GitHub release copy rather than the file you download from nuget.org. Use the GitHub release asset for digest verification, and `dotnet nuget verify <file>` for the nuget.org repository signature.

`dotnet tool install -g Zphil.LoadBearing.Cli` and, on .NET 10, `dnx Zphil.LoadBearing.Cli` install the same nuget.org package. `loadbearing --version` prints `<version>+<commit>`, and that commit matches the release tag on andypgray/loadbearing, a source cross-check that needs no tooling.

## Supply chain

Publishing uses NuGet trusted publishing (OIDC), so there is no long-lived API key to store or leak. The release workflow builds, tests, packs, and attests every package. Builds use SourceLink and a deterministic CI configuration. Every GitHub Actions dependency is pinned to a commit SHA, and every NuGet dependency of the shipped projects is locked to a content hash in a committed `packages.lock.json` (restored in locked mode on CI); Dependabot keeps both current.
