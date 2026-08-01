# Agent hooks

An agent hook runs `loadbearing check` after every edit and blocks the edit when a rule goes red. This directory holds what that takes for this repository.

| File | What it is |
|---|---|
| [`arch-hook.sh`](arch-hook.sh) | POSIX shell wrapper |
| [`arch-hook.ps1`](arch-hook.ps1) | the same wrapper in PowerShell 7 |
| [`settings.snippet.json`](settings.snippet.json) | the `PostToolUse` entry for `.claude/settings.json` |

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

`.claude/` and `.mcp.json` are not committed. They are local dev aids, and a registration pointing at `bin/Debug` would hand every clone the build lock described below. What a clone gets instead is these files and four steps.

Build first. The CLI has to exist before anything can run it:

```bash
dotnet build Zphil.LoadBearing.slnx
```

Copy the wrapper for a shell you have. PowerShell 7 is not on every machine, and `sh` is:

```bash
mkdir -p .claude && cp hooks/arch-hook.sh .claude/arch-hook.sh
```

Paste [`settings.snippet.json`](settings.snippet.json) into `.claude/settings.json`. It names the PowerShell variant, so if you copied the shell one, change its `command` to `sh "${CLAUDE_PROJECT_DIR}/.claude/arch-hook.sh"`. The three config values at the top of each wrapper are already this repository's solution, spec, and diff base, so nothing else needs editing.

Register the MCP server, if you also want the `arch_*` query tools in the session. In `.mcp.json`:

```json
{
  "mcpServers": {
    "loadbearing": {
      "command": "dotnet",
      "args": [
        "exec", "src/Zphil.LoadBearing.Cli/bin/Debug/net10.0/loadbearing.dll",
        "mcp", "Zphil.LoadBearing.slnx",
        "--spec", "arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj"
      ]
    }
  }
}
```

**Note:** a connected server holds the assemblies it is running open, so `dotnet build` fails with `MSB3021`/`MSB3027` copy errors until you stop it. Build before you connect. A session that needs to rebuild the CLI has to drop the server first, and every connected session runs one, so stopping a single process may not be enough.

## Lifting it into your own repository

Install the tool, copy one wrapper into your repo's `.claude/`, and change the three values at the top of it: your solution, your spec (a csproj or a built spec DLL), and the ref you diff against. `HEAD` suits a working session; CI wants the base branch.

```bash
dotnet tool install -g Zphil.LoadBearing.Cli
```

Then paste the snippet, pointing its `command` at the wrapper you copied.

[The Meridian storyboard](../examples/Meridian/hooks/storyboard.md) walks the whole loop on an example codebase, beat by beat with captured output, and ships the same two wrappers filled in with that example's paths.
