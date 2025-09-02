#!/bin/bash

# Script to completely prune all bin/ and obj/ directories from the git repository
# This script will:
# 1. Find and remove all bin/ and obj/ directories from the working tree
# 2. Remove them from git tracking if they were previously committed
# 3. REWRITE GIT HISTORY to remove them from all commits (optional)
# 4. Show a summary of what was removed

echo "=== .NET Build Directory Complete Pruning Script ==="
echo "This script will remove all bin/ and obj/ directories from the repository"
echo "and optionally rewrite git history to remove them completely."
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_question() {
    echo -e "${BLUE}[QUESTION]${NC} $1"
}

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository!"
    exit 1
fi

# Check if git-filter-repo is available (preferred method)
FILTER_REPO_AVAILABLE=false
if command -v git-filter-repo >/dev/null 2>&1; then
    FILTER_REPO_AVAILABLE=true
    print_status "git-filter-repo is available (recommended for history rewriting)"
else
    print_warning "git-filter-repo not found. Will use git filter-branch if history rewriting is requested."
    print_warning "Consider installing git-filter-repo for better performance: pip install git-filter-repo"
fi

print_status "Scanning for bin/ and obj/ directories..."

# Find all bin and obj directories (case insensitive)
BIN_DIRS=$(find . -type d -iname "bin" 2>/dev/null | grep -v ".git")
OBJ_DIRS=$(find . -type d -iname "obj" 2>/dev/null | grep -v ".git")

# Count directories found
if [ -z "$BIN_DIRS" ]; then
    BIN_COUNT=0
else
    BIN_COUNT=$(echo "$BIN_DIRS" | grep -c . 2>/dev/null || echo "0")
fi

if [ -z "$OBJ_DIRS" ]; then
    OBJ_COUNT=0
else
    OBJ_COUNT=$(echo "$OBJ_DIRS" | grep -c . 2>/dev/null || echo "0")
fi

echo ""
print_status "Found $BIN_COUNT bin directories and $OBJ_COUNT obj directories"

# Check if any bin/obj files exist in git history
print_status "Checking git history for bin/ and obj/ files..."
HISTORY_BIN_COUNT=$(git log --all --full-history --name-only --pretty=format: | grep -E "(^|/)bin/" | wc -l 2>/dev/null | tr -d ' ' || echo "0")
HISTORY_OBJ_COUNT=$(git log --all --full-history --name-only --pretty=format: | grep -E "(^|/)obj/" | wc -l 2>/dev/null | tr -d ' ' || echo "0")

echo ""
if [ "$HISTORY_BIN_COUNT" -gt 0 ] || [ "$HISTORY_OBJ_COUNT" -gt 0 ]; then
    print_warning "Found $HISTORY_BIN_COUNT bin/ and $HISTORY_OBJ_COUNT obj/ file references in git history"
    print_warning "These files are taking up space in your repository even though they're in .gitignore"
else
    print_status "No bin/ or obj/ files found in git history"
fi

if [ "$BIN_COUNT" -eq 0 ] && [ "$OBJ_COUNT" -eq 0 ] && [ "$HISTORY_BIN_COUNT" -eq 0 ] && [ "$HISTORY_OBJ_COUNT" -eq 0 ]; then
    print_status "No bin/ or obj/ directories found anywhere. Repository is already clean!"
    exit 0
fi

echo ""
echo "Current working tree directories to be removed:"
if [ "$BIN_COUNT" -gt 0 ]; then
    echo -e "${YELLOW}bin/ directories:${NC}"
    echo "$BIN_DIRS" | sed 's/^/  /'
fi

if [ "$OBJ_COUNT" -gt 0 ]; then
    echo -e "${YELLOW}obj/ directories:${NC}"
    echo "$OBJ_DIRS" | sed 's/^/  /'
fi

echo ""
print_question "Do you want to proceed with removal from working tree? (y/N): "
read -r -n 1 REPLY_WORKING
echo ""

if [[ ! $REPLY_WORKING =~ ^[Yy]$ ]]; then
    print_warning "Operation cancelled by user"
    exit 0
fi

# Ask about history rewriting
REWRITE_HISTORY=false
if [ "$HISTORY_BIN_COUNT" -gt 0 ] || [ "$HISTORY_OBJ_COUNT" -gt 0 ]; then
    echo ""
    print_warning "WARNING: History rewriting will change all commit hashes!"
    print_warning "This is DESTRUCTIVE and will require force-pushing if you have remote repositories."
    print_warning "All collaborators will need to re-clone or rebase their work."
    echo ""
    print_question "Do you want to rewrite git history to remove bin/ and obj/ files completely? (y/N): "
    read -r -n 1 REPLY_HISTORY
    echo ""
    
    if [[ $REPLY_HISTORY =~ ^[Yy]$ ]]; then
        REWRITE_HISTORY=true
        echo ""
        print_warning "FINAL WARNING: This will permanently rewrite your git history!"
        print_question "Are you absolutely sure? Type 'YES' to confirm: "
        read -r FINAL_CONFIRM
        
        if [ "$FINAL_CONFIRM" != "YES" ]; then
            print_warning "History rewriting cancelled. Will only clean working tree."
            REWRITE_HISTORY=false
        fi
    fi
fi

echo ""
print_status "Starting removal process..."

# Remove bin directories from working tree
if [ "$BIN_COUNT" -gt 0 ]; then
    print_status "Removing bin/ directories from filesystem..."
    echo "$BIN_DIRS" | while read -r dir; do
        if [ -n "$dir" ] && [ -d "$dir" ]; then
            echo "  Removing: $dir"
            rm -rf "$dir"
        fi
    done
    
    print_status "Removing bin/ directories from git tracking..."
    echo "$BIN_DIRS" | while read -r dir; do
        if [ -n "$dir" ]; then
            git rm -r --cached "$dir" 2>/dev/null || true
        fi
    done
fi

# Remove obj directories from working tree
if [ "$OBJ_COUNT" -gt 0 ]; then
    print_status "Removing obj/ directories from filesystem..."
    echo "$OBJ_DIRS" | while read -r dir; do
        if [ -n "$dir" ] && [ -d "$dir" ]; then
            echo "  Removing: $dir"
            rm -rf "$dir"
        fi
    done
    
    print_status "Removing obj/ directories from git tracking..."
    echo "$OBJ_DIRS" | while read -r dir; do
        if [ -n "$dir" ]; then
            git rm -r --cached "$dir" 2>/dev/null || true
        fi
    done
fi

# Also remove any individual bin/obj files that might exist
print_status "Removing any tracked bin/obj files from git..."
git ls-files | grep -E "(^|/)bin/" | xargs -r git rm --cached 2>/dev/null || true
git ls-files | grep -E "(^|/)obj/" | xargs -r git rm --cached 2>/dev/null || true

# Rewrite history if requested
if [ "$REWRITE_HISTORY" = true ]; then
    echo ""
    print_status "Creating backup branch before history rewriting..."
    BACKUP_BRANCH="backup-before-prune-$(date +%Y%m%d-%H%M%S)"
    git branch "$BACKUP_BRANCH"
    print_status "Backup created: $BACKUP_BRANCH"
    
    echo ""
    if [ "$FILTER_REPO_AVAILABLE" = true ]; then
        print_status "Using git-filter-repo to rewrite history..."
        git filter-repo --path-glob '*/bin/*' --path-glob '*/obj/*' --path-glob 'bin/*' --path-glob 'obj/*' --invert-paths --force
    else
        print_status "Using git filter-branch to rewrite history (this may take a while)..."
        git filter-branch --force --index-filter \
            'git rm -r --cached --ignore-unmatch */bin */obj bin obj' \
            --prune-empty --tag-name-filter cat -- --all
        
        # Clean up filter-branch refs
        print_status "Cleaning up filter-branch references..."
        git for-each-ref --format="%(refname)" refs/original/ | xargs -n 1 git update-ref -d
        git reflog expire --expire=now --all
        git gc --prune=now --aggressive
    fi
    
    echo ""
    print_status "History rewriting completed!"
    print_warning "Your backup branch is: $BACKUP_BRANCH"
    print_warning "If you have remote repositories, you'll need to force push: git push --force-with-lease --all"
    print_warning "All collaborators will need to re-clone or rebase their work."
fi

echo ""
print_status "Checking git status..."
if git status --porcelain | grep -q "D "; then
    echo ""
    print_status "Files removed from git tracking:"
    git status --porcelain | grep "^D " | sed 's/^D  /  /'
    
    echo ""
    print_warning "Don't forget to commit these changes:"
    echo "  git add -A"
    echo "  git commit -m \"Remove bin/ and obj/ directories from repository\""
else
    print_status "No files were being tracked by git in working tree."
fi

echo ""
print_status "Build directory pruning completed successfully!"
print_status "Your .gitignore already contains patterns to prevent future tracking of bin/ and obj/ directories."

if [ "$REWRITE_HISTORY" = true ]; then
    echo ""
    print_status "Repository size before/after comparison:"
    echo "  Check repository size with: du -sh .git/"
    echo "  The repository should be significantly smaller now."
fi