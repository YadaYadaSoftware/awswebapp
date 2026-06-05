#Requires -Version 5.1
<#
.SYNOPSIS
    Works out "what should I do next" for this brother (work folder). See BROTHERS.md.

.DESCRIPTION
    Two modes, chosen by whether this folder's branch leaf names an active OpenSpec
    change under openspec/changes/:

    - ON-SPEC: you're implementing a change. Reports task progress, the next unchecked
      task and the section it sits under (so the caller can name the step - writing
      code, testing, deploying, ...), and the working-tree state.

    - IDLE: you're not on a change. Lists every active change with who (if anyone) is
      working it - by cross-referencing each brother's branch leaf - its task
      progress, and whether it's effectively done. Surfaces the changes no brother
      has claimed, so the family doesn't double up on the same spec.

    Emits readable text with `NEXT-MODE:` / `KEY=value` markers for the caller
    (the /next command) to narrate and recommend from. Read-only.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_BrothersCommon.ps1"

$ctx        = Get-FamilyRoot
$repoRoot   = $ctx.RepoRoot
$self       = $ctx.Self
$branch     = (& git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null)
if ($branch) { $branch = $branch.Trim() } else { $branch = '(detached)' }
$branchLeaf = ($branch -split '/')[-1]

$changesDir = Join-Path $repoRoot 'openspec\changes'
$archiveDir = Join-Path $changesDir 'archive'

# Active changes = subdirectories of openspec/changes except the archive folder.
$activeChanges = @()
if (Test-Path $changesDir) {
    $activeChanges = @(
        Get-ChildItem $changesDir -Directory |
            Where-Object { $_.Name -ne 'archive' } |
            ForEach-Object { $_.Name }
    )
}

function Get-TaskProgress {
    param([string]$ChangeName)
    $tasks = Join-Path $changesDir (Join-Path $ChangeName 'tasks.md')
    if (-not (Test-Path $tasks)) { return $null }
    $section = ''; $next = ''; $nextSection = ''; $done = 0; $todo = 0
    foreach ($l in (Get-Content $tasks)) {
        if     ($l -match '^##\s+(.*)')   { $section = $matches[1].Trim() }
        elseif ($l -match '^\s*- \[x\]')  { $done++ }
        elseif ($l -match '^\s*- \[ \]')  {
            $todo++
            if (-not $next) {
                $next = ($l -replace '^\s*- \[ \]\s*', '').Trim()
                $nextSection = $section
            }
        }
    }
    [pscustomobject]@{
        Done = $done; Todo = $todo; Total = ($done + $todo)
        NextTask = $next; NextSection = $nextSection
    }
}

$onSpec = ($activeChanges -contains $branchLeaf)

Write-Host "BROTHER=$self"
Write-Host "BRANCH=$branch"

if ($onSpec) {
    $p = Get-TaskProgress $branchLeaf
    $dirty = (& git -C $repoRoot status --porcelain 2>$null | Measure-Object -Line).Lines
    Write-Host "NEXT-MODE: on-spec"
    Write-Host "CHANGE=$branchLeaf"
    if ($p) {
        Write-Host "PROGRESS=$($p.Done)/$($p.Total) tasks complete"
        if ($p.Todo -gt 0) {
            Write-Host "NEXT-SECTION=$($p.NextSection)"
            Write-Host "NEXT-TASK=$($p.NextTask)"
        } else {
            Write-Host "NEXT-TASK=(all tasks complete - validate, then archive with /opsx:archive)"
        }
    } else {
        Write-Host "PROGRESS=(no tasks.md found for this change)"
    }
    Write-Host "WORKING-TREE=$dirty uncommitted file(s)"
    return
}

# --- IDLE: not on a change. Who's working what? ---
Write-Host "NEXT-MODE: idle"

# Map branch leaf -> brother for every checkout (including self) so we can tell
# which changes are already claimed.
$claimByLeaf = @{}
foreach ($c in (Get-FamilyCheckouts -IncludeSelf)) {
    if (-not $claimByLeaf.ContainsKey($c.BranchLeaf)) { $claimByLeaf[$c.BranchLeaf] = $c.Name }
}

$archived = @()
if (Test-Path $archiveDir) {
    # Archive folder names are date-prefixed (e.g. 2026-06-05-foo); match loosely.
    $archived = @(Get-ChildItem $archiveDir -Directory | ForEach-Object { $_.Name })
}

Write-Host ""
Write-Host ("{0,-38} {1,-12} {2,-12} {3}" -f 'CHANGE', 'PROGRESS', 'CLAIMED-BY', 'STATE')
Write-Host ("{0,-38} {1,-12} {2,-12} {3}" -f ('-'*38), ('-'*12), ('-'*12), '-----')

$available = @()
foreach ($ch in ($activeChanges | Sort-Object)) {
    $p = Get-TaskProgress $ch
    $prog = if ($p) { "$($p.Done)/$($p.Total)" } else { 'n/a' }
    $claim = if ($claimByLeaf.ContainsKey($ch)) { $claimByLeaf[$ch] } else { '-' }

    $state = 'available'
    if ($claimByLeaf.ContainsKey($ch)) {
        $state = 'in progress'
    } elseif ($p -and $p.Total -gt 0 -and $p.Todo -eq 0) {
        $state = 'done -> archive'
    } else {
        $available += [pscustomobject]@{ Change = $ch; Done = ($p.Done); Total = ($p.Total) }
    }

    Write-Host ("{0,-38} {1,-12} {2,-12} {3}" -f $ch, $prog, $claim, $state)
}

Write-Host ""
if ($available.Count -gt 0) {
    Write-Host "AVAILABLE (no brother on these, not yet done):"
    foreach ($a in $available) { Write-Host "  - $($a.Change)  [$($a.Done)/$($a.Total) done]" }
} else {
    Write-Host "AVAILABLE=(none - every change is claimed or already done)"
}
