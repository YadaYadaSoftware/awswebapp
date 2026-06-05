#Requires -Version 5.1
<#
.SYNOPSIS
    Lists the "brothers" - sibling work folders of this repo - and what each is doing.

.DESCRIPTION
    Each parallel feature is developed in its own sibling folder under the family
    directory (the parent of this repo's root), named after a German brother
    (e.g. wilhelm, friedrich). See BROTHERS.md.

    This script scans the family directory for sibling checkouts (git worktrees or
    full clones) and reports, for each, the checked-out branch, working-tree state,
    last commit, ahead/behind vs origin, and any free-text note left in a
    `.brother-status` file.

    Clones and worktrees are treated identically (both have a `.git` entry, file or
    directory).

.PARAMETER IncludeSelf
    Include the current folder in the listing (marked with *). By default the
    current brother is omitted so the output answers "what are my BROTHERS doing".

.EXAMPLE
    .\scripts\Get-Brothers.ps1
    Lists every other brother and what they're working on.

.EXAMPLE
    .\scripts\Get-Brothers.ps1 -IncludeSelf
    Lists all brothers including this one.
#>
[CmdletBinding()]
param(
    [switch]$IncludeSelf
)

$ErrorActionPreference = 'Stop'

# Resolve this folder's repo root, then the family directory (its parent).
$repoRoot = & git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) {
    Write-Error "Not inside a git repository."
    return
}
$repoRoot = ($repoRoot | Select-Object -First 1).Trim()
$self     = Split-Path $repoRoot -Leaf
$family   = Split-Path $repoRoot -Parent

$rows = @()
foreach ($dir in Get-ChildItem -Path $family -Directory) {
    # A git checkout has a `.git` entry: a directory (clone) or a file (worktree).
    if (-not (Test-Path (Join-Path $dir.FullName '.git'))) { continue }

    $name   = $dir.Name
    $isSelf = ($name -eq $self)
    if ($isSelf -and -not $IncludeSelf) { continue }

    $branch = & git -C $dir.FullName rev-parse --abbrev-ref HEAD 2>$null
    if ($branch) { $branch = $branch.Trim() } else { $branch = '(detached)' }

    $subject = & git -C $dir.FullName log -1 --format='%s' 2>$null
    $when    = & git -C $dir.FullName log -1 --format='%cr' 2>$null
    if ($subject) { $lastCommit = "$($subject.Trim()) ($($when.Trim()))" } else { $lastCommit = '(no commits)' }

    $dirtyCount = (& git -C $dir.FullName status --porcelain 2>$null | Measure-Object -Line).Lines
    if ($dirtyCount -gt 0) { $tree = "$dirtyCount uncommitted" } else { $tree = 'clean' }

    # Ahead/behind vs the branch's upstream, if any.
    $sync = ''
    $counts = & git -C $dir.FullName rev-list --left-right --count "@{upstream}...HEAD" 2>$null
    if ($counts) {
        $parts = ($counts.Trim() -split '\s+')
        if ($parts.Count -eq 2) {
            $behind = [int]$parts[0]; $ahead = [int]$parts[1]
            if ($ahead -gt 0) { $sync += "+$ahead" }
            if ($behind -gt 0) { $sync += "-$behind" }
        }
    }
    if (-not $sync) { $sync = 'in sync' }

    # Optional free-text note the brother left for the family.
    $note = ''
    $statusFile = Join-Path $dir.FullName '.brother-status'
    if (Test-Path $statusFile) {
        $note = ((Get-Content $statusFile -Raw -ErrorAction SilentlyContinue) | Out-String).Trim()
    }

    $marker = ''
    if ($isSelf) { $marker = '(self)' }

    $rows += [pscustomobject]@{
        Brother    = $name
        Who        = $marker
        Branch     = $branch
        Tree       = $tree
        Sync       = $sync
        LastCommit = $lastCommit
        Note       = $note
    }
}

if (-not $rows) {
    Write-Host "No brothers found under $family."
    return
}

$rows | Sort-Object Brother | Format-Table -AutoSize -Wrap
