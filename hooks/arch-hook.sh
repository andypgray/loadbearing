#!/bin/sh
# LoadBearing architecture hook for this repository (POSIX variant of arch-hook.ps1).
#
# This is LoadBearing's own instantiation of the agent-hook recipe: the tool checking its own
# solution against its own spec at the moment the agent says it is done. Same exit-code contract as
# the PowerShell variant: clean check -> 0 (the turn ends); a red rule -> 2 (the stop is refused,
# with the violation report on stderr so the agent reads the rule ID, reason, fix, and file:line and
# keeps working); LoadBearing's own error -> 1 (a non-blocking config problem, not an architecture
# violation the agent is told to "fix").
#
# Exit 0 is not silence: --hook-json makes a clean check write its report as the fired event's
# additional context when a tripwire warned, and this wrapper passes that through on stdout, where
# Claude Code turns it into a transcript message. A clean check with nothing to say writes nothing.
#
# Lift this into your own repo: copy it to .claude/arch-hook.sh and change the three values below.

# The hook payload on stdin says which event fired and what it touched. Read it whole and once,
# because stdin does not rewind and every gate below answers from it. A POSIX shell has no JSON, so
# what follows lifts the handful of scalars this needs out of the document by key.
payload=''
[ -t 0 ] || payload=$(cat)

hook_field() {
  printf '%s' "$payload" | grep -o "\"$1\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" | head -n 1 | sed 's/.*:[[:space:]]*"//; s/"$//'
}

hook_flag() {
  printf '%s' "$payload" | grep -Eo "\"$1\"[[:space:]]*:[[:space:]]*(true|false)" | head -n 1 | sed 's/.*:[[:space:]]*//'
}

# A path out of the payload, with its JSON escaping undone and its separators turned forward, so
# everything downstream compares one spelling.
hook_path() {
  hook_field "$1" | sed 's|\\\\|/|g; s|\\|/|g'
}

# True when nothing the listing names has been written since $1. Timestamps rather than find or
# stat: Windows ships a find.exe of its own, and stat is not POSIX. A path the listing names and the
# tree no longer has is a deletion, which the listing itself already carries.
unwritten_since() {
  printf '%s\n' "$listing" | (
    while IFS= read -r entry; do
      [ -n "$entry" ] || continue
      path=${entry#???}
      case "$path" in *' -> '*) path=${path##* -> } ;; esac
      case "$path" in '"'*'"') path=${path#\"}; path=${path%\"} ;; esac
      [ -e "$path" ] || continue
      [ "$path" -nt "$1" ] && exit 1
    done
    exit 0
  )
}

# The three writes that make the next stop cheap. Each is a no-op wherever there is no state to keep
# — a per-edit firing, or a state directory that would not open — so the exit mapping below reads as
# one contract rather than two.
record() {
  [ "$stateful" = 1 ] || return 0
  { printf '%s\n%s\n' "$1" "$head_sha"; printf '%s\n' "$listing"; } > "$verdict_file" 2>/dev/null
}

count_round() {
  [ "$stateful" = 1 ] || return 0
  printf '%s %s\n' "$prompt_id" "$((rounds + 1))" > "$rounds_file" 2>/dev/null
}

forget_rounds() {
  [ "$stateful" = 1 ] || return 0
  rm -f "$rounds_file" 2>/dev/null
}

# Stop and SubagentStop are the turn boundary: the agent has said it is done, so the check reads the
# working tree rather than one tool's payload — which is also how it covers the edits no matcher
# sees, the ones a shell or a language server made. Any other event, and a hand run, takes the
# per-edit path instead, which is what an existing PostToolUse wiring still gets.
hook_event=$(hook_field hook_event_name)
[ -n "$hook_event" ] || hook_event='PostToolUse'
case "$hook_event" in
  Stop | SubagentStop) turn_end=1 ;;
  *) turn_end=0 ;;
esac
# Empty while the hook may still block. 'report' is the counted cap: say so once and let the turn
# end. 'silent' is the degraded mode with nowhere to count — the payload says this hook has already
# blocked in this turn, which is the single round it can carry, so the turn ends without a word.
stand_down=''
stateful=0

if [ "$turn_end" = 1 ]; then
  # Check the tree the session is in. The payload names the directory it ran from and git names the
  # working tree that directory belongs to, so a session inside a linked worktree checks its own
  # worktree rather than the checkout CLAUDE_PROJECT_DIR points at; the three config values below are
  # root-relative, which is what lets one wrapper serve both.
  tree=''
  session_cwd=$(hook_path cwd)
  [ -n "$session_cwd" ] && tree=$(git -C "$session_cwd" rev-parse --show-toplevel 2>/dev/null)
  [ -n "$tree" ] || tree="${CLAUDE_PROJECT_DIR:-}"
  [ -n "$tree" ] && cd "$tree"

  # Per-session state, so a turn that changed no code since the last clean verdict costs nothing at
  # all: the verdict (its state, the HEAD it was taken at, and the changed-code listing it covered)
  # and the round count for the prompt in flight. LOADBEARING_HOOK_STATE_DIR moves the root, which is
  # how the tests keep out of the real temp directory.
  session_id=$(hook_field session_id)
  state_dir="${LOADBEARING_HOOK_STATE_DIR:-${TMPDIR:-/tmp}}/loadbearing-hook/${session_id:-nosession}"
  verdict_file="$state_dir/verdict"
  rounds_file="$state_dir/rounds"
  mkdir -p "$state_dir" 2>/dev/null && [ -w "$state_dir" ] && stateful=1

  head_sha=$(git rev-parse HEAD 2>/dev/null)
  listing=$(git status --porcelain=v1 -uall 2>/dev/null | grep -Ei '\.(cs|csproj|props|targets|sln|slnx|razor|cshtml)"?$')

  # The skip, and the whole reason a question-and-answer turn is free: the last verdict still stands
  # if it was green, was taken at this HEAD, covered this listing, and nothing it covered has been
  # written since. A listing alone cannot see a file edited twice; the file's own timestamp can. Any
  # doubt — no state, no HEAD, a verdict that will not parse — runs the check. It never skips a red.
  if [ "$stateful" = 1 ] && [ -n "$head_sha" ] && [ -f "$verdict_file" ]; then
    recorded=$(cat "$verdict_file" 2>/dev/null)
    if [ "$(printf '%s\n' "$recorded" | sed -n 1p)" = 'green' ] &&
      [ "$(printf '%s\n' "$recorded" | sed -n 2p)" = "$head_sha" ] &&
      [ "$(printf '%s\n' "$recorded" | sed -n '3,$p')" = "$listing" ] &&
      unwritten_since "$verdict_file"; then
      exit 0
    fi
  fi

  # Consecutive continuations for this prompt: a block and a warning passthrough both count, a silent
  # green resets. At the cap the hook says its piece and lets the turn end, so a rule the agent cannot
  # satisfy stops being a loop and becomes something the user reads. With nowhere to keep the count,
  # the payload's own stop_hook_active is the one round it can carry.
  prompt_id=$(hook_field prompt_id)
  [ -n "$prompt_id" ] || prompt_id='noprompt'
  MAX_ROUNDS="${LOADBEARING_HOOK_MAX_ROUNDS:-3}"
  rounds=0
  if [ "$stateful" = 1 ]; then
    [ -f "$rounds_file" ] && counted=$(cat "$rounds_file" 2>/dev/null)
    case "${counted:-}" in "$prompt_id "*) rounds=${counted##* } ;; esac
    case "$rounds" in '' | *[!0-9]*) rounds=0 ;; esac
    [ "$rounds" -ge "$MAX_ROUNDS" ] && stand_down='report'
  elif [ "$(hook_flag stop_hook_active)" = 'true' ]; then
    stand_down='silent'
  fi
else
  # The check reads code, so an edit to anything else (docs, config, lockfiles) skips it; with no
  # payload (a hand-run), it runs.
  edited_file=$(hook_path file_path)
  if [ -n "$edited_file" ]; then
    case "$(printf '%s' "$edited_file" | tr '[:upper:]' '[:lower:]')" in
      *.cs | *.csproj | *.props | *.targets | *.sln | *.slnx | *.razor | *.cshtml) ;;
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
        /* | [A-Za-z]:/*)
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
fi

SOLUTION="${SOLUTION:-Zphil.LoadBearing.slnx}"
SPEC="${SPEC:-arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj}"
DIFF_BASE="${DIFF_BASE:-HEAD}"

out=$(loadbearing check "$SOLUTION" --spec "$SPEC" --diff-base "$DIFF_BASE" --hook-json --hook-event "$hook_event" 2>&1)
code=$?
case "$code" in
  0)
    record green
    if [ -z "$out" ]; then
      forget_rounds
      exit 0 # nothing to say, and a hook that speaks anyway is a hook people turn off
    fi
    [ -n "$stand_down" ] && exit 0
    count_round
    printf '%s\n' "$out"
    exit 0 # tripwire warnings reach the agent as context, once
    ;;
  1)
    record red
    if [ "$stand_down" = 'report' ]; then
      printf 'loadbearing: still red after %s rounds; not blocking again.\n%s\n' "$MAX_ROUNDS" "$out" >&2
      exit 1 # the user reads it and the turn ends; Claude Code's own progress override sits behind this
    fi
    [ "$stand_down" = 'silent' ] && exit 0
    count_round
    printf '%s\n' "$out" >&2
    exit 2 # violations -> block
    ;;
  *) printf 'loadbearing config error:\n%s\n' "$out" >&2; exit 1 ;;
esac
