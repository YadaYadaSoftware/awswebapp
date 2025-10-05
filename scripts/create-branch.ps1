param(
    [string]$BranchType,
    [string]$BranchName,
    [string]$ChangesDescription
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

# Validate or prompt for branch type
$validTypes = @("build", "deploy", "system", "feature", "fix")
if ([string]::IsNullOrWhiteSpace($BranchType)) {
    $BranchType = Get-ValidatedInput -Prompt "Enter branch type (build/deploy/system/feature/fix)" -ValidOptions $validTypes -Required
}

# Validate or prompt for branch name
if ([string]::IsNullOrWhiteSpace($BranchName)) {
    $BranchName = Get-ValidatedInput -Prompt "Enter branch name (without type prefix)" -Required
}

# Validate or prompt for changes description
if ([string]::IsNullOrWhiteSpace($ChangesDescription)) {
    $ChangesDescription = Get-ValidatedInput -Prompt "Enter description of changes" -Required
}

# Construct full branch name
$fullBranchName = "$BranchType/$BranchName"

Write-Host "Creating branch: $fullBranchName" -ForegroundColor Green

# Check if branch already exists
$branchExists = git branch --list $fullBranchName
if ($branchExists) {
    Write-Host "Branch '$fullBranchName' already exists. Switching to it..." -ForegroundColor Yellow
    git checkout $fullBranchName
} else {
    Write-Host "Creating new branch: $fullBranchName" -ForegroundColor Green
    git checkout -b $fullBranchName
}

# Create branch-specific changes file in changes folder
$changesContent = @"
# Changes for $fullBranchName

**Purpose:** $ChangesDescription

**Date Created:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

**Branch Type:** $BranchType

## Description
Provide detailed information about the changes being made in this branch.

## Files Modified
- List files that will be modified

## Testing
- Describe testing approach

## Deployment Notes
- Any special deployment considerations
"@

# Create changes folder if it doesn't exist
if (!(Test-Path "changes")) {
    New-Item -ItemType Directory -Path "changes" | Out-Null
    Write-Host "Created changes/ directory" -ForegroundColor Green
}

$changesFileName = $fullBranchName -replace "/", "-"
$changesPath = "changes\$changesFileName.md"
if (Test-Path $changesPath) {
    Write-Host "changes/$fullBranchName.md already exists. Updating content..." -ForegroundColor Yellow
} else {
    Write-Host "Creating changes/$fullBranchName.md..." -ForegroundColor Green
}

$changesContent | Out-File -FilePath $changesPath -Encoding UTF8

Write-Host "Branch '$fullBranchName' created successfully!" -ForegroundColor Green
Write-Host "changes.md has been created with the provided description." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Make your code changes" -ForegroundColor White
Write-Host "2. Update changes.md with detailed information about your changes" -ForegroundColor White
Write-Host "3. Commit your changes: git add . && git commit -m 'Your commit message'" -ForegroundColor White
Write-Host "4. Push branch to origin: git push origin $fullBranchName" -ForegroundColor White
Write-Host "5. Merge into dev branch when ready" -ForegroundColor White