## Why

Today the Aurora `AuroraKmsKey` (multi-region KMS key) lives inside [security.template](../../../infrastructure/security.template), which is a nested stack of every environment's master stack (`dev`, `alpha`, `beta`, `app`). That coupling causes three real problems:

1. **Cost & sprawl on teardown.** The key is `DeletionPolicy: Retain`, so when a non-`app` env stack is destroyed the key stays orphaned and continues to bill (and even a manual deletion forces a minimum 7-day `PendingWindowInDays`). With four envs × two regions, the account accumulates orphaned multi-region keys that nobody owns.
2. **No prod / non-prod blast-radius separation.** The same `AllowGitHubActionsUser` key-policy statement is attached to the `app` key and the `dev`/`alpha`/`beta` keys — anyone or any workflow that can deploy `dev` can grant/decrypt/etc. against the production database's KMS key.
3. **Operational drag of the secondary region.** us-west-2 is materially more expensive than us-east-2 for the same workload, and we have no architectural reason to be there.

This change centralises KMS into bootstrap stacks with a hard prod/nonprod split, and migrates the secondary region from us-west-2 to us-east-2 as part of the same teardown wave (the multi-region replicas have to be re-laid anyway).

## What Changes

- **BREAKING** Add two new bootstrap stack instances per region: `bootstrap-prod` and `bootstrap-nonprod`. Each owns one multi-region `AWS::KMS::Key` (primary in `us-east-1`, `AWS::KMS::ReplicaKey` in the secondary region).
- **BREAKING** Remove `AuroraKmsKey` and `AuroraKmsKeyReplica` (and their `AuroraKmsKeyArn-${BranchName}` export) from [security.template](../../../infrastructure/security.template). `SharedLambdaExecutionRole` stays.
- Rewire [backend.template](../../../infrastructure/backend.template) and [db.template](../../../infrastructure/db.template) so the Aurora cluster's `KmsKeyId` is sourced from the bootstrap stack — `bootstrap-prod` when `BranchName == app`, `bootstrap-nonprod` otherwise (including all feature branches that import from `dev`).
- The `bootstrap-prod` KMS key policy grants `kms:CreateGrant`/`kms:DescribeKey`/`kms:Decrypt`/`kms:Encrypt`/`kms:GenerateDataKey` to the GitHub Actions user (the operations RDS needs to encrypt with the key) but **denies** `kms:ScheduleKeyDeletion`, `kms:DisableKey`, `kms:PutKeyPolicy`, `kms:DeleteAlias`. Those destructive ops are limited to a `prod-kms-admin` principal (IAM role assumed only by a human operator). The `bootstrap-nonprod` key keeps the current broad policy.
- **BREAKING** Replace `us-west-2` with `us-east-2` as `AWS_REGION_SECONDARY`. All existing `us-west-2` stacks (bootstrap, app/beta/alpha master) are torn down; the new multi-region KMS replicas, Aurora regional clusters, and DNS health checks are laid down in `us-east-2`. The Aurora Global Cluster for `app`/`beta`/`alpha` is rebuilt with `us-east-2` as the secondary.
- Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) so the deploy job:
  - Targets `us-east-2` instead of `us-west-2`.
  - Looks up the Aurora KMS key from `bootstrap-prod` or `bootstrap-nonprod` (per branch) instead of `Get primary region outputs for secondary`'s `AuroraKmsKeyArn` lookup on the env stack.
  - The `app` deploy job uses the privileged "prod deployer" credentials (separate GitHub secret), other branches use the existing non-prod credentials.
- Add SES domain-identity + DKIM in `us-east-2` (parallels existing per-region setup documented in [CLAUDE.md](../../../CLAUDE.md)).

## Capabilities

### New Capabilities

- `aurora-kms-key-management`: How the Aurora encryption key is provisioned, where it lives, who can use it vs. who can destroy it, and how its lifecycle is decoupled from per-branch env stacks.
- `multi-region-deployment-topology`: Which AWS regions the platform deploys to as primary and secondary, how branches map to multi-region vs single-region, and how secondary-region region selection is parameterised.

### Modified Capabilities

_None._ The existing `branch-stack-cleanup` spec does not reference KMS keys, so no delta is needed there — the cleanup workflow's behaviour is unchanged (it deletes the stack and S3 prefix; KMS keys now simply aren't in that stack to begin with).

## Impact

**Infrastructure templates (rewritten):**
- [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — accepts a `BootstrapScope` parameter (`prod` or `nonprod`); adds the multi-region `AuroraKmsKey` / `AuroraKmsKeyReplica`; key policy differs by scope; existing IAM user / ECR / Config rules stay in a single shared bootstrap stack instance (not duplicated).
- [infrastructure/security.template](../../../infrastructure/security.template) — KMS resources deleted; only `SharedLambdaExecutionRole` and its outputs remain.
- [infrastructure/backend.template](../../../infrastructure/backend.template) — `KmsKeyArn` parameter becomes required (always passed in by master.template from workflow lookup), no longer derived from nested SecurityStack output.
- [infrastructure/master.template](../../../infrastructure/master.template) — `KmsKeyArn` becomes required; conditions/outputs simplified.

**Workflows:**
- [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — `AWS_REGION_SECONDARY: us-east-2`; new "lookup bootstrap KMS key" step; branch-conditional AWS credentials for `app` vs non-`app`.
- [.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml) — no template changes needed, but verify the region constant matches.

**Existing data (migration required, NOT data-preserving for non-prod):**
- `dev` / `alpha` / `beta` / `app` Aurora clusters: `KmsKeyId` is immutable, so each cluster must be snapshotted, the env stack recreated against the new key, and the cluster restored. Non-prod envs may simply be dropped and recreated empty.
- `us-west-2` Aurora secondary regional clusters for `app` / `beta` / `alpha`: removed from the global cluster, deleted, then re-added in `us-east-2`.

**External dependencies:**
- AWS SES: verify `appcloud.systems` domain identity + DKIM in `us-east-2` before any deploy in that region runs.
- AWS Route 53 health checks for DNS failover: re-create against `us-east-2` ALB endpoints.

**Out of scope (explicitly):**
- No change to ECR layout, container image content-addressing, or NuGet packaging.
- No change to the application code in `src/` — this is entirely an infra and pipeline change.
