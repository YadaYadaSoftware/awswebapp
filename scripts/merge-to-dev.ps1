param(
    [switch]$NonInteractive,
    [string]$BranchType,
    [string]$BranchName
)

# Function to prompt for input with validation
function Get-ValidatedInput {
    param(
        [string]$Prompt,
        [string[]]$ValidOptions = $null,
        [switch]$Required
    )

    do {
        $input = Read-Host -Prompt $Prompt
        if ($Required -and [string]::IsNullOrWhiteSpace($input)) {
            Write-Host "This field is required. Please try again." -ForegroundColor Red
            continue
        }
        if ($ValidOptions -and $ValidOptions -notcontains $input) {
            Write-Host "Invalid option. Valid options are: $($ValidOptions -join ', ')" -ForegroundColor Red
            continue
        }
        break
    } while ($true)

    return $input
}

# Function to get branch type selection
function Get-BranchTypeSelection {
    $branchTypes = @("system", "fix", "feature", "build", "deploy")
    Write-Host "Available branch types:" -ForegroundColor Cyan
    for ($i = 0; $i -lt $branchTypes.Length; $i++) {
        Write-Host "  $($i + 1). $($branchTypes[$i])" -ForegroundColor White
    }

    do {
        $selection = Read-Host -Prompt "Select branch type (1-$($branchTypes.Length))"
        $selectionNum = [int]::TryParse($selection, [ref]$null) ? [int]$selection : 0
        if ($selectionNum -ge 1 -and $selectionNum -le $branchTypes.Length) {
            return $branchTypes[$selectionNum - 1]
        }
        Write-Host "Invalid selection. Please enter a number between 1 and $($branchTypes.Length)" -ForegroundColor Red
    } while ($true)
}

# Function to get branches by type sorted by commit date
function Get-BranchesByType {
    param([string]$BranchType)

    Write-Host "Fetching $BranchType branches..." -ForegroundColor Green

    # Get all branches of the specified type
    $branches = git branch -r | Where-Object { $_ -match "origin/$BranchType/" } | ForEach-Object {
        $branchName = $_.Trim() -replace "origin/", ""
        return $branchName
    }

    if ($branches.Count -eq 0) {
        Write-Host "No $BranchType branches found." -ForegroundColor Yellow
        return $null
    }

    # Get commit dates for each branch and sort by date (newest first)
    $branchInfo = foreach ($branch in $branches) {
        $commitDate = git log -1 --format=%ct "$branch" 2>$null
        if ($commitDate) {
            [PSCustomObject]@{
                Branch = $branch
                CommitDate = [DateTime]::FromFileTimeUtc($commitDate * 10000000)
                DisplayName = $branch -replace "$BranchType/", ""
            }
        }
    }

    $sortedBranches = $branchInfo | Sort-Object -Property CommitDate -Descending

    Write-Host "Available $BranchType branches (newest first):" -ForegroundColor Cyan
    for ($i = 0; $i -lt $sortedBranches.Count; $i++) {
        $branch = $sortedBranches[$i]
        $dateStr = $branch.CommitDate.ToString("yyyy-MM-dd HH:mm:ss")
        Write-Host "  $($i + 1). $($branch.DisplayName) (last commit: $dateStr)" -ForegroundColor White
    }

    return $sortedBranches
}

# Function to merge branch and process changelog
function Merge-BranchAndUpdateChangelog {
    param(
        [string]$FullBranchName,
        [string]$BranchType,
        [string]$BranchDisplayName
    )

    Write-Host "Merging branch: $FullBranchName" -ForegroundColor Green

    # Checkout dev branch
    git checkout dev

    # Merge the selected branch
    git merge $FullBranchName --no-ff -m "Merge branch '$FullBranchName' into dev"

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Branch merged successfully" -ForegroundColor Green

        # Process changes file
        $changesFileName = $FullBranchName -replace "/", "-"
        $changesFile = "changes\$changesFileName.md"
        if (Test-Path $changesFile) {
            Write-Host "📝 Processing changes file: $changesFile" -ForegroundColor Green

            # Read the changes file content
            $changesContent = Get-Content $changesFile -Raw

            # Update changelog.md
            $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC"

            if (Test-Path "changelog.md") {
                # Prepend to existing changelog
                $existingContent = Get-Content "changelog.md" -Raw
                $newContent = @"
## [$timestamp] $BranchDisplayName ($BranchType)

$changesContent

$existingContent
"@
            } else {
                # Create new changelog
                $newContent = @"
# Changelog

## [$timestamp] $BranchDisplayName ($BranchType)

$changesContent
"@
            }

            $newContent | Out-File -FilePath "changelog.md" -Encoding UTF8

            # Delete the changes file
            Remove-Item $changesFile -Force
            Write-Host "✅ Changes content added to changelog.md" -ForegroundColor Green
            Write-Host "🗑️  Changes file deleted: $changesFile" -ForegroundColor Green

            # Commit the changelog update
            git add changelog.md
            git commit -m "docs: update changelog with changes from $BranchDisplayName"

            Write-Host "✅ Changelog update committed" -ForegroundColor Green
        } else {
            Write-Host "⚠️  No changes file found for $FullBranchName" -ForegroundColor Yellow
        }
    } else {
        Write-Host "❌ Failed to merge branch $FullBranchName" -ForegroundColor Red
        exit 1
    }
}

# Main execution
Write-Host "🔄 Merge to Dev Script" -ForegroundColor Cyan
Write-Host "====================" -ForegroundColor Cyan

# Check if we're on dev branch
$currentBranch = git branch --show-current
if ($currentBranch -ne "dev") {
    Write-Host "⚠️  Not currently on dev branch. Switching to dev..." -ForegroundColor Yellow
    git checkout dev
}

# Get branch type selection
if (-not $NonInteractive) {
    if ([string]::IsNullOrWhiteSpace($BranchType)) {
        $selectedBranchType = Get-BranchTypeSelection
    } else {
        $selectedBranchType = $BranchType
    }

    # Get branches and present selection
    $branches = Get-BranchesByType -BranchType $selectedBranchType
    if (-not $branches) {
        Write-Host "No branches available for type: $selectedBranchType" -ForegroundColor Red
        exit 1
    }

    # Get branch selection
    do {
        $selection = Read-Host -Prompt "Select branch to merge (1-$($branches.Count))"
        $selectionNum = [int]::TryParse($selection, [ref]$null) ? [int]$selection : 0
        if ($selectionNum -ge 1 -and $selectionNum -le $branches.Count) {
            $selectedBranch = $branches[$selectionNum - 1]
            break
        }
        Write-Host "Invalid selection. Please enter a number between 1 and $($branches.Count)" -ForegroundColor Red
    } while ($true)

    $fullBranchName = $selectedBranch.Branch
    $branchDisplayName = $selectedBranch.DisplayName
} else {
    # Non-interactive mode
    if ([string]::IsNullOrWhiteSpace($BranchType) -or [string]::IsNullOrWhiteSpace($BranchName)) {
        Write-Host "❌ Non-interactive mode requires BranchType and BranchName parameters" -ForegroundColor Red
        exit 1
    }
    $selectedBranchType = $BranchType
    $fullBranchName = "$BranchType/$BranchName"
    $branchDisplayName = $BranchName
}

Write-Host "Selected: $fullBranchName" -ForegroundColor Green

# Confirm merge
if (-not $NonInteractive) {
    $confirm = Read-Host -Prompt "Proceed with merge? (y/N)"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Host "Merge cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# Perform the merge and update changelog
Merge-BranchAndUpdateChangelog -FullBranchName $fullBranchName -BranchType $selectedBranchType -BranchDisplayName $branchDisplayName

Write-Host ""
Write-Host "🎉 Merge completed successfully!" -ForegroundColor Green
Write-Host "📋 Summary:" -ForegroundColor Cyan
Write-Host "  • Merged: $fullBranchName" -ForegroundColor White
Write-Host "  • Type: $selectedBranchType" -ForegroundColor White
Write-Host "  • Changelog updated with changes" -ForegroundColor White
Write-Host "  • Changes file cleaned up" -ForegroundColor White