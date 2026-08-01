#!/bin/sh
# LoadBearing architecture hook for this repository (POSIX variant of arch-hook.ps1).
#
# This is LoadBearing's own instantiation of the agent-hook recipe: the tool checking its own
# solution against its own spec after every agent edit. Same exit-code contract as the
# PowerShell variant: clean check -> 0 (proceed); a red rule -> 2 (block, with the violation
# report on stderr so the agent reads the rule ID, reason, fix, and file:line and
# self-corrects); LoadBearing's own error -> 1 (a non-blocking config problem, not an
# architecture violation the agent is told to "fix").
#
# Lift this into your own repo: copy it to .claude/arch-hook.sh and change the three values below.

SOLUTION="${SOLUTION:-Zphil.LoadBearing.slnx}"
SPEC="${SPEC:-arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj}"
DIFF_BASE="${DIFF_BASE:-HEAD}"

out=$(loadbearing check "$SOLUTION" --spec "$SPEC" --diff-base "$DIFF_BASE" 2>&1)
code=$?
case "$code" in
  0) exit 0 ;;
  1) printf '%s\n' "$out" >&2; exit 2 ;;             # violations -> block
  *) printf 'loadbearing config error:\n%s\n' "$out" >&2; exit 1 ;;
esac
