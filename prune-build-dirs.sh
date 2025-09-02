#!/bin/bash

# Script to prune all bin/ and obj/ directories from the git repository
# This script will:
# 1. Find and remove all bin/ and obj/ directories from the working tree
# 2. Remove them from git tracking if they were previously committed
# 3. Show a summary of what was removed

echo "=== .NET Build Directory Pruning Script ==="
echo "This script will remove all bin/ and obj/ directories from the repository"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
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

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository!"
    exit 1
fi

print_status "Scanning for bin/ and obj/ directories..."

# Find all bin and obj directories (case insensitive)
BIN_DIRS=$(find . -type d -iname "bin" 2>/dev/null | grep -v ".git")
OBJ_DIRS=$(find . -type d -iname "obj" 2>/dev/null | grep -v ".git")

# Count directories found
BIN_COUNT=$(echo "$BIN_DIRS" | grep -c . 2>/dev/null || echo "0")
OBJ_COUNT=$(echo "$OBJ_DIRS" | grep -c . 2>/dev/null || echo "0")

echo ""
print_status "Found $BIN_COUNT bin directories and $OBJ_COUNT obj directories"

if [ "$BIN_COUNT" -eq 0 ] && [ "$OBJ_COUNT" -eq 0 ]; then
    print_status "No bin/ or obj/ directories found. Repository is already clean!"
    exit 0
fi

echo ""
echo "Directories to be removed:"
if [ "$BIN_COUNT" -gt 0 ]; then
    echo -e "${YELLOW}bin/ directories:${NC}"
    echo "$BIN_DIRS" | sed 's/^/  /'
fi

if [ "$OBJ_COUNT" -gt 0 ]; then
    echo -e "${YELLOW}obj/ directories:${NC}"
    echo "$OBJ_DIRS" | sed 's/^/  /'
fi

echo ""
read -p "Do you want to proceed with removal? (y/N): " -n 1 -r
echo ""

if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    print_warning "Operation cancelled by user"
    exit 0
fi

echo ""
print_status "Starting removal process..."

# Remove bin directories
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

# Remove obj directories
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
    print_status "No files were being tracked by git. Repository is clean!"
fi

echo ""
print_status "Build directory pruning completed successfully!"
print_status "Your .gitignore already contains patterns to prevent future tracking of bin/ and obj/ directories."