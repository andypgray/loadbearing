#!/bin/sh
# Runs `loadbearing mcp` from a copy of the CLI build output, so a warm server and `dotnet build`
# stop contending for the same files.
#
# Needed only when the server is run from a source checkout rather than the installed global tool.
# A live server holds open every assembly it loaded, and those are exactly the files the next build
# has to overwrite: `dotnet build` fails with MSB3021/MSB3027 copy errors for as long as a client is
# connected. Stopping the server clears that and costs more than it looks, because a stdio server
# whose connection dies is never reconnected — every hook armed against it then fails quietly for
# the rest of the session, which is a gate you no longer have and were not told about.
#
# So the server runs from a copy outside the build tree, keyed by the identity of the build it was
# taken from (modification time and size of loadbearing.dll). That key is what makes it safe with
# several sessions live at once: sessions on one build share a directory and copy nothing, a rebuild
# mints a new one, and no directory is ever overwritten while a server may still be running from it.
#
# This covers the server's own binaries. The spec assembly is the other half and needs nothing here:
# the server loads spec DLLs from their bytes rather than their paths, so the spec project's output
# stays replaceable while a client is connected.
#
# Use it in place of the command and the `mcp` verb; every argument is passed straight through.
#
#   {"mcpServers": {"loadbearing": {
#     "command": "sh",
#     "args": ["hooks/mcp-launch.sh", "MyApp.sln", "--spec", "./arch/bin/Debug/net10.0/MyApp.ArchSpec.dll"]}}}
#
# stdout is the JSON-RPC channel, so everything this script has to say goes to stderr instead.

# Relative paths below resolve from the launcher's working directory, which is the client's to
# choose; Claude Code names the repository root in CLAUDE_PROJECT_DIR.
[ -n "${CLAUDE_PROJECT_DIR:-}" ] && cd "$CLAUDE_PROJECT_DIR"

# The two values to set for your checkout: where the CLI was built, and where copies are kept.
BUILD_OUTPUT="${LOADBEARING_MCP_BUILD_OUTPUT:-src/Zphil.LoadBearing.Cli/bin/Debug/net10.0}"
RUN_ROOT="${LOADBEARING_MCP_RUN_ROOT:-$HOME/.loadbearing/mcp-run}"

{
  if [ ! -f "$BUILD_OUTPUT/loadbearing.dll" ]; then
    printf 'loadbearing mcp launcher: %s/loadbearing.dll not found; build the CLI first.\n' "$BUILD_OUTPUT"
    exit 1
  fi

  # The build's identity, from GNU stat and then from BSD/macOS stat. With neither, the copy is
  # skipped rather than keyed on a guess: a directory under a key that never changes would serve a
  # stale build for as long as it survives, which is worse than the lock this script exists to lift.
  stamp=$(stat -c '%Y_%s' "$BUILD_OUTPUT/loadbearing.dll" 2>/dev/null) \
    || stamp=$(stat -f '%m_%z' "$BUILD_OUTPUT/loadbearing.dll" 2>/dev/null) \
    || stamp=''

  if [ -z "$stamp" ]; then
    printf 'loadbearing mcp launcher: no usable stat here, so the build cannot be identified; running from the build output, which builds will contend with.\n'
    dest="$BUILD_OUTPUT"
  else
    dest="$RUN_ROOT/$stamp"
    mkdir -p "$RUN_ROOT"

    # Drop copies nothing has launched from in days. Age alone, so a directory is never removed
    # merely because a newer build exists; best-effort, and never fatal.
    find "$RUN_ROOT" -mindepth 1 -maxdepth 1 -type d -mtime +2 -exec rm -rf {} + 2>/dev/null

    # Populate through a private staging directory and rename into place, so a half-copied tree is
    # never visible at the destination and a session that loses the race just uses the winner's copy.
    if [ ! -f "$dest/loadbearing.dll" ]; then
      staging="$RUN_ROOT/.staging.$$"
      rm -rf "$staging"
      if mkdir -p "$staging" && cp -R "$BUILD_OUTPUT/." "$staging/"; then
        mv "$staging" "$dest" 2>/dev/null || rm -rf "$staging"
      else
        rm -rf "$staging"
      fi
    fi

    if [ -f "$dest/loadbearing.dll" ]; then
      # Mark the copy as launched from, so the sweep above ages copies by last use rather than by
      # when they were made: a build that stays current for a week is still in service on day three,
      # and a copy swept while a client is connected to it is one that cannot be fully deleted either.
      touch "$dest" 2>/dev/null
    else
      # The copy could not be made, so fall back to the build output. The lock comes back with it,
      # but a working server beats no server, and the reason is on stderr where the client shows it.
      printf 'loadbearing mcp launcher: could not stage a copy under %s; running from the build output, which builds will contend with.\n' "$RUN_ROOT"
      dest="$BUILD_OUTPUT"
    fi
  fi
} 1>&2

exec dotnet exec "$dest/loadbearing.dll" mcp "$@"
