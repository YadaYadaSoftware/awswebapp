#Requires -Version 5.1
<#
.SYNOPSIS
    Shared helpers for the "brothers" tooling (the parallel work folders - see BROTHERS.md).

.DESCRIPTION
    Single source of truth for: locating this folder's repo root + the family
    directory, and enumerating the sibling checkouts with their git state. Both
    Get-Brothers.ps1 (/brothers) and Get-NextStep.ps1 (/next) dot-source this so the
    enumeration logic isn't copy-pasted:

        . "$PSScriptRoot\_BrothersCommon.ps1"
#>

function Get-FamilyRoot {
    <# Returns the current repo root, this brother's name (leaf), and the family dir. #>
    $repoRoot = & git rev-parse --show-toplevel 2>$null
    if (-not $repoRoot) { throw "Not inside a git repository." }
    $repoRoot = ($repoRoot | Select-Object -First 1).Trim()
    [pscustomobject]@{
        RepoRoot = $repoRoot
        Self     = Split-Path $repoRoot -Leaf
        Family   = Split-Path $repoRoot -Parent
    }
}

function Get-FamilyCheckouts {
    <#
    .SYNOPSIS
        Enumerate every sibling checkout (clone or worktree) under the family dir
        and report its git state as objects.
    .PARAMETER IncludeSelf
        Include the current folder (IsSelf = $true). Omitted by default.
    #>
    [CmdletBinding()]
    param([switch]$IncludeSelf)

    # Native git stderr (e.g. "no upstream") must never terminate the enumeration -
    # under PS 5.1 a 2>$null redirect of a native command otherwise becomes a
    # terminating error when the caller's preference is 'Stop'.
    $ErrorActionPreference = 'Continue'

    # The shared long-lived branches are the homestead, not feature brothers -
    # even now that they're materialized as their own worktree folders.
    $homestead = @('app', 'test', 'dev')

    $ctx = Get-FamilyRoot
    foreach ($dir in Get-ChildItem -Path $ctx.Family -Directory) {
        # A checkout has a `.git` entry (dir for a clone, file for a worktree). The
        # `.bare` hub has neither at its root, so it is skipped automatically.
        if (-not (Test-Path (Join-Path $dir.FullName '.git'))) { continue }

        $name   = $dir.Name
        $isSelf = ($name -eq $ctx.Self)
        if ($isSelf -and -not $IncludeSelf) { continue }
        $isHomestead = ($homestead -contains $name)

        $branch = & git -C $dir.FullName rev-parse --abbrev-ref HEAD 2>$null
        if ($branch) { $branch = $branch.Trim() } else { $branch = '(detached)' }
        $branchLeaf = ($branch -split '/')[-1]

        $subject = & git -C $dir.FullName log -1 --format='%s' 2>$null
        $when    = & git -C $dir.FullName log -1 --format='%cr' 2>$null
        if ($subject) { $lastCommit = "$($subject.Trim()) ($($when.Trim()))" } else { $lastCommit = '(no commits)' }

        $dirty = (& git -C $dir.FullName status --porcelain 2>$null | Measure-Object -Line).Lines
        if ($dirty -gt 0) { $tree = "$dirty uncommitted" } else { $tree = 'clean' }

        # Ahead/behind vs upstream - check upstream exists first (for-each-ref never errors).
        $sync = ''
        $upstream = (& git -C $dir.FullName for-each-ref --format='%(upstream:short)' "refs/heads/$branch")
        if ($upstream) {
            $counts = & git -C $dir.FullName rev-list --left-right --count "@{upstream}...HEAD"
            if ($counts) {
                $p = ($counts.Trim() -split '\s+')
                if ($p.Count -eq 2) {
                    if ([int]$p[1] -gt 0) { $sync += "+$($p[1])" }
                    if ([int]$p[0] -gt 0) { $sync += "-$($p[0])" }
                }
            }
            if (-not $sync) { $sync = 'in sync' }
        } else {
            $sync = 'no upstream'
        }

        $note = ''
        $sf = Join-Path $dir.FullName '.brother-status'
        if (Test-Path $sf) { $note = ((Get-Content $sf -Raw -ErrorAction SilentlyContinue) | Out-String).Trim() }

        [pscustomobject]@{
            Name        = $name
            Path        = $dir.FullName
            IsSelf      = $isSelf
            IsHomestead = $isHomestead
            Branch      = $branch
            BranchLeaf  = $branchLeaf
            Tree        = $tree
            DirtyCount  = $dirty
            Sync        = $sync
            LastCommit  = $lastCommit
            Note        = $note
        }
    }
}
