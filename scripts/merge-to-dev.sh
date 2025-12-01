#!/bin/bash

# Parse command line arguments
NON_INTERACTIVE=false
BRANCH_TYPE=""
BRANCH_NAME=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --non-interactive)
            NON_INTERACTIVE=true
            shift
            ;;
        --branch-type)
            BRANCH_TYPE="$2"
            shift 2
            ;;
        --branch-name)
            BRANCH_NAME="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--non-interactive] [--branch-type TYPE] [--branch-name NAME]"
            exit 1
            ;;
    esac
done

# Function to prompt for input with validation
get_validated_input() {
    local prompt="$1"
    local valid_options="$2"
    local required="$3"

    while true; do
        read -p "$prompt: " input
        if [[ "$required" == "true" && -z "$input" ]]; then
            echo "This field is required. Please try again." >&2
            continue
        fi
        if [[ -n "$valid_options" ]]; then
            IFS=',' read -ra options <<< "$valid_options"
            valid=false
            for option in "${options[@]}"; do
                if [[ "$input" == "$option" ]]; then
                    valid=true
                    break
                fi
            done
            if [[ "$valid" != "true" ]]; then
                echo "Invalid option. Valid options are: $valid_options" >&2
                continue
            fi
        fi
        break
    done

    echo "$input"
}

# Function to merge branch and process changelog
merge_branch_and_update_changelog() {
    local full_branch_name="$1"
    local branch_type="$2"
    local branch_display_name="$3"

    echo "Merging branch: $full_branch_name"

    # Checkout dev branch
    git checkout dev

    # Merge the selected branch
    git merge "$full_branch_name" --no-ff -m "Merge branch '$full_branch_name' into dev"

    if [[ $? -eq 0 ]]; then
        echo "✅ Branch merged successfully"

        # Process changes file
        changes_file_name="${full_branch_name//\//-}"
        changes_file="changes/$changes_file_name.md"
        if [[ -f "$changes_file" ]]; then
            echo "📝 Processing changes file: $changes_file"

            # Read the changes file content
            changes_content=$(cat "$changes_file")

            # Update changelog.md
            timestamp=$(date -u '+%Y-%m-%d %H:%M:%S UTC')

            if [[ -f "changelog.md" ]]; then
                # Prepend to existing changelog
                existing_content=$(cat "changelog.md")
                new_content="# Changelog

## [$timestamp] $branch_display_name ($branch_type)

$changes_content

$existing_content"
            else
                # Create new changelog
                new_content="# Changelog

## [$timestamp] $branch_display_name ($branch_type)

$changes_content"
            fi

            echo "$new_content" > "changelog.md"

            # Delete the changes file
            rm -f "$changes_file"
            echo "✅ Changes content added to changelog.md"
            echo "🗑️  Changes file deleted: $changes_file"

            # Commit the changelog update and file deletion
            git add .
            git commit -m "docs: update changelog with changes from $branch_display_name"

            echo "✅ Changelog update committed"
            CHANGES_PROCESSED=true
        else
            echo "⚠️  No changes file found for $full_branch_name"
            CHANGES_PROCESSED=false
        fi
    else
        echo "❌ Failed to merge branch $full_branch_name"
        exit 1
    fi
}

# Main execution
echo "🔄 Merge to Dev Script"
echo "===================="

# Ensure we're in a git repository and at the root
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    echo "❌ Error: Not in a git repository"
    exit 1
fi

# Change to repository root if not already there
cd "$(git rev-parse --show-toplevel)"

echo "Working directory: $(pwd)"

# Get current branch
current_branch=$(git branch --show-current)
echo "Current branch: $current_branch"

# Validate current branch format and determine branch type
if [[ "$current_branch" =~ ^(system|fix|feature|build|deploy)/ ]]; then
    branch_type=$(echo "$current_branch" | cut -d'/' -f1)
    branch_name=$(echo "$current_branch" | cut -d'/' -f2)
    full_branch_name="$current_branch"
    branch_display_name="$branch_name"

    echo "Branch type: $branch_type"
    echo "Branch name: $branch_display_name"
else
    echo "❌ Current branch '$current_branch' is not a valid branch type (system/, fix/, feature/, build/, deploy/)"
    echo "Please switch to a valid branch or create one using create-branch.sh"
    exit 1
fi

echo "Ready to merge: $full_branch_name"

# Confirm merge
if [[ "$NON_INTERACTIVE" != "true" ]]; then
    confirm=$(get_validated_input "Proceed with merge? (y/N)" "" "false")
    if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
        echo "Merge cancelled."
        exit 0
    fi
fi

# Perform the merge and update changelog
merge_branch_and_update_changelog "$full_branch_name" "$branch_type" "$branch_display_name"

echo ""
echo "🎉 Merge completed successfully!"
echo "📋 Summary:"
echo "  • Merged: $full_branch_name"
echo "  • Type: $branch_type"
if [[ "$CHANGES_PROCESSED" == "true" ]]; then
    echo "  • Changelog updated with changes"
    echo "  • Changes file cleaned up"
fi