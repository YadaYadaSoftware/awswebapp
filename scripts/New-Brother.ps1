#Requires -Version 5.1
<#
.SYNOPSIS
    Welcomes a new "brother" - a new sibling work folder of this repo, checked out from `dev`.

.DESCRIPTION
    Each parallel feature is developed in its own sibling folder under the family
    directory (the parent of this repo's root), named after a German brother
    (e.g. wilhelm, friedrich). See BROTHERS.md.

    This script creates the next brother:
      1. Picks his name (the next unused name from the roster in BROTHERS.md, or -Name).
      2. Creates his folder as a `git worktree` under the family directory.
      3. Starts him from `dev` - either a fresh branch off dev (-Branch) or, with no
         branch yet, parked detached at dev's tip. A brother never occupies the `dev`
         branch itself: dev is the shared homestead branch other folders must be able
         to check out, and git forbids the same branch in two worktrees.

    The worktree is based off the `dev` homestead clone if one exists in the family
    directory; otherwise off the current repo (which shares its object store).

.PARAMETER Name
    The new brother's name. Defaults to the next unused name from the BROTHERS.md
    roster (skipping names that already have a folder).

.PARAMETER Branch
    Branch to create off dev and check out for him (the "born to do work" case).
    Follow the repo's branch rules (spec-named for OpenSpec changes, {type}/{name}
    otherwise - see CLAUDE.md). If omitted, he is parked detached at dev's tip, ready
    to branch when assigned work.

.PARAMETER Force
    Allow reusing a folder name whose directory already exists (passed through to
    `git worktree add --force`).

.EXAMPLE
    .\scripts\New-Brother.ps1
    Welcomes the next roster brother, parked detached at dev's tip.

.EXAMPLE
    .\scripts\New-Brother.ps1 -Name friedrich -Branch fix/oauth-callback
    Welcomes friedrich on a new branch fix/oauth-callback off dev.
#>
[CmdletBinding()]
param(
    [string]$Name,
    [string]$Branch,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# --- Locate this folder's repo root and the family directory (its parent) ---
$repoRoot = & git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) {
    Write-Error "Not inside a git repository."
    return
}
$repoRoot = ($repoRoot | Select-Object -First 1).Trim()
$family   = Split-Path $repoRoot -Parent

# --- Parse the roster: the BROTHERS.md line with the most `backtick-quoted` names ---
$brothersMd = Join-Path $repoRoot 'BROTHERS.md'
$roster = @()
if (Test-Path $brothersMd) {
    $best = 0
    foreach ($line in Get-Content $brothersMd) {
        $m = [regex]::Matches($line, '`([a-z]+)`')
        if ($m.Count -gt $best) {
            $best   = $m.Count
            $roster = @($m | ForEach-Object { $_.Groups[1].Value })
        }
    }
}

# --- Existing brother folders (any sibling with a .git entry) ---
$existing = @(
    Get-ChildItem -Path $family -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName '.git') } |
        ForEach-Object { $_.Name }
)

# --- Pick the name ---
if ($Name) {
    $Name = $Name.Trim().ToLowerInvariant()
    if ($roster.Count -and ($roster -notcontains $Name)) {
        Write-Warning "'$Name' is not on the BROTHERS.md roster - adding an off-roster brother."
    }
} else {
    if (-not $roster.Count) {
        Write-Error "Could not read the roster from BROTHERS.md; pass -Name explicitly."
        return
    }
    $Name = $roster | Where-Object { $existing -notcontains $_ } | Select-Object -First 1
    if (-not $Name) {
        Write-Error "Every roster name already has a folder. Extend the roster in BROTHERS.md or pass -Name."
        return
    }
}

if ($existing -contains $Name -and -not $Force) {
    Write-Error "Brother '$Name' already exists under $family. Use -Force to reuse the folder."
    return
}

$target = Join-Path $family $Name

# --- Choose the base clone for the worktree: dev homestead if present, else here ---
$devHome = Join-Path $family 'dev'
if (Test-Path (Join-Path $devHome '.git')) { $base = $devHome } else { $base = $repoRoot }

# --- Best-effort refresh of dev from origin (offline is fine) ---
try { & git -C $base fetch --quiet origin dev 2>$null } catch { }

# --- Resolve a valid dev start-point: local `dev`, else `origin/dev` ---
& git -C $base rev-parse --verify --quiet refs/heads/dev > $null
$hasLocalDev = ($LASTEXITCODE -eq 0)
if (-not $hasLocalDev) {
    & git -C $base rev-parse --verify --quiet refs/remotes/origin/dev > $null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "No 'dev' branch found locally or on origin in $base. Fetch dev first."
        return
    }
}

# --- Create the worktree ---
# A brother must NEVER occupy the `dev` branch ref: dev is the shared homestead branch
# every other folder needs to be able to check out, and git allows a branch in only one
# worktree. So with no -Branch we park him DETACHED at dev's tip (he gets dev's files but
# doesn't own the branch); with -Branch he gets his own branch off dev.
$forceArg = @(); if ($Force) { $forceArg = @('--force') }
$start = if ($hasLocalDev) { 'dev' } else { 'origin/dev' }
if ($Branch) {
    & git -C $base worktree add @forceArg $target -b $Branch $start
    $mode = "on new branch '$Branch' (off dev)"
} else {
    & git -C $base worktree add @forceArg --detach $target $start
    $mode = "parked at dev's tip (detached - run ``git checkout -b <branch>`` when assigned a task)"
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "git worktree add failed (exit $LASTEXITCODE)."
    return
}

$lastCommit = (& git -C $target log -1 --format='%s (%cr)' 2>$null)
Write-Host ""
Write-Host "Welcome, $Name. New brother created:" -ForegroundColor Green
Write-Host "  Folder : $target"
Write-Host "  Base   : $base"
Write-Host "  State  : $mode"
if ($lastCommit) { Write-Host "  Head   : $($lastCommit.Trim())" }
Write-Host ""
Write-Host "He'll report as '$Name' on /whoami and show up in /brothers." -ForegroundColor DarkGray
