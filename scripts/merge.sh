#!/bin/bash

# Function to list local branches and let user select
select_branch() {
    local prompt="$1"
    local current_branch=$(git branch --show-current)

    echo "$prompt" >&2
    echo "Available local branches:" >&2
    echo "" >&2

    # List branches with numbers, highlighting current branch
    local branches=()
    local i=1
    while IFS= read -r branch; do
        branch=$(echo "$branch" | sed 's/^[ *]*//')  # Remove leading spaces and asterisks
        branches+=("$branch")
        if [ "$branch" = "$current_branch" ]; then
            echo "  $i) $branch (current)" >&2
        else
            echo "  $i) $branch" >&2
        fi
        ((i++))
    done < <(git branch --format='%(refname:short)')

    echo "" >&2
    local choice
    local num_branches=${#branches[@]}
    while true; do
        read -p "Enter branch number: " choice >&2
        # Simple validation
        if echo "$choice" | grep -q '^[0-9][0-9]*$'; then
            if [ "$choice" -ge 1 ] && [ "$choice" -le "$num_branches" ]; then
                echo "${branches[$((choice-1))]}"
                return 0
            fi
        fi
        echo "Invalid choice. Please enter a number between 1 and $num_branches" >&2
    done
}

# Function to confirm action
confirm_action() {
    local prompt="$1"
    local response
    read -p "$prompt (y/N): " response
    case "$response" in
        [yY]|[yY][eE][sS])
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

# Main execution
echo "🔄 Git Branch Merge Tool"
echo "========================"

# Ensure we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    echo "❌ Error: Not in a git repository"
    exit 1
fi

# Change to repository root if not already there
cd "$(git rev-parse --show-toplevel)"

echo "Working directory: $(pwd)"
echo ""

# Select target branch (branch to merge into)
TARGET_BRANCH=$(select_branch "Select the TARGET branch (branch to merge INTO):")

# Select source branch (branch to merge from)
SOURCE_BRANCH=$(select_branch "Select the SOURCE branch (branch to merge FROM):")

# Validate selection
if [ "$TARGET_BRANCH" = "$SOURCE_BRANCH" ]; then
    echo "❌ Error: Cannot merge a branch into itself"
    exit 1
fi

echo ""
echo "📋 Merge Summary:"
echo "  Target branch: $TARGET_BRANCH"
echo "  Source branch: $SOURCE_BRANCH"
echo ""

# Confirm merge
if ! confirm_action "Proceed with merge?"; then
    echo "Merge cancelled."
    exit 0
fi

# Perform the merge
echo ""
echo "🔄 Performing merge..."
echo "Switching to target branch: $TARGET_BRANCH"
git checkout "$TARGET_BRANCH"

if [ $? -ne 0 ]; then
    echo "❌ Failed to switch to target branch $TARGET_BRANCH"
    exit 1
fi

echo "Merging $SOURCE_BRANCH into $TARGET_BRANCH..."
git merge "$SOURCE_BRANCH" --no-ff -m "Merge branch '$SOURCE_BRANCH' into $TARGET_BRANCH"


if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Branch merged successfully!"

    # Ask about deleting the source branch (only for non-protected branches)
    echo ""
    if [[ " app test dev " == *" $SOURCE_BRANCH "* ]]; then
        echo "ℹ️  Branch '$SOURCE_BRANCH' is a protected branch and will not be deleted."
    else
        if confirm_action "Delete the source branch '$SOURCE_BRANCH'?"; then
        echo "Deleting branch: $SOURCE_BRANCH"
        git branch -d "$SOURCE_BRANCH"

        if [ $? -eq 0 ]; then
            echo "✅ Branch '$SOURCE_BRANCH' deleted successfully"
        else
            echo "⚠️  Failed to delete branch '$SOURCE_BRANCH' (it may have unmerged commits)"
            echo "   Use 'git branch -D $SOURCE_BRANCH' to force delete if needed"
        fi
    else
        echo "Branch '$SOURCE_BRANCH' kept."
    fi

    echo ""
    echo "🎉 Merge operation completed!"
    echo "📋 Summary:"
    echo "  • Merged: $SOURCE_BRANCH → $TARGET_BRANCH"
    echo "  • Current branch: $(git branch --show-current)"
else
    echo "❌ Failed to merge branch $SOURCE_BRANCH into $TARGET_BRANCH"
    echo ""
    echo "Troubleshooting:"
    echo "  • Check for merge conflicts and resolve them"
    echo "  • Ensure both branches are up to date"
    echo "  • Run 'git status' to see current state"
    exit 1
fi