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

# Hooks run in the session's current directory, which need not be the repository root;
# Claude Code sets CLAUDE_PROJECT_DIR to the root for every hook it fires.
if ($env:CLAUDE_PROJECT_DIR) { Set-Location $env:CLAUDE_PROJECT_DIR }

# The PostToolUse payload on stdin names the edited file. The check reads code, so an edit to
# anything else (docs, config, lockfiles) skips it; with no payload (a hand-run), it runs.
$editedFile = $null
if ([Console]::IsInputRedirected) {
    try { $editedFile = ([Console]::In.ReadToEnd() | ConvertFrom-Json).tool_input.file_path } catch { }
}
if ($editedFile -and $editedFile -notmatch '\.(cs|csproj|props|targets|sln|slnx|razor|cshtml)$') { exit 0 }

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
