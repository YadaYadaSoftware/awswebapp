param(
    [switch]$SetupGitHooks,
    [switch]$ShowRecommendations
)

Write-Host "Branch Management System - Setup and Protection" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# Function to setup git hooks
function Setup-GitHooks {
    Write-Host "Setting up Git hooks for branch protection..." -ForegroundColor Yellow

    $hooksDir = ".git/hooks"
    $prePushHook = @"
#!/bin/sh
# Pre-push hook to validate branch naming and changes.md

branch_name=`git rev-parse --abbrev-ref HEAD`

# Check if branch follows naming convention (type/name)
if ! echo "$branch_name" | grep -qE "^(build|deploy|system|feature|fix)/"; then
    echo "❌ Branch name '$branch_name' doesn't follow naming convention"
    echo "   Use one of: build/, deploy/, system/, feature/, fix/"
    echo "   Example: feature/user-authentication"
    exit 1
fi

# Check if changes.md exists for categorized branches
if [ -n "$branch_name" ] && echo "$branch_name" | grep -qE "^(build|deploy|system|feature|fix)/"; then
    if [ ! -f "changes.md" ]; then
        echo "❌ changes.md file not found"
        echo "   Create changes.md with description of your changes"
        echo "   Use the create-branch.ps1 script for assistance"
        exit 1
    fi

    # Check if changes.md has meaningful content
    if [ ! -s "changes.md" ]; then
        echo "❌ changes.md is empty"
        echo "   Add description of your changes to changes.md"
        exit 1
    fi
fi

echo "✅ Branch validation passed"
exit 0
"@

    if (-not (Test-Path $hooksDir)) {
        Write-Host "Git hooks directory not found" -ForegroundColor Red
        return $false
    }

    $prePushHook | Out-File -FilePath "$hooksDir/pre-push" -Encoding ASCII
    chmod +x "$hooksDir/pre-push" 2>$null

    Write-Host "✅ Git pre-push hook installed" -ForegroundColor Green
    Write-Host "   This will validate branch naming and changes.md before pushes" -ForegroundColor Gray
}

# Function to show recommendations
function Show-Recommendations {
    Write-Host ""
    Write-Host "GitHub Repository Settings Recommendations:" -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host ""

    Write-Host "1. Branch Protection Rules (for 'dev' branch):" -ForegroundColor Cyan
    Write-Host "   - Require status checks to pass" -ForegroundColor White
    Write-Host "   - Require up-to-date branches before merging" -ForegroundColor White
    Write-Host "   - Include administrators" -ForegroundColor White
    Write-Host ""

    Write-Host "2. Repository Settings:" -ForegroundColor Cyan
    Write-Host "   - Enable 'Require branches to be up to date before merging'" -ForegroundColor White
    Write-Host "   - Enable 'Require status checks to pass'" -ForegroundColor White
    Write-Host "   - Add 'Build and Test Applications' check" -ForegroundColor White
    Write-Host "   - Add 'Deploy to AWS' check" -ForegroundColor White
    Write-Host ""

    Write-Host "3. Webhook Configuration:" -ForegroundColor Cyan
    Write-Host "   - Ensure push events trigger the zbuild.yml workflow" -ForegroundColor White
    Write-Host "   - Verify workflow triggers on dev branch pushes" -ForegroundColor White
    Write-Host ""

    Write-Host "4. Secrets Configuration:" -ForegroundColor Cyan
    Write-Host "   - AWS_ACCESS_KEY_ID" -ForegroundColor White
    Write-Host "   - AWS_SECRET_ACCESS_KEY" -ForegroundColor White
    Write-Host "   - DATABASE_PASSWORD" -ForegroundColor White
    Write-Host "   - GOOGLE_CLIENT_ID" -ForegroundColor White
    Write-Host "   - GOOGLE_CLIENT_SECRET" -ForegroundColor White
    Write-Host ""

    Write-Host "5. Local Development Setup:" -ForegroundColor Cyan
    Write-Host "   - Use the create-branch.ps1 script for new branches" -ForegroundColor White
    Write-Host "   - Always update changes.md with detailed descriptions" -ForegroundColor White
    Write-Host "   - Test changes locally before merging to dev" -ForegroundColor White
}

# Main execution
if ($SetupGitHooks) {
    Setup-GitHooks
}

if ($ShowRecommendations -or (-not $SetupGitHooks)) {
    Show-Recommendations
}

Write-Host ""
Write-Host "Setup complete!" -ForegroundColor Green
Write-Host "Refer to BRANCH_MANAGEMENT_README.md for detailed usage instructions." -ForegroundColor Cyan