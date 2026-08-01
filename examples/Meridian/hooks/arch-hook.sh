#!/bin/sh
# LoadBearing architecture hook for the Meridian example (POSIX variant of arch-hook.ps1).
#
# This is the Meridian instantiation of the LoadBearing agent-hook recipe, filled in with
# Meridian's paths. Same exit-code contract as the PowerShell variant: clean check -> 0
# (proceed); a red rule -> 2 (block, with the violation report on stderr so the agent reads
# the rule ID, reason, fix, and file:line and self-corrects); LoadBearing's own error -> 1
# (a non-blocking config problem, not an architecture violation the agent is told to "fix").
#
# Lift this into your own repo: copy it to .claude/arch-hook.sh and change the three values below.

# Hooks run in the session's current directory, which need not be the repository root;
# Claude Code sets CLAUDE_PROJECT_DIR to the root for every hook it fires.
[ -n "${CLAUDE_PROJECT_DIR:-}" ] && cd "$CLAUDE_PROJECT_DIR"

# The PostToolUse payload on stdin names the edited file. The check reads code, so an edit to
# anything else (docs, config, lockfiles) skips it; with no payload (a hand-run), it runs.
edited_file=''
if [ ! -t 0 ]; then
  edited_file=$(grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n 1 | sed 's/.*:[[:space:]]*"//; s/"$//')
fi
if [ -n "$edited_file" ]; then
  case "$(printf '%s' "$edited_file" | tr '[:upper:]' '[:lower:]')" in
    *.cs|*.csproj|*.props|*.targets|*.sln|*.slnx|*.razor|*.cshtml) ;;
    *) exit 0 ;;
  esac
fi

SOLUTION="${SOLUTION:-examples/Meridian/Meridian.slnx}"
SPEC="${SPEC:-examples/Meridian/arch/Meridian.ArchSpec/bin/Debug/net10.0/Meridian.ArchSpec.dll}"
DIFF_BASE="${DIFF_BASE:-HEAD}"

out=$(loadbearing check "$SOLUTION" --spec "$SPEC" --diff-base "$DIFF_BASE" 2>&1)
code=$?
case "$code" in
  0) exit 0 ;;
  1) printf '%s\n' "$out" >&2; exit 2 ;;             # violations -> block
  *) printf 'loadbearing config error:\n%s\n' "$out" >&2; exit 1 ;;
esac
