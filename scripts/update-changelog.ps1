param(
    [string]$ChangesFile = "changes.md",
    [string]$ChangelogFile = "changelog.md",
    [string]$BranchName,
    [string]$Timestamp
)

# Function to get first line of changes.md as synopsis
function Get-ChangesSynopsis {
    param([string]$changesPath)

    if (-not (Test-Path $changesPath)) {
        throw "Changes file not found: $changesPath"
    }

    $content = Get-Content $changesPath -First 20

    # Find the first non-empty, non-comment line
    foreach ($line in $content) {
        $trimmedLine = $line.Trim()
        if (-not [string]::IsNullOrWhiteSpace($trimmedLine) -and -not $trimmedLine.StartsWith("#") -and -not $trimmedLine.StartsWith("//")) {
            # Return first meaningful line, limit to 100 characters
            $synopsis = $trimmedLine
            if ($synopsis.Length -gt 100) {
                $synopsis = $synopsis.Substring(0, 97) + "..."
            }
            return $synopsis
        }
    }

    throw "Could not find a meaningful synopsis in changes.md"
}

# Function to update changelog
function Update-Changelog {
    param(
        [string]$changelogPath,
        [string]$synopsis,
        [string]$branchName,
        [string]$timestamp
    )

    # Format the new entry
    $newEntry = @"
## [$timestamp] $branchName
- $synopsis

"@

    # Check if changelog exists
    if (Test-Path $changelogPath) {
        # Read existing content
        $existingContent = Get-Content $changelogPath -Raw

        # Add new entry at the top (after header if it exists)
        $lines = Get-Content $changelogPath
        $headerFound = $false
        $insertIndex = 0

        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i].Trim().Equals("# Changelog")) {
                $headerFound = $true
                $insertIndex = $i + 1
                break
            }
        }

        if ($headerFound) {
            # Insert after the header
            $newContent = $lines[0..$insertIndex] + $newEntry.Split("`n") + $lines[($insertIndex + 1)..$lines.Length]
        } else {
            # No header found, prepend to file
            $newContent = $newEntry.Split("`n") + $lines
        }
    } else {
        # Create new changelog
        $newContent = @"
# Changelog

$newEntry
"@.Split("`n")
    }

    # Write back to file
    $newContent | Out-File -FilePath $changelogPath -Encoding UTF8
    Write-Host "Updated $changelogPath with new entry" -ForegroundColor Green
}

# Main execution
try {
    Write-Host "Processing changelog update..." -ForegroundColor Cyan
    Write-Host "Changes file: $ChangesFile" -ForegroundColor Gray
    Write-Host "Changelog file: $ChangelogFile" -ForegroundColor Gray
    Write-Host "Branch: $BranchName" -ForegroundColor Gray

    # Get synopsis from changes.md
    $synopsis = Get-ChangesSynopsis -changesPath $ChangesFile
    Write-Host "Extracted synopsis: $synopsis" -ForegroundColor Green

    # Update changelog
    Update-Changelog -changelogPath $ChangelogFile -synopsis $synopsis -branchName $BranchName -timestamp $Timestamp

    Write-Host "Changelog update completed successfully!" -ForegroundColor Green

} catch {
    Write-Error "Failed to update changelog: $($_.Exception.Message)"
    exit 1
}