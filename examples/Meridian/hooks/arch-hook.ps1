# LoadBearing architecture hook for the Meridian example (PowerShell).
#
# This is the Meridian instantiation of the LoadBearing agent-hook recipe, filled in with
# Meridian's paths. The scenario it drives, and how to lift it into your own repo, are in
# storyboard.md beside this file.
#
# Exit-code mapping (the one thing to get right): LoadBearing and Claude Code use different
# conventions. Clean check -> 0 (proceed). A red rule -> 2, which is how a Claude Code hook
# blocks, with the violation report on stderr so the agent reads the rule ID, reason, fix, and
# file:line and self-corrects. LoadBearing's own error (bad ref, unresolvable spec) -> 1, a
# non-blocking config problem the user sees rather than an architecture violation the agent
# is told to "fix".
#
# Lift this into your own repo: copy it to .claude/arch-hook.ps1 and change the three values
# below to your solution, your built spec assembly, and the ref you diff against.

$Solution = if ($env:SOLUTION)  { $env:SOLUTION }  else { 'examples/Meridian/Meridian.slnx' }
$Spec     = if ($env:SPEC)      { $env:SPEC }      else { 'examples/Meridian/arch/Meridian.ArchSpec/bin/Debug/net10.0/Meridian.ArchSpec.dll' }
$DiffBase = if ($env:DIFF_BASE) { $env:DIFF_BASE } else { 'HEAD' }

$out = loadbearing check $Solution --spec $Spec --diff-base $DiffBase 2>&1
$code = $LASTEXITCODE
switch ($code) {
    0 { exit 0 }                                   # clean (tripwire warnings, if any, are informational)
    1 { [Console]::Error.WriteLine($out); exit 2 } # violations -> block, feed the report back to the agent
    default { [Console]::Error.WriteLine("loadbearing config error:`n$out"); exit 1 }
}
