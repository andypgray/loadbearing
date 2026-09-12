# LoadBearing architecture hook for the Meridian example (PowerShell).
#
# This is the Meridian instantiation of the LoadBearing agent-hook recipe, filled in with
# Meridian's paths. The scenario it drives, and how to lift it into your own repo, are in
# storyboard.md beside this file.
#
# Exit-code mapping (the one thing to get right): LoadBearing and Claude Code use different
# conventions. Clean check -> 0 (the turn ends). A red rule -> 2, which is how a Claude Code hook
# refuses a stop, with the violation report on stderr so the agent reads the rule ID, reason, fix,
# and file:line and keeps working. LoadBearing's own error (bad ref, unresolvable spec) -> 1, a
# non-blocking config problem the user sees rather than an architecture violation the agent
# is told to "fix".
#
# Exit 0 is not silence: --hook-json makes a clean check write its report as the fired event's
# additional context when a tripwire warned, and this wrapper passes that through on stdout, where
# Claude Code turns it into a transcript message. A clean check with nothing to say writes nothing.
#
# Lift this into your own repo: copy it to .claude/arch-hook.ps1 and change the three values
# below to your solution, your built spec assembly, and the ref you diff against.

# The hook payload on stdin says which event fired and what it touched. Read it whole and once,
# because stdin does not rewind and every gate below answers from it.
$payload = $null
if ([Console]::IsInputRedirected) {
    try { $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { }
}

# A path out of the payload, with its separators turned forward (ConvertFrom-Json has already undone
# the JSON escaping), so everything downstream compares one spelling.
function Get-HookPath($value) {
    if ($value) { return $value.Replace('\', '/') }
    return $null
}

# True when nothing the listing names has been written since $Since. Timestamps rather than a status
# line: a listing cannot see a file edited twice, and the file's own does. A path the listing names
# and the tree no longer has is a deletion, which the listing itself already carries.
function Test-UnwrittenSince($listing, $since) {
    foreach ($entry in $listing) {
        $path = $entry.Substring(3)
        if ($path -match ' -> (.*)$') { $path = $Matches[1] }
        $path = $path.Trim('"')
        $item = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($item -and $item.LastWriteTimeUtc -gt $since) { return $false }
    }
    return $true
}

# Stop and SubagentStop are the turn boundary: the agent has said it is done, so the check reads the
# working tree rather than one tool's payload — which is also how it covers the edits no matcher
# sees, the ones a shell or a language server made. Any other event, and a hand run, takes the
# per-edit path instead, which is what an existing PostToolUse wiring still gets.
$hookEvent = if ($payload.hook_event_name) { $payload.hook_event_name } else { 'PostToolUse' }
$turnEnd = $hookEvent -in @('Stop', 'SubagentStop')
# What counts as code, spelled once for both branches below: the turn-end listing filter and the
# per-edit payload gate ask the same question of different subjects, and a new extension has to
# reach both. Anchored at the end, so foo.csx is not a match; the quote is optional because that is
# how git status spells a path that needs quoting, and -match folds the case on its own.
$codeFile = '\.(cs|csproj|props|targets|sln|slnx|razor|cshtml)"?$'
# Empty while the hook may still block. 'report' is the counted cap: say so once and let the turn
# end. 'silent' is the degraded mode with nowhere to count — the payload says this hook has already
# blocked in this turn, which is the single round it can carry, so the turn ends without a word.
$standDown = ''
$stateful = $false

if ($turnEnd) {
    # Check the tree the session is in. The payload names the directory it ran from and git names the
    # working tree that directory belongs to, so a session inside a linked worktree checks its own
    # worktree rather than the checkout CLAUDE_PROJECT_DIR points at; the three config values below
    # are root-relative, which is what lets one wrapper serve both.
    $tree = $null
    $sessionCwd = Get-HookPath $payload.cwd
    if ($sessionCwd) { $tree = git -C $sessionCwd rev-parse --show-toplevel 2>$null }
    if (-not $tree) { $tree = $env:CLAUDE_PROJECT_DIR }
    if ($tree) { Set-Location $tree }

    # Per-session state, so a turn that changed no code since the last clean verdict costs nothing at
    # all: the verdict (its state, the HEAD it was taken at, and the changed-code listing it covered)
    # and the round count for the prompt in flight. LOADBEARING_HOOK_STATE_DIR moves the root, which
    # is how the tests keep out of the real temp directory.
    $sessionId = if ($payload.session_id) { $payload.session_id } else { 'nosession' }
    $stateRoot = if ($env:LOADBEARING_HOOK_STATE_DIR) { $env:LOADBEARING_HOOK_STATE_DIR } else { [IO.Path]::GetTempPath() }
    $stateDir = Join-Path (Join-Path $stateRoot 'loadbearing-hook') $sessionId
    $verdictFile = Join-Path $stateDir 'verdict'
    $roundsFile = Join-Path $stateDir 'rounds'
    # Asked for, then checked: New-Item -Force under a path whose parent is a file writes nothing and
    # raises nothing, so the directory's own existence is the only answer worth reading.
    New-Item -ItemType Directory -Force -Path $stateDir -ErrorAction SilentlyContinue | Out-Null
    $stateful = Test-Path -LiteralPath $stateDir -PathType Container

    $headSha = git rev-parse HEAD 2>$null
    $listing = @(git status --porcelain=v1 -uall 2>$null | Where-Object { $_ -match $codeFile })

    # The skip, and the whole reason a question-and-answer turn is free: the last verdict still stands
    # if it was green, was taken at this HEAD, covered this listing, and nothing it covered has been
    # written since. Any doubt — no state, no HEAD, a verdict that will not parse — runs the check. It
    # never skips a red.
    if ($stateful -and $headSha -and (Test-Path -LiteralPath $verdictFile)) {
        $recorded = @(Get-Content -LiteralPath $verdictFile -ErrorAction SilentlyContinue)
        $since = (Get-Item -LiteralPath $verdictFile).LastWriteTimeUtc
        if ($recorded.Count -ge 2 -and $recorded[0] -eq 'green' -and $recorded[1] -eq $headSha `
                -and (($recorded | Select-Object -Skip 2) -join "`n") -eq ($listing -join "`n") `
                -and (Test-UnwrittenSince $listing $since)) {
            exit 0
        }
    }

    # Consecutive continuations for this prompt: a block and a warning passthrough both count, a
    # silent green resets. At the cap the hook says its piece and lets the turn end, so a rule the
    # agent cannot satisfy stops being a loop and becomes something the user reads. With nowhere to
    # keep the count, the payload's own stop_hook_active is the one round it can carry.
    $promptId = if ($payload.prompt_id) { $payload.prompt_id } else { 'noprompt' }
    $maxRounds = if ($env:LOADBEARING_HOOK_MAX_ROUNDS) { [int]$env:LOADBEARING_HOOK_MAX_ROUNDS } else { 3 }
    $rounds = 0
    if ($stateful) {
        $counted = Get-Content -LiteralPath $roundsFile -First 1 -ErrorAction SilentlyContinue
        if ($counted -match "^$([regex]::Escape($promptId)) (\d+)$") { $rounds = [int]$Matches[1] }
        if ($rounds -ge $maxRounds) { $standDown = 'report' }
    }
    elseif ($payload.stop_hook_active) { $standDown = 'silent' }
}
else {
    # The check reads code, so an edit to anything else (docs, config, lockfiles) skips it; with no
    # payload (a hand-run), it runs.
    $editedFile = Get-HookPath $payload.tool_input.file_path
    if ($editedFile -and $editedFile -notmatch $codeFile) { exit 0 }

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
            # A relative payload path is project-relative. An absolute one outside the project
            # directory belongs to no tree this hook speaks for, and need not be in a repository at
            # all: a scratch directory is the everyday case. That compare ignores case, so the paths
            # it rules out are the ones plainly somewhere else.
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
}

$Solution = if ($env:SOLUTION)  { $env:SOLUTION }  else { 'examples/Meridian/Meridian.slnx' }
$Spec     = if ($env:SPEC)      { $env:SPEC }      else { 'examples/Meridian/arch/Meridian.ArchSpec/bin/Debug/net10.0/Meridian.ArchSpec.dll' }
$DiffBase = if ($env:DIFF_BASE) { $env:DIFF_BASE } else { 'HEAD' }

$out = loadbearing check $Solution --spec $Spec --diff-base $DiffBase --hook-json --hook-event $hookEvent 2>&1
$code = $LASTEXITCODE
# Multi-line output lands in $out as an array; written raw, stderr would carry the array's
# type name instead of the report. Join first.
$report = $out -join "`n"

# The three writes that make the next stop cheap. Each is a no-op wherever there is no state to keep
# — a per-edit firing, or a state directory that would not open — so the exit mapping below reads as
# one contract rather than two.
function Write-Verdict($state) {
    if (-not $stateful) { return }
    Set-Content -LiteralPath $verdictFile -Value (@($state, $headSha) + $listing) -ErrorAction SilentlyContinue
}
function Add-Round {
    if (-not $stateful) { return }
    Set-Content -LiteralPath $roundsFile -Value "$promptId $($rounds + 1)" -ErrorAction SilentlyContinue
}
function Clear-Rounds {
    if (-not $stateful) { return }
    Remove-Item -LiteralPath $roundsFile -Force -ErrorAction SilentlyContinue
}

switch ($code) {
    0 {
        Write-Verdict 'green'
        # Nothing to say, and a hook that speaks anyway is a hook people turn off.
        if (-not $report) { Clear-Rounds; exit 0 }
        if ($standDown) { exit 0 }
        Add-Round
        [Console]::Out.WriteLine($report) # warnings reach the agent as context, once
        exit 0
    }
    1 {
        Write-Verdict 'red'
        if ($standDown -eq 'report') {
            # The user reads it and the turn ends; Claude Code's own progress override sits behind this.
            [Console]::Error.WriteLine("loadbearing: still red after $maxRounds rounds; not blocking again.`n$report")
            exit 1
        }
        if ($standDown -eq 'silent') { exit 0 }
        Add-Round
        [Console]::Error.WriteLine($report) # violations -> block, feed the report back to the agent
        exit 2
    }
    default { [Console]::Error.WriteLine("loadbearing config error:`n$report"); exit 1 }
}
