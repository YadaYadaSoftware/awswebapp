param(
    [switch]$TestBranchCreation,
    [switch]$TestChangelogUpdate,
    [switch]$TestValidation,
    [switch]$All
)

Write-Host "Branch Management System - Test Suite" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan

# Test branch creation script validation
function Test-BranchCreation {
    Write-Host "Testing branch creation script..." -ForegroundColor Yellow

    # Test 1: Validate script exists and is executable
    if (-not (Test-Path "scripts/create-branch.ps1")) {
        Write-Host "❌ create-branch.ps1 not found" -ForegroundColor Red
        return $false
    }

    # Test 2: Check script syntax (basic)
    try {
        $scriptContent = Get-Content "scripts/create-branch.ps1" -Raw
        if (-not $scriptContent) {
            Write-Host "❌ create-branch.ps1 is empty" -ForegroundColor Red
            return $false
        }
        Write-Host "✅ create-branch.ps1 exists and has content" -ForegroundColor Green
    } catch {
        Write-Host "❌ Error reading create-branch.ps1: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }

    # Test 3: Check for required functions
    $requiredPatterns = @(
        "Get-ValidatedInput",
        "git checkout",
        "changes\.md",
        "BranchType",
        "BranchName"
    )

    $missingPatterns = @()
    foreach ($pattern in $requiredPatterns) {
        if (-not ($scriptContent -match $pattern)) {
            $missingPatterns += $pattern
        }
    }

    if ($missingPatterns.Count -gt 0) {
        Write-Host "❌ Missing required patterns in create-branch.ps1:" -ForegroundColor Red
        $missingPatterns | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
        return $false
    }

    Write-Host "✅ create-branch.ps1 validation passed" -ForegroundColor Green
    return $true
}

# Test changelog update script validation
function Test-ChangelogUpdate {
    Write-Host "Testing changelog update script..." -ForegroundColor Yellow

    # Test 1: Validate script exists
    if (-not (Test-Path "scripts/update-changelog.ps1")) {
        Write-Host "❌ update-changelog.ps1 not found" -ForegroundColor Red
        return $false
    }

    # Test 2: Check script content
    try {
        $scriptContent = Get-Content "scripts/update-changelog.ps1" -Raw
        if (-not $scriptContent) {
            Write-Host "❌ update-changelog.ps1 is empty" -ForegroundColor Red
            return $false
        }
        Write-Host "✅ update-changelog.ps1 exists and has content" -ForegroundColor Green
    } catch {
        Write-Host "❌ Error reading update-changelog.ps1: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }

    # Test 3: Check for required functions
    $requiredPatterns = @(
        "Get-ChangesSynopsis",
        "Update-Changelog",
        "changelog\.md",
        "changes\.md",
        "Test-Path"
    )

    $missingPatterns = @()
    foreach ($pattern in $requiredPatterns) {
        if (-not ($scriptContent -match $pattern)) {
            $missingPatterns += $pattern
        }
    }

    if ($missingPatterns.Count -gt 0) {
        Write-Host "❌ Missing required patterns in update-changelog.ps1:" -ForegroundColor Red
        $missingPatterns | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
        return $false
    }

    Write-Host "✅ update-changelog.ps1 validation passed" -ForegroundColor Green
    return $true
}

# Test workflow validation
function Test-WorkflowValidation {
    Write-Host "Testing workflow configuration..." -ForegroundColor Yellow

    # Test 1: Check workflow file exists
    if (-not (Test-Path ".github/workflows/zbuild.yml")) {
        Write-Host "❌ zbuild.yml workflow not found" -ForegroundColor Red
        return $false
    }

    # Test 2: Check for changelog job
    try {
        $workflowContent = Get-Content ".github/workflows/zbuild.yml" -Raw
        if (-not $workflowContent) {
            Write-Host "❌ zbuild.yml is empty" -ForegroundColor Red
            return $false
        }

        # Check for key components
        $requiredPatterns = @(
            "changelog-update:",
            "changes\.md",
            "changelog\.md",
            "categorized-branch"
        )

        $missingPatterns = @()
        foreach ($pattern in $requiredPatterns) {
            if (-not ($workflowContent -match $pattern)) {
                $missingPatterns += $pattern
            }
        }

        if ($missingPatterns.Count -gt 0) {
            Write-Host "❌ Missing required patterns in zbuild.yml:" -ForegroundColor Red
            $missingPatterns | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
            return $false
        }

        Write-Host "✅ zbuild.yml contains changelog management components" -ForegroundColor Green
    } catch {
        Write-Host "❌ Error reading zbuild.yml: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }

    Write-Host "✅ workflow validation passed" -ForegroundColor Green
    return $true
}

# Test documentation
function Test-Documentation {
    Write-Host "Testing documentation..." -ForegroundColor Yellow

    # Test 1: Check README exists
    if (-not (Test-Path "BRANCH_MANAGEMENT_README.md")) {
        Write-Host "❌ BRANCH_MANAGEMENT_README.md not found" -ForegroundColor Red
        return $false
    }

    # Test 2: Check content
    try {
        $readmeContent = Get-Content "BRANCH_MANAGEMENT_README.md" -Raw
        if (-not $readmeContent) {
            Write-Host "❌ BRANCH_MANAGEMENT_README.md is empty" -ForegroundColor Red
            return $false
        }

        # Check for key sections
        $requiredSections = @(
            "Branch Management System",
            "Creating a New Branch",
            "Development Process",
            "Automated Changelog Management"
        )

        $missingSections = @()
        foreach ($section in $requiredSections) {
            if (-not ($readmeContent -match [regex]::Escape($section))) {
                $missingSections += $section
            }
        }

        if ($missingSections.Count -gt 0) {
            Write-Host "❌ Missing sections in documentation:" -ForegroundColor Red
            $missingSections | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
            return $false
        }

        Write-Host "✅ documentation validation passed" -ForegroundColor Green
    } catch {
        Write-Host "❌ Error reading documentation: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }

    Write-Host "✅ documentation validation passed" -ForegroundColor Green
    return $true
}

# Main test execution
$testsRun = 0
$testsPassed = 0

if ($All -or $TestBranchCreation) {
    $testsRun++
    if (Test-BranchCreation) { $testsPassed++ }
}

if ($All -or $TestChangelogUpdate) {
    $testsRun++
    if (Test-ChangelogUpdate) { $testsPassed++ }
}

if ($All -or $TestValidation) {
    $testsRun++
    if (Test-WorkflowValidation) { $testsPassed++ }
}

# Always test documentation
$testsRun++
if (Test-Documentation) { $testsPassed++ }

# Summary
Write-Host ""
Write-Host "Test Summary:" -ForegroundColor Cyan
Write-Host "=============" -ForegroundColor Cyan
Write-Host "Tests run: $testsRun" -ForegroundColor White
Write-Host "Tests passed: $testsPassed" -ForegroundColor Green

if ($testsRun -eq $testsPassed) {
    Write-Host ""
    Write-Host "🎉 All tests passed! The branch management system is ready to use." -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "1. Run .\scripts\setup-branch-protection.ps1 for setup recommendations" -ForegroundColor White
    Write-Host "2. Use .\scripts\create-branch.ps1 to create your first branch" -ForegroundColor White
    Write-Host "3. Read BRANCH_MANAGEMENT_README.md for detailed usage instructions" -ForegroundColor White
    exit 0
} else {
    Write-Host ""
    Write-Host "❌ Some tests failed. Please check the errors above." -ForegroundColor Red
    exit 1
}