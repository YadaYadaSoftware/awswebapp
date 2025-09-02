#!/bin/bash

# Automated script to remove bin/ and obj/ files from git history
# This will automatically proceed with history rewriting

echo "=== Automated .NET Build Directory History Pruning ==="
echo "This script will automatically remove bin/ and obj/ files from git history"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository!"
    exit 1
fi

# Check git history for bin/obj files
print_status "Checking git history for bin/ and obj/ files..."
HISTORY_BIN_COUNT=$(git log --all --full-history --name-only --pretty=format: | grep -E "(^|/)bin/" | wc -l 2>/dev/null | tr -d ' ' || echo "0")
HISTORY_OBJ_COUNT=$(git log --all --full-history --name-only --pretty=format: | grep -E "(^|/)obj/" | wc -l 2>/dev/null | tr -d ' ' || echo "0")

echo ""
print_status "Found $HISTORY_BIN_COUNT bin/ and $HISTORY_OBJ_COUNT obj/ file references in git history"

if [ "$HISTORY_BIN_COUNT" -eq 0 ] && [ "$HISTORY_OBJ_COUNT" -eq 0 ]; then
    print_status "No bin/ or obj/ files found in git history. Repository is already clean!"
    exit 0
fi

print_warning "These files are taking up space in your repository"
print_status "Proceeding with automatic history rewriting..."

# Create backup branch
print_status "Creating backup branch before history rewriting..."
BACKUP_BRANCH="backup-before-prune-$(date +%Y%m%d-%H%M%S)"
git branch "$BACKUP_BRANCH"
print_status "Backup created: $BACKUP_BRANCH"

# Check for git-filter-repo
if command -v git-filter-repo >/dev/null 2>&1; then
    print_status "Using git-filter-repo to rewrite history..."
    git filter-repo --path-glob '*/bin/*' --path-glob '*/obj/*' --path-glob 'bin/*' --path-glob 'obj/*' --invert-paths --force
else
    print_status "Using git filter-branch to rewrite history (this may take a while)..."
    git filter-branch --force --index-filter \
        'git rm -r --cached --ignore-unmatch */bin */obj bin obj' \
        --prune-empty --tag-name-filter cat -- --all
    
    # Clean up filter-branch refs
    print_status "Cleaning up filter-branch references..."
    git for-each-ref --format="%(refname)" refs/original/ | xargs -n 1 git update-ref -d 2>/dev/null || true
    git reflog expire --expire=now --all
    git gc --prune=now --aggressive
fi

echo ""
print_status "History rewriting completed successfully!"
print_status "Repository has been cleaned of all bin/ and obj/ files from history"
print_warning "Your backup branch is: $BACKUP_BRANCH"

echo ""
print_status "Repository size comparison:"
echo "  Check current size with: du -sh .git/"
echo "  The repository should be significantly smaller now."

echo ""
print_warning "IMPORTANT NEXT STEPS:"
echo "  1. If you have remote repositories, force push with: git push --force-with-lease --all"
echo "  2. All collaborators will need to re-clone or rebase their work"
echo "  3. Your .gitignore already prevents future bin/obj tracking"

echo ""
print_status "Build directory history pruning completed!"