# Agent hooks

An agent hook runs `loadbearing check` after every edit and blocks the edit when a rule goes red. This directory holds what that takes for this repository, together with the launcher a warm MCP server needs when it is run from a source checkout.

| File | What it is |
|---|---|
| [`arch-hook.sh`](arch-hook.sh) | POSIX shell wrapper |
| [`arch-hook.ps1`](arch-hook.ps1) | the same wrapper in PowerShell 7 |
| [`settings.snippet.json`](settings.snippet.json) | the `PostToolUse` entry for `.claude/settings.json` |
| [`mcp-launch.sh`](mcp-launch.sh) | runs the MCP server from a copy, so a live server and `dotnet build` stop contending |

## The exit-code contract

LoadBearing and Claude Code number exit codes differently, and translating between them is the whole job of the wrapper.

| `loadbearing check` exits | Meaning | The wrapper exits |
|---|---|---|
| `0` | clean (tripwire warnings do not change this) | `0`, and the agent proceeds |
| `1` | a rule is red | `2`, which blocks, with the report on stderr |
| `2` | LoadBearing's own error | `1`, a config problem rather than a violation |

Claude Code blocks on exit 2 and feeds that process's stderr back to the agent, so the wrapper hands over the whole violation report: rule ID, reason, fix, and every offending `file:line`. Any other non-zero exit reaches the user as a non-blocking error, which is where an unresolvable spec or a bad git ref belongs. An agent told to "fix" a misconfiguration will try.

Each wrapper also reads the `PostToolUse` payload on stdin and returns early when the edited file is not code, so a docs edit pays nothing. With no payload, as in a hand-run, it checks.

Both behaviours are executed by tests rather than described: `HookWrapperTests` runs each wrapper as a real child process against a stub tool, once per row of the table above plus both sides of the payload filter, and holds every wrapper in this repository to one shared contract region.

## Turning it on in a clone of this repository

`.claude/` and `.mcp.json` are not committed. They are local dev aids, and a registration pointing straight at `bin/Debug` would hand every clone the build lock the launcher exists to prevent. What a clone gets instead is these files and four steps.

Build first. The CLI has to exist before anything can run it:

```bash
dotnet build Zphil.LoadBearing.slnx
```

Copy the wrapper for a shell you have. PowerShell 7 is not on every machine, and `sh` is:

```bash
mkdir -p .claude && cp hooks/arch-hook.sh .claude/arch-hook.sh
```

Paste [`settings.snippet.json`](settings.snippet.json) into `.claude/settings.json`. It names the PowerShell variant, so if you copied the shell one, change its `command` to `sh "${CLAUDE_PROJECT_DIR}/.claude/arch-hook.sh"`. The three config values at the top of each wrapper are already this repository's solution, spec, and diff base, so nothing else needs editing.

Register the MCP server, if you also want the `arch_*` query tools in the session. In `.mcp.json`, through the launcher, for the reason the next section gives:

```json
{
  "mcpServers": {
    "loadbearing": {
      "command": "sh",
      "args": [
        "hooks/mcp-launch.sh",
        "Zphil.LoadBearing.slnx",
        "--spec", "arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj"
      ]
    }
  }
}
```

On Windows, the same registration with the shell named by where it lives:

```json
{
  "mcpServers": {
    "loadbearing": {
      "command": "C:\\Program Files\\Git\\bin\\sh.exe",
      "args": [
        "hooks/mcp-launch.sh",
        "Zphil.LoadBearing.slnx",
        "--spec", "arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj"
      ]
    }
  }
}
```

A bare `sh` cannot resolve there. Stdio servers are spawned through `cmd.exe` with the PATH a desktop process inherits, and a default Git for Windows install contributes only `Git\cmd` to it: `git.exe` and its siblings, no shell. The shells git does install, under `Git\bin` and `Git\usr\bin`, sit on no PATH outside a Git Bash session, so the bare spelling fails every connect before the handshake with `'sh' is not recognized`.

Naming the path buys a loud failure and pays for it in generality: with Git installed anywhere else, the connect fails on the spot and the error says which path it wanted. The portable alternative, a committed `.cmd` shim that finds the shell itself, would relay the stdio channel faithfully (the relay fault is PowerShell's, not cmd's), but it stands between the client and the server for the life of the session as one more process a kill has to reach through, which is the very thing the launcher's closing `exec` exists to remove.

## Why a source checkout needs the launcher

A connected server holds open every assembly it loaded, and in a source checkout those are exactly the files the next build has to overwrite: `dotnet build` fails with `MSB3021`/`MSB3027` copy errors for as long as a client is connected. Every connected session runs its own server, so stopping one process may not be enough.

Stopping them is the obvious way out and a bad one. A stdio server whose connection dies is never reconnected, so an `mcp_tool` hook armed against it does not fail loudly, it stops working. On this repository that ran to 222 dead firings, 73% of every firing recorded, most of them downstream of a connection killed to unblock a build. The command wrapper above is a fresh process per firing and never had that failure mode, which is why both legs are kept rather than one dropped: the leg that can hard-gate does not share the weakness of the leg that is fast and warm.

`mcp-launch.sh` takes the contention away instead. It copies the CLI build output to a directory outside the tree, keyed by that build's identity, and execs the server from the copy. Sessions on one build share a directory and copy nothing, a rebuild mints a new one, and no directory is overwritten while a server may still be running from it. Two values configure it, both read from the environment: `LOADBEARING_MCP_BUILD_OUTPUT` (the CLI build output, defaulting to this repository's `Debug` path) and `LOADBEARING_MCP_RUN_ROOT` (where copies live, defaulting to `~/.loadbearing/mcp-run`). Everything after the script name is passed straight to `loadbearing mcp`. The copy is taken at connect, so a live server is the build as of the handshake, and `/mcp` → reconnect is how you pick up a rebuild.

**It fixes the server's own binaries, and only those.** Spec assemblies are the other half, and they need nothing from you: the server loads spec DLLs and their dependencies from their bytes rather than their paths, so the spec project's output stays replaceable and builds of it succeed while a client is connected. The launcher's copy is the fix available on the server side, where the path is the launcher's to choose; the spec's path is yours.

There is no PowerShell sibling here, unlike the wrapper pair. The launcher's last act is `exec`, which replaces the shell with the server and leaves nothing standing between the client and the stdio channel it speaks JSON-RPC over; PowerShell has no equivalent, and a wrapper that relays that channel instead of getting out of its way is a fault nobody wants to debug. A source checkout implies git, and git on Windows installs the POSIX shell the launcher needs; what it does not do is put that shell on the PATH stdio servers are spawned with, which is why the Windows registration above names it by its full path.

## Lifting it into your own repository

Install the tool, copy one wrapper into your repo's `.claude/`, and change the three values at the top of it: your solution, your spec (a csproj or a built spec DLL), and the ref you diff against. `HEAD` suits a working session; CI wants the base branch. The launcher is not part of that move: an installed tool's binaries live outside your tree, so nothing there contends with your build, and the server registers as `loadbearing` directly.

```bash
dotnet tool install -g Zphil.LoadBearing.Cli
```

Then paste the snippet, pointing its `command` at the wrapper you copied.

[The Meridian storyboard](../examples/Meridian/hooks/storyboard.md) walks the whole loop on an example codebase, beat by beat with captured output, and ships the same two wrappers filled in with that example's paths.
