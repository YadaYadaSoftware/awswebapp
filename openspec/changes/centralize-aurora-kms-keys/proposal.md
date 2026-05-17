## Why

Today the Aurora `AuroraKmsKey` (multi-region KMS key) lives inside [security.template](../../../infrastructure/security.template), which is a nested stack of every environment's master stack (`dev`, `alpha`, `beta`, `app`). That coupling causes two real problems:

1. **Cost & sprawl on teardown.** The key is `DeletionPolicy: Retain`, so when a non-`app` env stack is destroyed the key stays orphaned and continues to bill (and even a manual deletion forces a minimum 7-day `PendingWindowInDays`). With four envs × two regions, the account accumulates orphaned multi-region keys that nobody owns.
2. **No prod / non-prod blast-radius separation.** The same `AllowGitHubActionsUser` key-policy statement is attached to the `app` key and the `dev`/`alpha`/`beta` keys — anyone or any workflow that can deploy `dev` can grant/decrypt/etc. against the production database's KMS key. And the same CI IAM principal that deploys `dev` can also deploy `app`, so there is no IAM-level separation between prod and non-prod CI operations.

This change centralises KMS into bootstrap stacks with a hard prod/nonprod split, **and** introduces a separate `prod-deployer` GitHub Actions IAM user so that the `app` branch's CI uses different AWS credentials than every other branch's CI. The secondary-region migration (us-west-2 → us-east-2) is tracked separately in [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md) and depends on this change landing first.

## What Changes

- **BREAKING** Add two new bootstrap stack instances per region: `bootstrap-prod` and `bootstrap-nonprod`. Each owns one multi-region `AWS::KMS::Key` (primary in `us-east-1`, `AWS::KMS::ReplicaKey` in the current secondary region). For the duration of this change the secondary region is still `us-west-2`; the follow-up region change re-deploys these stacks to `us-east-2`.
- **BREAKING** Add a new `GitHubActionsUserProd` IAM user in `bootstrap-prod` with its own access key, scoped to the policies needed to deploy the `app` env stack only (CloudFormation + ECR + Aurora + KMS-use against the prod key + the existing `DeploymentPolicy` shape).
- **BREAKING** Remove `AuroraKmsKey` and `AuroraKmsKeyReplica` (and their `AuroraKmsKeyArn-${BranchName}` export) from [security.template](../../../infrastructure/security.template). `SharedLambdaExecutionRole` stays.
- Rewire [backend.template](../../../infrastructure/backend.template) and [db.template](../../../infrastructure/db.template) so the Aurora cluster's `KmsKeyId` is sourced from the bootstrap stack — `bootstrap-prod` when `BranchName == app`, `bootstrap-nonprod` otherwise (including all feature branches that import from `dev`).
- The `bootstrap-prod` KMS key policy grants the `GitHubActionsUserProd` user only the operations RDS needs (`kms:CreateGrant`, `kms:DescribeKey`, `kms:Decrypt`, `kms:Encrypt`, `kms:GenerateDataKey`, `kms:ReEncryptFrom`, `kms:ReEncryptTo`, `kms:RetireGrant`) but denies `kms:ScheduleKeyDeletion`, `kms:DisableKey`, `kms:PutKeyPolicy`, `kms:DeleteAlias`. Those destructive ops are limited to the AWS account root and a `prod-kms-admin` IAM role (assumable by a human operator). The `bootstrap-nonprod` key keeps the current broad policy granted to the existing CI IAM user.
- **BREAKING** Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) so the deploy job selects AWS credentials by branch:
  - `app` branch → `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` GitHub secrets (new).
  - Every other branch → `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` (existing).
- The deploy workflow looks up the Aurora KMS key ARN from SSM Parameter Store (`/taskmanager/kms/${SCOPE}/aurora-key-arn`) where scope is `prod` for `app`, `nonprod` otherwise.
- A one-time sweep schedules deletion of pre-existing orphaned `AuroraKmsKey` resources left behind by previously-deleted env stacks.

## Capabilities

### New Capabilities

- `aurora-kms-key-management`: How the Aurora encryption key is provisioned, where it lives, who can use it vs. who can destroy it, how its lifecycle is decoupled from per-branch env stacks, and how branch-conditional CI credentials enforce the prod/nonprod boundary.

### Modified Capabilities

_None._ The existing `branch-stack-cleanup` spec does not reference KMS keys, so no delta is needed there — the cleanup workflow's behaviour is unchanged (it deletes the stack and S3 prefix; KMS keys now simply aren't in that stack to begin with).

## Impact

**Infrastructure templates (rewritten):**
- [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — accepts a `BootstrapScope` parameter (`prod`, `nonprod`, or omitted for the existing shared stack); adds the multi-region `AuroraKmsKey` / `AuroraKmsKeyReplica`; adds `GitHubActionsUserProd` (only when `IsProdScope`); key policy differs by scope; existing IAM user / ECR / Config rules stay in the unscoped `bootstrap` stack.
- [infrastructure/security.template](../../../infrastructure/security.template) — KMS resources deleted; only `SharedLambdaExecutionRole` and its outputs remain.
- [infrastructure/backend.template](../../../infrastructure/backend.template) — `KmsKeyArn` parameter becomes required (always passed in by master.template from workflow SSM lookup), no longer derived from nested SecurityStack output. `DbStack` no longer depends on `SecurityStack`.
- [infrastructure/master.template](../../../infrastructure/master.template) — `KmsKeyArn` becomes required; conditions/outputs simplified.
- [infrastructure/application.template](../../../infrastructure/application.template) — accepts `KmsKeyArn` directly instead of importing from a dev env export.

**Workflows:**
- [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — new "select AWS credentials by branch" step in the deploy matrix; new "lookup bootstrap KMS key from SSM" step; `KmsKeyArn` passed via SSM-resolved value, not from the env stack's primary-region lookup.

**GitHub repository secrets:**
- New: `AWS_ACCESS_KEY_ID_PROD`, `AWS_SECRET_ACCESS_KEY_PROD` (added by hand after `bootstrap-prod` is first deployed).
- Existing `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` continue to be used by non-`app` branches.

**Existing data (migration required, NOT data-preserving for `dev`):**
- `dev` Aurora cluster: dropped and recreated empty (acceptable per design decision).
- `alpha` / `beta` / `app` Aurora clusters: snapshot → recreate stack with new key → restore. `app` requires a maintenance window with parallel-cluster cutover.

**Out of scope (explicitly):**
- **No region change.** us-west-2 stays the secondary region in this change. The us-west-2 → us-east-2 migration is the sibling change [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md), which depends on this one and lands after it.
- No change to ECR layout, container image content-addressing, or NuGet packaging.
- No change to the application code in `src/`.
