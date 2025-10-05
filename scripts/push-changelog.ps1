param(
    [string]$ChangelogFile = "changelog.md",
    [string]$CommitMessage = "docs: update changelog with latest changes"
)

# Function to check if there are changes to commit
function Test-GitChanges {
    param([string]$file)

    $status = git status --porcelain $file
    return [string]::IsNullOrWhiteSpace($status) -eq $false
}

# Function to push changelog without triggering builds
function Push-ChangelogQuietly {
    param(
        [string]$file,
        [string]$message
    )

    # Check if file exists and has changes
    if (-not (Test-Path $file)) {
        Write-Host "Changelog file not found: $file" -ForegroundColor Red
        return $false
    }

    if (-not (Test-GitChanges -file $file)) {
        Write-Host "No changes to changelog detected" -ForegroundColor Yellow
        return $true
    }

    try {
        # Add the changelog file
        Write-Host "Adding changelog to git..." -ForegroundColor Cyan
        git add $file

        # Commit with specific message
        Write-Host "Committing changelog..." -ForegroundColor Cyan
        git commit -m $message

        # Push to origin - this should not trigger builds if properly configured
        Write-Host "Pushing changelog to origin..." -ForegroundColor Cyan
        git push origin HEAD

        Write-Host "Changelog pushed successfully!" -ForegroundColor Green
        return $true

    } catch {
        Write-Error "Failed to push changelog: $($_.Exception.Message)"
        return $false
    }
}

# Main execution
Write-Host "Pushing changelog updates..." -ForegroundColor Cyan

$success = Push-ChangelogQuietly -file $ChangelogFile -message $CommitMessage

if ($success) {
    Write-Host "Changelog update completed successfully!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Changelog update failed!" -ForegroundColor Red
    exit 1
}