# Feature Branch Documentation Rule

## Brief overview
All feature branches must include a clear purpose description that explains what the branch implements or fixes. This ensures proper documentation and helps maintain project history.

## Branch naming convention
Feature branches must follow the naming pattern: `feature/meaningful-description` or `fix/issue-description`

## Purpose documentation requirement
- Every feature branch must have a documented purpose in its branch description or PR description
- The purpose should clearly explain what the branch implements or fixes
- The purpose should be concise but informative enough for other developers to understand

## Branch purpose format
The branch purpose should include:
- **What**: Brief description of the feature or fix
- **Why**: Reason for the change (if not obvious)
- **Impact**: Any breaking changes or important notes

## Example purpose statements
- "Add user authentication system with JWT tokens for API security"
- "Fix memory leak in data processing service causing performance degradation"
- "Implement dark mode toggle with user preference persistence"

## Enforcement
- Pull requests without documented purposes will be blocked
- Branch creation should include purpose in the description
- Merge to dev branch will automatically extract purpose for changelog

## Changelog integration
When a feature branch is merged into dev, its documented purpose will be automatically added to the changelog.md file with timestamp and branch information.