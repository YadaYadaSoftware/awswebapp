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

            # Commit the changelog update and file deletion
            git add .
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

# Get current branch
$currentBranch = git branch --show-current
Write-Host "Current branch: $currentBranch" -ForegroundColor White

# Validate current branch format and determine branch type
if ($currentBranch -match "^(system|fix|feature|build|deploy)\/") {
    $branchType = $currentBranch.Split("/")[0]
    $branchName = $currentBranch.Split("/")[1]
    $fullBranchName = $currentBranch
    $branchDisplayName = $branchName

    Write-Host "Branch type: $branchType" -ForegroundColor White
    Write-Host "Branch name: $branchDisplayName" -ForegroundColor White
} else {
    Write-Host "❌ Current branch '$currentBranch' is not a valid branch type (system/, fix/, feature/, build/, deploy/)" -ForegroundColor Red
    Write-Host "Please switch to a valid branch or create one using create-branch.ps1" -ForegroundColor Yellow
    exit 1
}

# Check if changes file exists for current branch
$changesFileName = $fullBranchName -replace "/", "-"
$changesFile = "changes\$changesFileName.md"
if (Test-Path $changesFile) {
    Write-Host "✅ Found changes file: $changesFileName.md" -ForegroundColor Green
} else {
    Write-Host "❌ No changes file found for current branch: $changesFileName.md" -ForegroundColor Red
    Write-Host "Please ensure your branch has a changes file in the changes/ folder" -ForegroundColor Yellow
    exit 1
}

Write-Host "Ready to merge: $fullBranchName" -ForegroundColor Green

# Confirm merge
if (-not $NonInteractive) {
    $confirm = Read-Host -Prompt "Proceed with merge? (y/N)"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Host "Merge cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# Perform the merge and update changelog
Merge-BranchAndUpdateChangelog -FullBranchName $fullBranchName -BranchType $branchType -BranchDisplayName $branchDisplayName

Write-Host ""
Write-Host "🎉 Merge completed successfully!" -ForegroundColor Green
Write-Host "📋 Summary:" -ForegroundColor Cyan
Write-Host "  • Merged: $fullBranchName" -ForegroundColor White
Write-Host "  • Type: $selectedBranchType" -ForegroundColor White
Write-Host "  • Changelog updated with changes" -ForegroundColor White
Write-Host "  • Changes file cleaned up" -ForegroundColor White