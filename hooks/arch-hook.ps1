# LoadBearing architecture hook for this repository (PowerShell).
#
# This is LoadBearing's own instantiation of the agent-hook recipe: the tool checking its own
# solution against its own spec after every agent code edit. The report it produces on a red rule is
# the one quoted on the landing page.
#
# Exit-code mapping (the one thing to get right): LoadBearing and Claude Code use different
# conventions. Clean check -> 0 (proceed). A red rule -> 2, which is how a Claude Code hook
# blocks, with the violation report on stderr so the agent reads the rule ID, reason, fix, and
# file:line and self-corrects. LoadBearing's own error (bad ref, unresolvable spec) -> 1, a
# non-blocking config problem the user sees rather than an architecture violation the agent
# is told to "fix".
#
# Lift this into your own repo: copy it to .claude/arch-hook.ps1 and change the three values
# below to your solution, your spec project, and the ref you diff against.

# The PostToolUse payload on stdin names the edited file. Read it once, because stdin does not
# rewind and both gates below answer from it, and normalise the path it carries to forward slashes
# with its JSON escaping undone, so everything downstream compares one spelling.
$editedFile = $null
if ([Console]::IsInputRedirected) {
    try { $editedFile = ([Console]::In.ReadToEnd() | ConvertFrom-Json).tool_input.file_path } catch { }
}
if ($editedFile) { $editedFile = $editedFile.Replace('\', '/') }

# The check reads code, so an edit to anything else (docs, config, lockfiles) skips it; with no
# payload (a hand-run), it runs.
if ($editedFile -and $editedFile -notmatch '\.(cs|csproj|props|targets|sln|slnx|razor|cshtml)$') { exit 0 }

# Check the tree the edit landed in, not the tree this process happens to sit in. The hook runs in
# the session's directory, and neither that nor CLAUDE_PROJECT_DIR need be where the edit went: an
# agent working inside a linked worktree, or a session in the main checkout writing to a worktree
# by absolute path, would otherwise spend a full check on a tree the edit never touched and report
# a verdict about the wrong code. With no payload the current directory stands in for the edited
# file. Every fallback here runs the check rather than skipping it: when in doubt, check.
if ($env:CLAUDE_PROJECT_DIR) {
    $projectDir = $env:CLAUDE_PROJECT_DIR.Replace('\', '/').TrimEnd('/')
    $editedDir = '.'
    if ($editedFile) {
        # A relative payload path is project-relative. An absolute one outside the project directory
        # belongs to no tree this hook speaks for, and need not be in a repository at all: a scratch
        # directory is the everyday case. That compare ignores case, so the paths it rules out are
        # the ones plainly somewhere else.
        $editedDir = if ($editedFile -match '^(.*)/') { $Matches[1] } else { '.' }
        if ($editedDir -match '^(/|[A-Za-z]:/)') {
            if (-not "$editedDir/".StartsWith("$projectDir/", 'OrdinalIgnoreCase')) { exit 0 }
        }
        else { $editedDir = "$projectDir/$editedDir" }
    }

    # Inside by path is not inside by tree: a linked worktree lives under the project directory.
    # rev-parse --show-toplevel names the worktree's own root, so the two answers differ exactly
    # there, and asking git for both spells them the same way from any depth on any platform.
    $editedRoot = git -C $editedDir rev-parse --show-toplevel 2>$null
    $projectRoot = git -C $projectDir rev-parse --show-toplevel 2>$null
    if ($editedRoot -and $projectRoot -and $editedRoot -ne $projectRoot) { exit 0 }
}

# Hooks run in the session's current directory, which need not be the repository root;
# Claude Code sets CLAUDE_PROJECT_DIR to the root for every hook it fires.
if ($env:CLAUDE_PROJECT_DIR) { Set-Location $env:CLAUDE_PROJECT_DIR }

$Solution = if ($env:SOLUTION)  { $env:SOLUTION }  else { 'Zphil.LoadBearing.slnx' }
$Spec     = if ($env:SPEC)      { $env:SPEC }      else { 'arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj' }
$DiffBase = if ($env:DIFF_BASE) { $env:DIFF_BASE } else { 'HEAD' }

$out = loadbearing check $Solution --spec $Spec --diff-base $DiffBase 2>&1
$code = $LASTEXITCODE
# Multi-line output lands in $out as an array; written raw, stderr would carry the array's
# type name instead of the report. Join first.
$report = $out -join "`n"
switch ($code) {
    0 { exit 0 }                                      # clean (tripwire warnings, if any, are informational)
    1 { [Console]::Error.WriteLine($report); exit 2 } # violations -> block, feed the report back to the agent
    default { [Console]::Error.WriteLine("loadbearing config error:`n$report"); exit 1 }
}
