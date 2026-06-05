#Requires -Version 5.1
<#
.SYNOPSIS
    Claude Code status line for the brothers family (see BROTHERS.md).

.DESCRIPTION
    Renders the prompt label as "<what-or-who>>":
      - If this folder's branch leaf names an active OpenSpec change
        (openspec/changes/<leaf> exists) -> the change name, e.g. "stabilize-ui-tests>".
      - Otherwise -> this brother's name (the folder leaf), e.g. "heinrich>".

    Claude Code pipes a JSON blob on stdin; we read workspace.current_dir from it and
    fall back to the process working directory. Best-effort and read-only: any failure
    degrades to the folder leaf rather than throwing, so the status line never breaks.
#>
$ErrorActionPreference = 'SilentlyContinue'

# Where are we? Prefer the dir Claude Code reports; fall back to cwd.
$dir = $null
try {
    # Only read stdin when it's actually piped - ReadToEnd() blocks forever otherwise
    # (so the script stays runnable by hand for testing).
    if ([Console]::IsInputRedirected) {
        $raw = [Console]::In.ReadToEnd()
        if ($raw) { $dir = (ConvertFrom-Json $raw).workspace.current_dir }
    }
} catch { }
if (-not $dir) { $dir = (Get-Location).Path }

$label = Split-Path $dir -Leaf   # safe default: the folder leaf

$repoRoot = & git -C $dir rev-parse --show-toplevel 2>$null
if ($repoRoot) {
    $repoRoot = ($repoRoot | Select-Object -First 1).Trim()
    $brother  = Split-Path $repoRoot -Leaf
    $label    = $brother

    $branch = & git -C $dir rev-parse --abbrev-ref HEAD 2>$null
    if ($branch) {
        $leaf = (($branch.Trim()) -split '/')[-1]
        # On an OpenSpec change? Branch leaf == an active changes/<name> folder.
        if ($leaf -and (Test-Path (Join-Path $repoRoot "openspec/changes/$leaf"))) {
            $label = $leaf
        }
    }
}

[Console]::Out.Write("$label>")
