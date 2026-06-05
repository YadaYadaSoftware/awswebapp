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
      has claimed, so the family doesn't double up on the same spec - AND scores each
      such change for collision risk against the brothers' in-flight changes (shared
      OpenSpec capability = HIGH; shared code paths in .github/, infrastructure/,
      scripts/, src/ = RISK), ordering them safest-first and recommending one that
      overlaps nothing in flight, so two brothers don't fight over the same files.

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

function Get-ChangeFootprint {
    <#
        What a change "touches", for collision detection between brothers:
          - Capabilities: the spec-delta folders under the change's specs/ dir. Two
            changes that touch the same capability edit the same spec file - a hard
            conflict, regardless of code.
          - Paths: every repo code path the change's markdown references (.github/,
            infrastructure/, scripts/, src/). A crude but effective proxy for "which
            files this change will edit"; if two changes name the same workflow,
            template, or source tree, working them in parallel risks a messy merge.
        openspec/ links are deliberately ignored as Paths - spec-level overlap is
        already captured by Capabilities, and cross-links between proposals are noise.
    #>
    param([string]$ChangeName)
    $dir = Join-Path $changesDir $ChangeName

    $caps = @()
    $specsDir = Join-Path $dir 'specs'
    if (Test-Path $specsDir) {
        $caps = @(Get-ChildItem $specsDir -Directory | ForEach-Object { $_.Name })
    }

    $paths = New-Object 'System.Collections.Generic.HashSet[string]'
    $rx = [regex]'(?:src|infrastructure|scripts|\.github)/[A-Za-z0-9._/-]+'
    foreach ($f in (Get-ChildItem $dir -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue)) {
        $raw = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $raw) { continue }
        foreach ($m in $rx.Matches($raw)) {
            $p = $m.Value.TrimEnd('/', ')', '`', ',', '.').ToLowerInvariant()
            if ($p.Length -ge 6) { [void]$paths.Add($p) }
        }
    }

    [pscustomobject]@{
        Capabilities = $caps
        Paths        = @($paths)
    }
}

function Get-SharedPaths {
    <# Paths in A that equal, contain, or are contained by a path in B (so a file
       reference and the directory it lives in count as overlapping). #>
    param([string[]]$A, [string[]]$B)
    $shared = New-Object 'System.Collections.Generic.List[string]'
    foreach ($x in $A) {
        foreach ($y in $B) {
            if ($x -eq $y -or $x.StartsWith("$y/") -or $y.StartsWith("$x/")) {
                $rep = if ($x.Length -le $y.Length) { $x } else { $y }
                if (-not $shared.Contains($rep)) { $shared.Add($rep) }
            }
        }
    }
    @($shared)
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

# Footprints (capabilities + touched code paths) for every active change, used to
# detect whether an available change would collide with one a brother is already on.
$footprints = @{}
foreach ($ch in $activeChanges) { $footprints[$ch] = Get-ChangeFootprint $ch }

# The changes a brother is actively on - an available pick should avoid overlapping
# these so two brothers don't edit the same area and create a painful merge.
$claimedChanges = @($activeChanges | Where-Object { $claimByLeaf.ContainsKey($_) })

function Get-Collisions {
    <# Overlaps between $Change and every brother-claimed change. HIGH = shared
       capability (same spec file); RISK = shared code paths only. #>
    param([string]$Change)
    $mine = $footprints[$Change]
    $hits = @()
    foreach ($cc in $claimedChanges) {
        if ($cc -eq $Change) { continue }
        $theirs = $footprints[$cc]
        $sharedCaps  = @($mine.Capabilities | Where-Object { $theirs.Capabilities -contains $_ })
        $sharedPaths = Get-SharedPaths $mine.Paths $theirs.Paths
        if ($sharedCaps.Count -gt 0 -or $sharedPaths.Count -gt 0) {
            $hits += [pscustomobject]@{
                Change      = $cc
                Brother     = $claimByLeaf[$cc]
                Level       = if ($sharedCaps.Count -gt 0) { 'HIGH' } else { 'RISK' }
                SharedCaps  = $sharedCaps
                SharedPaths = $sharedPaths
            }
        }
    }
    $hits
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
        $available += [pscustomobject]@{
            Change = $ch; Done = ($p.Done); Total = ($p.Total)
            Collisions = (Get-Collisions $ch)
        }
    }

    Write-Host ("{0,-38} {1,-12} {2,-12} {3}" -f $ch, $prog, $claim, $state)
}

Write-Host ""
if ($available.Count -gt 0) {
    # Clear (no-collision) picks first; then RISK; HIGH last - so the first listed
    # is always the safest one to start in parallel.
    $rank = { if (-not $_.Collisions -or $_.Collisions.Count -eq 0) { 0 }
              elseif ($_.Collisions.Level -contains 'HIGH') { 2 } else { 1 } }
    $available = @($available | Sort-Object @{ Expression = $rank }, Change)

    Write-Host "AVAILABLE (no brother on these, not yet done):"
    foreach ($a in $available) {
        if (-not $a.Collisions -or $a.Collisions.Count -eq 0) {
            Write-Host "  - $($a.Change)  [$($a.Done)/$($a.Total) done]  conflict-risk: none"
        } else {
            $top = ($a.Collisions | Sort-Object @{ Expression = { if ($_.Level -eq 'HIGH') { 0 } else { 1 } } })[0]
            Write-Host "  - $($a.Change)  [$($a.Done)/$($a.Total) done]  conflict-risk: $($top.Level)"
            foreach ($c in $a.Collisions) {
                $cap  = if ($c.SharedCaps.Count)  { 'capability ' + ($c.SharedCaps  -join ', ') } else { '' }
                $pth  = if ($c.SharedPaths.Count) { 'files ' + ($c.SharedPaths -join ', ') } else { '' }
                $what = (@($cap, $pth) | Where-Object { $_ }) -join '; '
                Write-Host "      vs $($c.Change) ($($c.Brother)) [$($c.Level)] - shared $what"
                Write-Host "COLLISION change=$($a.Change) level=$($c.Level) with=$($c.Change) brother=$($c.Brother) sharedCaps=$($c.SharedCaps -join '|') sharedFiles=$($c.SharedPaths -join '|')"
            }
        }
    }
    $clear = @($available | Where-Object { -not $_.Collisions -or $_.Collisions.Count -eq 0 })
    if ($clear.Count -gt 0) {
        Write-Host ""
        Write-Host "RECOMMEND=$($clear[0].Change) (no overlap with any brother's in-flight change)"
    } else {
        Write-Host ""
        Write-Host "RECOMMEND=(every available change overlaps an in-flight one - see conflict-risk above; pick the lowest-risk or coordinate with that brother)"
    }
} else {
    Write-Host "AVAILABLE=(none - every change is claimed or already done)"
}
