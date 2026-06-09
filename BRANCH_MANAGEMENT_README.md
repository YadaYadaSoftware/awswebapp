# Branch Management System

This document describes the automated branch management system that helps organize development work and maintain an up-to-date changelog.

## Overview

The system supports five types of branches organized in folders:
- `build/` - Infrastructure and build system changes
- `deploy/` - Deployment configuration and process changes
- `system/` - System-wide architectural changes
- `feature/` - New feature development
- `fix/` - Bug fixes and patches

## Workflow

### 1. Creating a New Branch

Use the PowerShell script to create a new branch:

```powershell
# Run from the repository root

# Create a feature branch (interactive)
.\scripts\create-branch.ps1

# Or create with parameters
.\scripts\create-branch.ps1 -BranchType "feature" -BranchName "user-authentication" -ChangesDescription "Implement OAuth integration with Google"
```

The script will:
- Prompt for branch type if not provided
- Prompt for branch name if not provided
- Prompt for changes description if not provided
- Create the branch with naming convention: `{type}/{name}`
- Generate a `changes.md` file with the provided description

### 2. Development Process

1. **Make your code changes** in the branch
2. **Update `changes.md`** with detailed information about:
   - What was changed
   - Why it was changed
   - How it affects the system
   - Testing approach
3. **Commit your changes**:
   ```bash
   git add .
   git commit -m "feat: implement user authentication system"
   ```
4. **Push the branch**:
   ```bash
   git push origin feature/user-authentication
   ```

### 3. Merging to Dev Branch

When ready to merge:

```bash
git checkout dev
git merge feature/user-authentication
git push origin dev
```

## Changelog Management

Changelog updating is **not** performed by CI — it happens **locally**, at merge time, via `scripts/merge-to-dev.ps1` (or the `.sh` variant). CI only *validates* that the `changes/` folder is empty (see "What CI enforces" below); it does not edit or commit `changelog.md`.

### What happens when you merge to `dev`

Run `scripts/merge-to-dev.ps1` instead of merging by hand. It:

1. Merges the selected branch into `dev`.
2. Reads the branch's change file under `changes/` (named `{type}-{name}.md`, e.g. `changes/feature-user-authentication.md`).
3. Extracts the first meaningful line as a synopsis and prepends an entry to `changelog.md` (timestamp + branch + synopsis).
4. Removes the consumed `changes/` file so the folder is empty again.

### What CI enforces

The `dev`/`alpha`/`beta`/`app` branches **fail the build if the `changes/` folder is non-empty** ([.github/workflows/zbuild.yml](.github/workflows/zbuild.yml)). This is a guard, not an updater — it forces you to flush pending change files into `changelog.md` (via `scripts/merge-to-dev.ps1`) before those branches will build green.

### Changelog Format

```
# Changelog

## [2024-01-15 14:30:22 UTC] feature/user-authentication
- Implement OAuth integration with Google for user login

## [2024-01-14 09:15:33 UTC] fix/database-connection
- Fix connection pooling issue causing timeouts under load
```

## Branch Naming Conventions

- **build/**: `build/infrastructure-updates`, `build/ci-cd-improvements`
- **deploy/**: `deploy/rollback-strategy`, `deploy/monitoring-setup`
- **system/**: `system/architecture-refactor`, `system/performance-optimization`
- **feature/**: `feature/user-authentication`, `feature/payment-integration`
- **fix/**: `fix/security-patch`, `fix/bug-resolution`

## Best Practices

### For Branch Creation
- Use descriptive branch names that clearly indicate the purpose
- Provide meaningful descriptions in `changes.md`
- Keep branch names lowercase with hyphens for readability

### For Changes Documentation
- First line should be a clear, concise summary (used as changelog entry)
- Include technical details about the implementation
- Document any breaking changes or migration steps
- Note testing approach and validation steps

### For Merging
- Ensure all tests pass before merging
- Update `changes.md` with final implementation details
- Verify no conflicts with other ongoing work
- Consider the impact on dependent systems

## Troubleshooting

### Common Issues

**"changes.md not found" error**
- Ensure you created the branch using the provided script
- Check that `changes.md` exists in the repository root
- Verify the file wasn't accidentally deleted

**Changelog not updating**
- Check that the merge commit message follows expected format
- Verify the branch follows naming convention (type/name)
- Check GitHub Actions logs for detailed error information

**Branch creation script fails**
- Ensure you're running from the repository root directory
- Check that Git is properly initialized
- Verify PowerShell execution policy allows scripts

### Getting Help

If you encounter issues:
1. Check the GitHub Actions logs for the latest `dev` branch push
2. Verify all files are committed before merging
3. Ensure branch naming follows the required convention
4. Check that `changes.md` contains meaningful content

## Automatic Stack Cleanup on Branch Delete

> ⚠️ **GitHub Actions constraint:** the `delete` event only triggers workflows that exist on the **default branch** (`app`). A copy of `cleanup-on-branch-delete.yml` on a feature branch is inert — branch deletions will silently do nothing. This workflow only becomes live once it is merged into `app`.

When a branch is deleted from the remote (via the GitHub UI, the REST API, or `git push origin --delete <branch>`), `.github/workflows/cleanup-on-branch-delete.yml` automatically tears down the CloudFormation stack that branch deployed:

- **Trigger**: GitHub `delete` event, filtered to `ref_type == 'branch'` (tag deletions are ignored).
- **Stack name**: `{branch-leaf}-{processed-domain}` — the same formula the deploy workflow uses (`branch-leaf` is the segment after the final `/`; `processed-domain` is `DOMAIN_NAME` with dots replaced by hyphens).
- **Region**: `us-east-1` only. Non-shared branches never deploy to `us-west-2`, so cross-region cleanup is unnecessary.
- **Protected branches**: `app`, `beta`, `alpha`, and `dev` are exempt. If one of these is deleted, the workflow exits successfully without making any AWS API calls. A `feature/dev`-style branch (leaf segment `dev`) is also treated as protected, by design.
- **What gets deleted**: the CloudFormation stack itself (waiting for `DELETE_COMPLETE` with a 30-minute timeout) and, only on success, the `{branch-leaf}/` prefix in the bootstrap-owned templates bucket that holds packaged SAM templates for the branch. Per [infrastructure/bootstrap.template](infrastructure/bootstrap.template) that bucket is named `{account}-{dashed-domain}-{region}` (e.g. `s3://{account}-appcloud-systems-us-east-1/{branch-leaf}/`). (Note: the cleanup workflow currently references the older `cf-templates-{account}-us-east-1` name instead — a known code bug flagged separately by the audit.)
- **What does not get cleaned up**: ECR images tagged with the branch name (the ECR repo is shared and uses content-addressed tags). Manage these with an ECR lifecycle policy if pruning is desired.

If the stack ends in `DELETE_FAILED` or the waiter times out, the workflow fails red and dumps the last 25 `describe-stack-events` rows to the job log and step summary so a human can investigate.

## Configuration

The system uses these key files:
- `scripts/create-branch.ps1` - Interactive branch creation
- `scripts/merge-to-dev.ps1` - Merge a branch into `dev` and flush its `changes/` file into `changelog.md`
- `scripts/update-changelog.ps1` - Changelog update helper
- `.github/workflows/zbuild.yml` - CI/CD integration (validates the `changes/` folder is empty on `dev`/`alpha`/`beta`/`app`)
- `.github/workflows/cleanup-on-branch-delete.yml` - Stack teardown on branch deletion

No additional configuration is required - the system works out of the box with the existing project setup.
