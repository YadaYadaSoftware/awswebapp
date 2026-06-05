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

. "$PSScriptRoot\_BrothersCommon.ps1"

# Enumeration + git-state lives in Get-FamilyCheckouts (shared with /next); here we
# just project it into the display table.
$rows = @(
    Get-FamilyCheckouts -IncludeSelf:$IncludeSelf | ForEach-Object {
        [pscustomobject]@{
            Brother    = $_.Name
            Who        = if ($_.IsSelf) { '(self)' } else { '' }
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
    return
}

$rows | Sort-Object Brother | Format-Table -AutoSize -Wrap
