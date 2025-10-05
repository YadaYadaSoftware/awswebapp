# Test script for branch naming validation
# This script tests the same logic used in the GitHub workflow

Write-Host "🧪 Testing Branch Naming Validation" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

# Test cases
$testCases = @(
    @{ BranchName = "feature/user-authentication"; Expected = "valid" },
    @{ BranchName = "fix/memory-leak-issue"; Expected = "valid" },
    @{ BranchName = "feature/add-dark-mode"; Expected = "valid" },
    @{ BranchName = "fix/database-connection-bug"; Expected = "valid" },
    @{ BranchName = "main"; Expected = "valid" },
    @{ BranchName = "dev"; Expected = "valid" },
    @{ BranchName = "app"; Expected = "valid" },
    @{ BranchName = "beta"; Expected = "valid" },
    @{ BranchName = "user-authentication"; Expected = "invalid" },
    @{ BranchName = "bugfix"; Expected = "invalid" },
    @{ BranchName = "new-feature"; Expected = "invalid" },
    @{ BranchName = "random-branch-name"; Expected = "invalid" }
)

$validCount = 0
$invalidCount = 0

foreach ($testCase in $testCases) {
    $branchName = $testCase.BranchName
    $expected = $testCase.Expected

    Write-Host ""
    Write-Host "Testing branch: '$branchName'" -ForegroundColor Yellow

    # Apply the same validation logic as the workflow
    if ($branchName -match "^(main|dev|app|beta)$") {
        $result = "valid"
        $reason = "protected branch"
    } elseif ($branchName -match "^feature/" -or $branchName -match "^fix/") {
        $result = "valid"
        $reason = "follows naming convention"
    } else {
        $result = "invalid"
        $reason = "does not follow naming convention"
    }

    Write-Host "Expected: $expected, Got: $result" -ForegroundColor White

    if ($result -eq $expected) {
        Write-Host "✅ PASS - $reason" -ForegroundColor Green
        if ($result -eq "valid") {
            $validCount++
        } else {
            $invalidCount++
        }
    } else {
        Write-Host "❌ FAIL - Expected $expected but got $result" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "📊 Test Results Summary" -ForegroundColor Cyan
Write-Host "=======================" -ForegroundColor Cyan
Write-Host "Valid branches: $validCount" -ForegroundColor Green
Write-Host "Invalid branches: $invalidCount" -ForegroundColor Red
Write-Host "Total tests: $($validCount + $invalidCount)" -ForegroundColor White

if ($invalidCount -eq 0) {
    Write-Host ""
    Write-Host "🎉 All tests passed! Branch validation logic is working correctly." -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "⚠️  Some tests failed. Please check the validation logic." -ForegroundColor Yellow
    exit 1
}