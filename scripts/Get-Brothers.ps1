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

.PARAMETER IncludeHomestead
    Also list the homestead worktrees (app/beta/alpha/dev). By default they're
    summarized on a single line, since they're shared branches, not feature brothers.

.EXAMPLE
    .\scripts\Get-Brothers.ps1
    Lists every other brother and what they're working on.

.EXAMPLE
    .\scripts\Get-Brothers.ps1 -IncludeSelf
    Lists all brothers including this one.
#>
[CmdletBinding()]
param(
    [switch]$IncludeSelf,
    [switch]$IncludeHomestead
)

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\_BrothersCommon.ps1"

$all = @(Get-FamilyCheckouts -IncludeSelf:$IncludeSelf)

# Feature brothers vs the homestead (app/beta/alpha/dev). Enumeration + git-state
# lives in Get-FamilyCheckouts (shared with /next); here we just project + split.
$brothers  = @($all | Where-Object { $IncludeHomestead -or -not $_.IsHomestead })
$homestead = @($all | Where-Object { $_.IsHomestead })

$rows = @(
    $brothers | ForEach-Object {
        [pscustomobject]@{
            Brother    = $_.Name
            Who        = if ($_.IsSelf) { '(self)' } elseif ($_.IsHomestead) { '(homestead)' } else { '' }
            Branch     = $_.Branch
            Tree       = $_.Tree
            Sync       = $_.Sync
            LastCommit = $_.LastCommit
            Note       = $_.Note
        }
    }
)

if (-not $rows) {
    Write-Host "No brothers found."
} else {
    $rows | Sort-Object Brother | Format-Table -AutoSize -Wrap
}

# One-line homestead summary unless it was folded into the table above.
if (-not $IncludeHomestead -and $homestead.Count) {
    $summary = ($homestead | Sort-Object Name | ForEach-Object {
        $t = if ($_.DirtyCount -gt 0) { "*$($_.DirtyCount)" } else { '' }
        "$($_.Name)$t"
    }) -join ', '
    Write-Host "Homestead: $summary  (shared branches - not feature brothers; * = uncommitted)"
}
