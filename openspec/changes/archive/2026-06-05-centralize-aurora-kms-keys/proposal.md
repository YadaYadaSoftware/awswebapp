## Why

Today the Aurora `AuroraKmsKey` (multi-region KMS key) lives inside [security.template](../../../infrastructure/security.template), which is a nested stack of every environment's master stack (`dev`, `alpha`, `beta`, `app`). That coupling causes two real problems:

1. **Cost & sprawl on teardown.** The key is `DeletionPolicy: Retain`, so when a non-`app` env stack is destroyed the key stays orphaned and continues to bill (and even a manual deletion forces a minimum 7-day `PendingWindowInDays`). With four envs × two regions, the account accumulates orphaned multi-region keys that nobody owns.
2. **No prod / non-prod blast-radius separation.** The same `AllowGitHubActionsUser` key-policy statement is attached to the `app` key and the `dev`/`alpha`/`beta` keys — anyone or any workflow that can deploy `dev` can grant/decrypt/etc. against the production database's KMS key. And the same CI IAM principal that deploys `dev` can also deploy `app`, so there is no IAM-level separation between prod and non-prod CI operations.

This change centralises both Aurora KMS keys (prod + nonprod) and both GitHub Actions IAM users into a single regional `bootstrap` stack, with the prod key's resource policy enforcing the access boundary via an explicit `NotPrincipal+Deny` clause. The secondary-region migration (us-west-2 → us-east-2) is tracked separately in [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md) and depends on this change landing first.

## What Changes

- **BREAKING** Tear down the existing single `bootstrap` stack per region and replace it with **one** rewritten `bootstrap` stack per region that owns everything: the legacy shared infrastructure (ECR, ApiGateway logs role + account, Config log-retention rule + Lambda + SSM doc, S3 templates-bucket policy) **plus** both Aurora multi-region KMS keys (prod + nonprod) with their aliases and SSM parameters, **plus** both GitHub Actions IAM users (`GitHubActionsUser` for non-prod, `GitHubActionsUserProd` for prod) with their access keys and deployment policies, **plus** the `prod-kms-admin` IAM role.
- **BREAKING** Rewrite [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) as a single consolidated template. Parameters: `TemplatesBucketName`, `RetentionDays`, `PrimaryNonprodKeyArn` (Default: `""`, set for replica region deploys), `PrimaryProdKeyArn` (Default: `""`, set for replica region deploys), `KeyAdminPrincipalArn` (Default: `""`, used during initial setup to satisfy KMS's lockout-safety check on the prod key). Only conditions: `IsPrimary`/`IsReplica` (derived from `PrimaryNonprodKeyArn` presence) and `HasKeyAdmin`. No `BootstrapScope`, no `IsProd`/`IsNonprod` conditions — both prod and nonprod resources coexist in the same stack and are differentiated by resource name.
- **BREAKING** Both non-prod and prod GitHub Actions access keys are reissued by this change (new IAM users created by the rewritten stack). Both `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` **and** the new `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` GitHub secrets must be set from the new stack outputs.
- **BREAKING** Remove `AuroraKmsKey` and `AuroraKmsKeyReplica` (and their `AuroraKmsKeyArn-${BranchName}` export) from [security.template](../../../infrastructure/security.template). `SharedLambdaExecutionRole` stays.
- Rewire [backend.template](../../../infrastructure/backend.template) and [db.template](../../../infrastructure/db.template) so the Aurora cluster's `KmsKeyId` is sourced from the bootstrap stack's SSM parameter — the prod ARN when `BranchName == app`, the nonprod ARN otherwise.
- The prod KMS key policy grants `GitHubActionsUserProd` only the operations RDS needs (`kms:CreateGrant`, `kms:DescribeKey`, `kms:Decrypt`, `kms:Encrypt`, `kms:GenerateDataKey`, `kms:ReEncryptFrom`, `kms:ReEncryptTo`, `kms:RetireGrant`). Destructive ops on the prod key are limited to the AWS account root and the `prod-kms-admin` IAM role (assumable by a human operator). The policy also includes an explicit `Effect: Deny` with `NotPrincipal` listing only the authorized principals — this closes the IAM-delegation pathway that the standard `AllowAccountRoot kms:*` statement would otherwise enable for any IAM user with `kms:*` permissions (including the non-prod CI user). The nonprod key keeps a broad policy granted to `GitHubActionsUser`.
- **BREAKING** Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) so the deploy job selects AWS credentials by branch:
  - `app` branch → `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` GitHub secrets.
  - Every other branch → `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` (new value, sourced from the consolidated `bootstrap`'s `GitHubActionsUser`).
- The deploy workflow looks up the Aurora KMS key ARN from SSM Parameter Store (`/taskmanager/kms/{prod,nonprod}/aurora-key-arn`) for the matrix region, with the prod path used only for the `app` branch.
- A one-time sweep schedules deletion of pre-existing orphaned `AuroraKmsKey` resources left behind by previously-deleted env stacks.

**Accepted consequences of the teardown approach:**
- The existing `WebECRRepository` is destroyed and recreated by the rewritten bootstrap — one Docker rebuild on the next deploy (~3 minutes per region).
- The existing non-prod CI access key is invalidated; the `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub secrets must be rotated from the new stack outputs.
- During the teardown window between the old `bootstrap` stack going away and the rewritten `bootstrap` coming up, the `TemplatesBucketPolicy` is gone — any concurrent CI deploys will fail. The migration must be done during a deploy quiet window.
- Config rule evaluation history for log-group retention is lost on the old `LogRetentionConfigRule`.

## Capabilities

### New Capabilities

- `aurora-kms-key-management`: How the Aurora encryption keys are provisioned, where they live (a single `bootstrap` stack per region with both prod and nonprod keys coexisting), who can use vs. destroy each, how their lifecycle is decoupled from per-branch env stacks, and how branch-conditional CI credentials + the prod key's `NotPrincipal+Deny` together enforce the prod/nonprod boundary.

### Modified Capabilities

_None._ The existing `branch-stack-cleanup` spec does not reference KMS keys, so no delta is needed there — the cleanup workflow's behaviour is unchanged (it deletes the stack and S3 prefix; KMS keys now simply aren't in that stack to begin with).

## Impact

**Infrastructure templates (rewritten):**
- [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — **rewritten as a single consolidated template** owning everything: shared infrastructure (ECR, ApiGateway, Config, S3 policy) + both Aurora KMS keys + both GitHub Actions IAM users + `prod-kms-admin` role. Deployed once per region as stack name `bootstrap`. Conditions: only `IsPrimary`/`IsReplica` (for the KMS primary vs replica shape) and `HasKeyAdmin` (for the optional extra prod-key admin during setup).
- [infrastructure/security.template](../../../infrastructure/security.template) — KMS resources deleted in Phase 5; only the `Export` name on `AuroraKmsKeyArn` is dropped in Section 4. `SharedLambdaExecutionRole` stays.
- [infrastructure/backend.template](../../../infrastructure/backend.template) — `KmsKeyArn` parameter becomes required (always passed in by master.template from workflow SSM lookup), no longer derived from nested SecurityStack output. `DbStack` no longer depends on `SecurityStack`.
- [infrastructure/master.template](../../../infrastructure/master.template) — `KmsKeyArn` becomes required; `HasKmsKeyArn` condition removed.
- [infrastructure/application.template](../../../infrastructure/application.template) — no change (no KMS references; feature branches consume `dev`'s backend via `EnvironmentToImport`).

**Workflows:**
- [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — new "Select AWS credentials by branch" step in the deploy matrix; new "Lookup bootstrap KMS key from SSM" step; `KmsKeyArn` passed via the SSM-resolved env var, not from the env stack's primary-region lookup.

**GitHub repository secrets:**
- New: `AWS_ACCESS_KEY_ID_PROD`, `AWS_SECRET_ACCESS_KEY_PROD` (added by hand after the consolidated bootstrap is first deployed).
- **Re-issued:** `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` — the old values become invalid when the legacy `bootstrap` stack is torn down. New values come from the consolidated bootstrap's `GitHubActionsUser` access key.

**Existing data (migration required, NOT data-preserving for `dev`):**
- `dev` Aurora cluster: dropped and recreated empty (acceptable per design decision).
- `alpha` / `beta` / `app` Aurora clusters: snapshot → recreate stack with new key → restore. `app` requires a maintenance window with parallel-cluster cutover.

**Out of scope (explicitly):**
- **No region change.** us-west-2 stays the secondary region in this change. The us-west-2 → us-east-2 migration is the sibling change [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md), which depends on this one and lands after it.
- **No bootstrap stack split.** Both prod and nonprod KMS keys / IAM users live in the same stack. The security boundary is enforced by the prod key's `NotPrincipal+Deny`, not by stack-level isolation. (An earlier draft split into three stacks; that was simplified to one stack to reduce operational complexity — the security boundary is preserved by the key policy itself.)
- No change to ECR layout, container image content-addressing, or NuGet packaging.
- No change to the application code in `src/`.
