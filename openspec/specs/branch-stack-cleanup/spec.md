# branch-stack-cleanup Specification

## Purpose
Automated teardown of per-branch CloudFormation stacks (and their staged template artifacts in S3) when the source branch is deleted from the GitHub repository, so that abandoned/merged feature branches do not leave orphaned AWS infrastructure running and billing.

## Requirements

### Requirement: Trigger on remote branch deletion

The system SHALL run a cleanup workflow exactly once when a branch is deleted from the GitHub repository, regardless of whether the deletion was initiated via the GitHub UI, REST API, or `git push --delete`. The workflow SHALL NOT run for tag deletions.

#### Scenario: Branch deleted via GitHub UI
- **WHEN** a developer deletes the branch `feature/login-banner` from the GitHub UI
- **THEN** the cleanup workflow starts with `github.event.ref` equal to `feature/login-banner` and `github.event.ref_type` equal to `branch`

#### Scenario: Branch deleted via git push
- **WHEN** a developer runs `git push origin --delete fix/typo`
- **THEN** the cleanup workflow starts with `github.event.ref` equal to `fix/typo`

#### Scenario: Tag deleted
- **WHEN** a tag (rather than a branch) is deleted
- **THEN** the cleanup workflow performs no AWS operations and exits successfully

### Requirement: Protect shared-infrastructure branches

The system SHALL NOT delete any CloudFormation stack when the deleted branch's leaf name is `app`, `beta`, `alpha`, or `dev`. The workflow SHALL exit successfully in this case so that protected-branch deletion (which should never happen but might) does not produce a red CI signal of its own.

#### Scenario: Protected branch leaf is detected
- **WHEN** the deleted ref is `dev` (or its leaf segment is `app`, `beta`, or `alpha`)
- **THEN** the workflow logs a "protected branch — skipping" message and exits with status 0 before any AWS API call is made

#### Scenario: Feature branch nested under a protected name
- **WHEN** the deleted ref is `feature/dev` (leaf segment `dev`)
- **THEN** the workflow treats it as protected and skips deletion, matching the conservative behavior of the deploy job which would have created or updated the shared `dev` stack for this branch

### Requirement: Compute the target stack name deterministically

The system SHALL compute the CloudFormation stack name as `{branch-leaf}-{processed-domain}` where:
- `branch-leaf` is the substring of the deleted ref after the final `/` (or the full ref if no `/` is present),
- `processed-domain` is the value of `secrets.DOMAIN_NAME` with every `.` replaced by `-`.

This formula MUST match the formula used by the deploy workflow's stack creation step.

#### Scenario: Simple branch name
- **WHEN** the deleted ref is `add-login` and `DOMAIN_NAME` is `appcloud.systems`
- **THEN** the computed stack name is `add-login-appcloud-systems`

#### Scenario: Type-prefixed branch
- **WHEN** the deleted ref is `feature/profile-page` and `DOMAIN_NAME` is `appcloud.systems`
- **THEN** the computed stack name is `profile-page-appcloud-systems`

### Requirement: Operate only in us-east-1

The cleanup workflow SHALL target only the `us-east-1` AWS region. It SHALL NOT attempt any operations in `us-west-2` or any other region.

#### Scenario: Region is fixed
- **WHEN** the workflow configures AWS credentials and issues CloudFormation API calls
- **THEN** every AWS API call uses `us-east-1` as its region

### Requirement: Skip cleanly when the stack does not exist

The system SHALL handle the case where the computed stack name does not correspond to any existing CloudFormation stack (for example, because the branch was deleted without ever deploying). The workflow SHALL exit successfully with a log message indicating no stack was found.

#### Scenario: Stack absent
- **WHEN** `aws cloudformation describe-stacks --stack-name <name>` returns `ValidationError: Stack with id <name> does not exist`
- **THEN** the workflow logs "stack not found — nothing to delete" and exits with status 0

### Requirement: Delete the stack and wait for terminal state

The system SHALL request stack deletion via `aws cloudformation delete-stack` and then synchronously wait for the stack to reach a terminal state. The workflow SHALL fail (non-zero exit) if the stack ends in `DELETE_FAILED` or if the wait exceeds 30 minutes.

#### Scenario: Stack deletes cleanly
- **WHEN** the stack exists and CloudFormation completes deletion of all resources
- **THEN** the workflow reports success and the stack reaches `DELETE_COMPLETE`

#### Scenario: Stack deletion fails
- **WHEN** a resource in the stack (e.g. an ENI still attached to a Fargate task) blocks deletion and the stack enters `DELETE_FAILED`
- **THEN** the workflow exits non-zero and the workflow summary surfaces the stack name, region, and failure state

#### Scenario: Deletion exceeds timeout
- **WHEN** the stack has not reached a terminal state after 30 minutes
- **THEN** the AWS CLI waiter exits non-zero and the workflow fails red

### Requirement: Clean up staged template artifacts after stack deletion

The system SHALL remove the per-branch prefix in the CloudFormation templates S3 bucket (`s3://cf-templates-{account}-us-east-1/{branch-leaf}/`) only after the stack has reached `DELETE_COMPLETE`. The S3 cleanup step SHALL NOT cause the workflow to fail if the prefix does not exist or is already empty.

#### Scenario: Templates present
- **WHEN** the stack has just reached `DELETE_COMPLETE` and `s3://cf-templates-{account}-us-east-1/{branch-leaf}/` contains one or more packaged templates
- **THEN** the workflow deletes all objects under that prefix and reports them in the workflow summary

#### Scenario: Templates already absent
- **WHEN** the prefix does not exist or contains no objects
- **THEN** the workflow logs the empty result and exits successfully

#### Scenario: Stack deletion failed
- **WHEN** the stack ended in `DELETE_FAILED`
- **THEN** the S3 cleanup step does not run, preserving the packaged template that may be needed to retry deletion manually

### Requirement: Serialize against any in-flight deploy for the same branch

The cleanup workflow SHALL share a GitHub Actions `concurrency` group with the deploy job for the same branch, so that cleanup cannot run while a deploy for the same branch is still in progress.

#### Scenario: Concurrency group matches deploy
- **WHEN** the deploy workflow is using concurrency group `deploy-us-east-1-{branch-leaf}` for a given branch
- **THEN** the cleanup workflow uses the same concurrency group and waits (with `cancel-in-progress: false`) for any active deploy to finish before starting

### Requirement: Self-document the run

The workflow SHALL write a summary to `$GITHUB_STEP_SUMMARY` containing at minimum: the deleted ref, the computed stack name, the AWS region, the pre-check stack-exists result, the final stack status, and the count of S3 objects cleaned up. The summary SHALL be written whether the workflow succeeds, skips, or fails.

#### Scenario: Successful deletion
- **WHEN** the workflow completes a successful stack deletion
- **THEN** the summary shows the stack name, `us-east-1`, `DELETE_COMPLETE`, and the number of deleted S3 objects

#### Scenario: Protected branch skip
- **WHEN** the workflow skips because the branch is protected
- **THEN** the summary records the branch name and the reason for skipping (no AWS section is required)

#### Scenario: Failure
- **WHEN** the workflow fails due to `DELETE_FAILED` or timeout
- **THEN** the summary includes the stack name, region, and the final stack status
