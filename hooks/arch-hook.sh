#!/bin/sh
# LoadBearing architecture hook for this repository (POSIX variant of arch-hook.ps1).
#
# This is LoadBearing's own instantiation of the agent-hook recipe: the tool checking its own
# solution against its own spec after every agent code edit. Same exit-code contract as the
# PowerShell variant: clean check -> 0 (proceed); a red rule -> 2 (block, with the violation
# report on stderr so the agent reads the rule ID, reason, fix, and file:line and
# self-corrects); LoadBearing's own error -> 1 (a non-blocking config problem, not an
# architecture violation the agent is told to "fix").
#
# Exit 0 is not silence: --hook-json makes a clean check write its report as PostToolUse additional
# context when a tripwire warned, and this wrapper passes that through on stdout, where Claude Code
# turns it into a transcript message. A clean check with nothing to say writes nothing.
#
# Lift this into your own repo: copy it to .claude/arch-hook.sh and change the three values below.

# The PostToolUse payload on stdin names the edited file. Read it once, because stdin does not
# rewind and both gates below answer from it, and normalise the path it carries to forward slashes
# with its JSON escaping undone, so everything downstream compares one spelling.
edited_file=''
if [ ! -t 0 ]; then
  edited_file=$(grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n 1 | sed 's/.*:[[:space:]]*"//; s/"$//')
  edited_file=$(printf '%s' "$edited_file" | sed 's|\\\\|/|g; s|\\|/|g')
fi

# The check reads code, so an edit to anything else (docs, config, lockfiles) skips it; with no
# payload (a hand-run), it runs.
if [ -n "$edited_file" ]; then
  case "$(printf '%s' "$edited_file" | tr '[:upper:]' '[:lower:]')" in
    *.cs|*.csproj|*.props|*.targets|*.sln|*.slnx|*.razor|*.cshtml) ;;
    *) exit 0 ;;
  esac
fi

# Check the tree the edit landed in, not the tree this process happens to sit in. The hook runs in
# the session's directory, and neither that nor CLAUDE_PROJECT_DIR need be where the edit went: an
# agent working inside a linked worktree, or a session in the main checkout writing to a worktree
# by absolute path, would otherwise spend a full check on a tree the edit never touched and report
# a verdict about the wrong code. With no payload the current directory stands in for the edited
# file. Every fallback here runs the check rather than skipping it: when in doubt, check.
if [ -n "${CLAUDE_PROJECT_DIR:-}" ]; then
  project_dir=$(printf '%s' "$CLAUDE_PROJECT_DIR" | sed 's|\\|/|g; s|/$||')
  edited_dir='.'
  if [ -n "$edited_file" ]; then
    # A relative payload path is project-relative. An absolute one outside the project directory
    # belongs to no tree this hook speaks for, and need not be in a repository at all: a scratch
    # directory is the everyday case. That compare ignores case, so the paths it rules out are the
    # ones plainly somewhere else.
    edited_dir=$(dirname "$edited_file")
    case "$edited_dir" in
      /*|[A-Za-z]:/*)
        edited_prefix=$(printf '%s/' "$edited_dir" | tr '[:upper:]' '[:lower:]')
        project_prefix=$(printf '%s/' "$project_dir" | tr '[:upper:]' '[:lower:]')
        case "$edited_prefix" in
          "$project_prefix"*) ;;
          *) exit 0 ;;
        esac
        ;;
      *) edited_dir="$project_dir/$edited_dir" ;;
    esac
  fi

  # Inside by path is not inside by tree: a linked worktree lives under the project directory.
  # rev-parse --show-toplevel names the worktree's own root, so the two answers differ exactly
  # there, and asking git for both spells them the same way from any depth on any platform.
  edited_root=$(git -C "$edited_dir" rev-parse --show-toplevel 2>/dev/null)
  project_root=$(git -C "$project_dir" rev-parse --show-toplevel 2>/dev/null)
  if [ -n "$edited_root" ] && [ -n "$project_root" ] && [ "$edited_root" != "$project_root" ]; then
    exit 0
  fi
fi

# Hooks run in the session's current directory, which need not be the repository root;
# Claude Code sets CLAUDE_PROJECT_DIR to the root for every hook it fires.
[ -n "${CLAUDE_PROJECT_DIR:-}" ] && cd "$CLAUDE_PROJECT_DIR"

SOLUTION="${SOLUTION:-Zphil.LoadBearing.slnx}"
SPEC="${SPEC:-arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj}"
DIFF_BASE="${DIFF_BASE:-HEAD}"

out=$(loadbearing check "$SOLUTION" --spec "$SPEC" --diff-base "$DIFF_BASE" --hook-json 2>&1)
code=$?
case "$code" in
  0) [ -n "$out" ] && printf '%s\n' "$out"; exit 0 ;; # tripwire warnings reach the agent as context
  1) printf '%s\n' "$out" >&2; exit 2 ;;             # violations -> block
  *) printf 'loadbearing config error:\n%s\n' "$out" >&2; exit 1 ;;
esac
