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
