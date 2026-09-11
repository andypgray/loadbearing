# The agent loop, closed by a hook

This directory exists for [storyboard.md](storyboard.md): a coding agent working a routine
task writes the pattern Meridian is retiring, and the architecture rule reaches it before the
turn is over. The storyboard walks that loop beat by beat with real captured output, from the
task brief to the refused stop to the self-correction that lands instead. A fifth beat walks
the other outcome, the one an exit code cannot carry: an edit the rules allow, into the
quarantined clearance engine, where the tripwire warns rather than blocks and the warning
reaches the agent as hook context.

Around it sit the pieces that make the loop run: two wrapper scripts, one PowerShell and
one POSIX, that map a red check to the exit code a Claude Code hook treats as blocking, and
`settings.snippet.json`, the paste-in that wires the `Stop` and `SubagentStop` entries into a
repo's own Claude Code configuration. The storyboard's file table says what each one is and how to lift it into
your repo.

The general recipe these wrappers instantiate lives at the repository root under
[hooks/](../../../hooks/), and the codebase they guard is [Meridian itself](../README.md).
