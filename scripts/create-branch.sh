#!/bin/bash

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

# Validate or prompt for branch type
valid_types="build,deploy,system,feature,fix"
if [[ -z "$1" ]]; then
    BranchType=$(get_validated_input "Enter branch type (build/deploy/system/feature/fix)" "$valid_types" "true")
else
    BranchType="$1"
fi

# Validate or prompt for branch name
if [[ -z "$2" ]]; then
    BranchName=$(get_validated_input "Enter branch name (without type prefix)" "" "true")
else
    BranchName="$2"
fi

# Validate or prompt for changes description
if [[ -z "$3" ]]; then
    ChangesDescription=$(get_validated_input "Enter description of changes" "" "true")
else
    ChangesDescription="$3"
fi

# Construct full branch name
fullBranchName="$BranchType/$BranchName"

echo "Creating branch: $fullBranchName"

# Check if branch already exists
if git branch --list "$fullBranchName" | grep -q "$fullBranchName"; then
    echo "Branch '$fullBranchName' already exists. Switching to it..."
    git checkout "$fullBranchName"
else
    echo "Creating new branch: $fullBranchName"
    git checkout -b "$fullBranchName"
fi

# Create branch-specific changes file in changes folder
changesContent="# Changes for $fullBranchName

**Purpose:** $ChangesDescription

**Date Created:** $(date '+%Y-%m-%d %H:%M:%S')"

# Create changes folder if it doesn't exist
if [[ ! -d "changes" ]]; then
    mkdir "changes"
    echo "Created changes/ directory"
fi

changesFileName="${fullBranchName//\//-}"
changesPath="changes/$changesFileName.md"
if [[ -f "$changesPath" ]]; then
    echo "changes/$changesFileName.md already exists. Updating content..."
else
    echo "Creating changes/$changesFileName.md..."
fi

echo "$changesContent" > "$changesPath"

echo "Branch '$fullBranchName' created successfully!"
echo "changes.md has been created with the provided description."
echo ""
echo "Next steps:"
echo "1. Make your code changes"
echo "2. Update changes.md with detailed information about your changes"
echo "3. Commit your changes: git add . && git commit -m 'Your commit message'"
echo "4. Push branch to origin: git push origin $fullBranchName"
echo "5. Merge into dev branch when ready"