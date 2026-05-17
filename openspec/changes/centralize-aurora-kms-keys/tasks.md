## 1. Phase 0 — Prereqs

- [ ] 1.1 Identify the existing human admin IAM user (or SSO role) that will deploy `bootstrap-shared`, `bootstrap-prod`, and `bootstrap-nonprod` for the first time. Confirm that user/role has AdministratorAccess or equivalent CloudFormation+IAM+KMS create permissions.
- [ ] 1.2 Inventory existing orphaned `AuroraKmsKey` resources in `us-east-1` and `us-west-2` (description matches `Multi-region KMS key for Aurora Global Database encryption - *`); save the list to `openspec/changes/centralize-aurora-kms-keys/orphaned-keys.txt` for the Phase 5 sweep.
- [ ] 1.3 Snapshot the legacy `bootstrap` stack's current state for rollback reference: `aws cloudformation get-template --stack-name bootstrap --region us-east-1 > openspec/changes/centralize-aurora-kms-keys/legacy-bootstrap-template.yaml` (and same for `us-west-2`). Note the existing values of `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub secrets — they will be invalidated by this change.

## 2. Phase 1 — Template work (teardown + rebuild)

### 2a. New `bootstrap-shared.template`

- [ ] 2.1 Create [infrastructure/bootstrap-shared.template](../../../infrastructure/bootstrap-shared.template) as a new file. Parameters: `TemplatesBucketName` (required), `RetentionDays` (default 7).
- [ ] 2.2 Add resources to `bootstrap-shared.template`: `WebECRRepository` (with explicit `RepositoryName: !Sub "ecr-${AWS::AccountId}-${AWS::Region}"` to keep workflow image URIs valid across the recreation; same lifecycle policy and image-scanning config as the legacy version), `ApiGatewayCloudWatchLogsRole`, `ApiGatewayAccount`, `LogRetentionConfigFunction`, `ConfigRuleRole`, `LogRetentionConfigRule`, `LogRetentionRemediationDocument`, `SSMAutomationRole`, `LogRetentionRemediation`, `ConfigInvokeLambdaPermission`, `TemplatesBucketPolicy`.
- [ ] 2.3 Add outputs to `bootstrap-shared.template`: `WebECRRepositoryName`, `WebECRRepositoryUri`, `ApiGatewayCloudWatchLogsRoleArn`, `TemplatesBucketName` — each `Export`ed under the same names the legacy `bootstrap` stack used so any downstream `Fn::ImportValue` references continue to resolve.

### 2b. Rewrite `bootstrap.template` (gut legacy, add scoped KMS+IAM)

- [ ] 2.4 Delete all legacy resources, parameters, conditions, and outputs from [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — including the `BootstrapScope` / `Conditions` scaffold left behind by the abandoned in-place approach. Start from a clean header (AWSTemplateFormatVersion, Description).
- [ ] 2.5 Add parameters: `BootstrapScope` (required, `AllowedValues: [prod, nonprod]`, no default), `IsPrimaryRegion` (required, `AllowedValues: ["true", "false"]`), `PrimaryKeyArn` (`Default: ""`, used when `IsPrimaryRegion=false`).
- [ ] 2.6 Add conditions: `IsProd` (`!Equals [!Ref BootstrapScope, "prod"]`), `IsNonprod` (`!Equals [!Ref BootstrapScope, "nonprod"]`), `IsPrimary` (`!Equals [!Ref IsPrimaryRegion, "true"]`), `IsReplica` (`!Equals [!Ref IsPrimaryRegion, "false"]`).
- [ ] 2.7 Add `AuroraKmsKey` resource (`Type: AWS::KMS::Key`, `MultiRegion: true`, `DeletionPolicy: Retain`, `PendingWindowInDays: 7`) — gated on `IsPrimary`. KeyPolicy uses `!If [IsProd, <prod-policy>, <nonprod-policy>]` — see 2.9.
- [ ] 2.8 Add `AuroraKmsKeyReplica` resource (`Type: AWS::KMS::ReplicaKey`, `PrimaryKeyArn: !Ref PrimaryKeyArn`, `DeletionPolicy: Retain`, `PendingWindowInDays: 7`) — gated on `IsReplica`. Same KeyPolicy `!If` as 2.7.
- [ ] 2.9 Author the two KMS KeyPolicy bodies as YAML anchors or `!If`-driven blocks:
  - **Prod policy** (used when `IsProd`): allow account root `kms:*`; allow `rds.amazonaws.com` service the Aurora-use ops (`CreateGrant`, `DescribeKey`, `Decrypt`, `Encrypt`, `GenerateDataKey`, `ReEncryptFrom`, `ReEncryptTo`, `RetireGrant`); allow `!GetAtt GitHubActionsUserProd.Arn` the same Aurora-use ops + `ListGrants`, `RevokeGrant`; allow `!GetAtt ProdKmsAdminRole.Arn` `kms:*`. **No** `GitHubActionsUser` reference. **No** `ScheduleKeyDeletion`/`DisableKey`/`PutKeyPolicy`/`DeleteAlias`/`UpdateAlias`/`ReplicateKey`/`CreateAlias` granted to `GitHubActionsUserProd`.
  - **Nonprod policy** (used when `IsNonprod`): allow account root `kms:*`; allow `rds.amazonaws.com` the same Aurora-use ops; allow `!GetAtt GitHubActionsUser.Arn` the broad set the legacy `security.template` granted (the full alias/grant/encrypt/decrypt set).
- [ ] 2.10 Add `AWS::KMS::Alias` resource — `AliasName: !Sub "alias/taskmanager-aurora-${BootstrapScope}"`, `TargetKeyId: !If [IsPrimary, !Ref AuroraKmsKey, !Ref AuroraKmsKeyReplica]`. No condition — every deploy creates the alias.
- [ ] 2.11 Add `AWS::SSM::Parameter` resource at name `!Sub "/taskmanager/kms/${BootstrapScope}/aurora-key-arn"`, type `String`, value the key/replica ARN via the same `!If` as 2.10, `DeletionPolicy: Retain`.
- [ ] 2.12 Add `GitHubActionsUser` IAM user + `AWS::IAM::AccessKey` + `DeploymentPolicy` IAM policy — **gated on `IsNonprod`**. Policy mirrors the legacy `bootstrap.template`'s `DeploymentPolicy` shape (the broad CloudFormation/Lambda/ECS/RDS/ECR/IAM etc. policy).
- [ ] 2.13 Add `GitHubActionsUserProd` IAM user + `AWS::IAM::AccessKey` + `DeploymentPolicy-prod` IAM policy — **gated on `IsProd`**. `DeploymentPolicy-prod` mirrors `DeploymentPolicy` shape but scopes IAM/KMS/Secrets statements to resources used by the `app` env stack only (e.g. `kms:` statements limited to `Resource: !GetAtt AuroraKmsKey.Arn` rather than `*`).
- [ ] 2.14 Add `ProdKmsAdminRole` IAM role (`RoleName: prod-kms-admin`) — **gated on `IsProd`**. `AssumeRolePolicyDocument` allows only the account root principal (`AWS: !Sub "arn:aws:iam::${AWS::AccountId}:root"`). Inline policy grants `kms:*` on the key itself; no other permissions.
- [ ] 2.15 Add stack outputs:
  - When `IsNonprod` and `IsPrimary`: `GitHubActionsUserAccessKeyId` (`!Ref GitHubActionsAccessKey`), `GitHubActionsUserSecretAccessKey` (`!GetAtt GitHubActionsAccessKey.SecretAccessKey`, `NoEcho: true`), `GitHubActionsUserArn`.
  - When `IsProd` and `IsPrimary`: `GitHubActionsUserProdAccessKeyId`, `GitHubActionsUserProdSecretAccessKey` (`NoEcho: true`), `GitHubActionsUserProdArn`, `ProdKmsAdminRoleArn`.
  - Always: `AuroraKmsKeyArn` (the `!If` value).
- [ ] 2.16 Run `aws cloudformation validate-template --template-body file://infrastructure/bootstrap-shared.template --region us-east-1` and `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template --region us-east-1`. Both must succeed.

## 3. Phase 1b — Teardown + initial deploy (manual, human admin, deploy quiet window)

This phase removes the legacy `bootstrap` stack and stands up the three new stacks. Schedule it for a quiet window: no in-flight PRs, no pending merges. While the old `bootstrap` is gone and `bootstrap-shared` is not yet up, the `TemplatesBucketPolicy` is absent and any concurrent CI deploy will fail.

- [ ] 3.1 Confirm no active CI runs and no merges queued. Pause any auto-merge bots.
- [ ] 3.2 Delete the legacy bootstrap stack: `aws cloudformation delete-stack --stack-name bootstrap --region us-east-1`; wait for `DELETE_COMPLETE`. Same for `--region us-west-2`.
- [ ] 3.3 Human admin from 1.1 deploys `bootstrap-shared` to `us-east-1`: `aws cloudformation deploy --stack-name bootstrap-shared --template-file infrastructure/bootstrap-shared.template --parameter-overrides TemplatesBucketName=cf-templates-991795635857-us-east-1 --capabilities CAPABILITY_NAMED_IAM --region us-east-1`.
- [ ] 3.4 Same operator deploys `bootstrap-shared` to `us-west-2` with the matching `TemplatesBucketName=cf-templates-991795635857-us-west-2`.
- [ ] 3.5 Same operator deploys `bootstrap-nonprod` to `us-east-1`: `--parameter-overrides BootstrapScope=nonprod IsPrimaryRegion=true`. Captures `GitHubActionsUserAccessKeyId` + `GitHubActionsUserSecretAccessKey` from stack outputs.
- [ ] 3.6 Same operator deploys `bootstrap-nonprod` to `us-west-2`: `--parameter-overrides BootstrapScope=nonprod IsPrimaryRegion=false PrimaryKeyArn=<AuroraKmsKeyArn from 3.5 output>`.
- [ ] 3.7 Same operator deploys `bootstrap-prod` to `us-east-1`: `--parameter-overrides BootstrapScope=prod IsPrimaryRegion=true`. Captures `GitHubActionsUserProdAccessKeyId` + `GitHubActionsUserProdSecretAccessKey` from stack outputs.
- [ ] 3.8 Same operator deploys `bootstrap-prod` to `us-west-2`: `--parameter-overrides BootstrapScope=prod IsPrimaryRegion=false PrimaryKeyArn=<AuroraKmsKeyArn from 3.7 output>`.
- [ ] 3.9 **Rotate `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub repository secrets** using the values from 3.5. The legacy values are now invalid.
- [ ] 3.10 **Add `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` GitHub repository secrets** using the values from 3.7.
- [ ] 3.11 Verify SSM parameters in both regions: `aws ssm get-parameter --name /taskmanager/kms/nonprod/aurora-key-arn --region us-east-1` and `--region us-west-2`; same for `/taskmanager/kms/prod/aurora-key-arn`. All four must return valid ARNs.
- [ ] 3.12 Verify key policies: try `aws kms schedule-key-deletion --key-id <prod-key-arn>` using the new `GitHubActionsUserProd` credentials — confirm `AccessDeniedException`. Try `aws kms describe-key --key-id <prod-key-arn>` using the new non-prod `GitHubActionsUser` credentials — confirm `AccessDeniedException`.
- [ ] 3.13 Quiet window closes. Resume any paused auto-merge bots.

## 4. Phase 2 — Wire workflows and templates

- [ ] 4.1 Add "Select AWS credentials" step in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s `deploy` job before `Configure AWS credentials`: branch-conditional output of `access-key-id` / `secret-access-key` per D4 in design.md.
- [ ] 4.2 Add explicit guard: if branch is `app` and `secrets.AWS_ACCESS_KEY_ID_PROD == ''`, `exit 1` with a clear message.
- [ ] 4.3 Change `Configure AWS credentials` step to consume `steps.select-creds.outputs.*` instead of `secrets.AWS_*` directly.
- [ ] 4.4 Add "Lookup bootstrap KMS key from SSM" step that runs after `Get AWS Account ID` and before `Set parameter overrides`: `aws ssm get-parameter --name /taskmanager/kms/${SCOPE}/aurora-key-arn --region ${{ matrix.region }} --query Parameter.Value --output text` where `SCOPE=prod` iff `BRANCH_NAME == app` else `nonprod`. Export as `BOOTSTRAP_KMS_KEY_ARN` env var.
- [ ] 4.5 Update `Set parameter overrides` step: `KmsKeyArn` parameter now sources from `BOOTSTRAP_KMS_KEY_ARN`. Remove the `KmsKeyArn=${{ steps.get-primary-outputs.outputs.kms-key-arn }}` line. Remove the `kms-key-arn` line from `Get primary region outputs for secondary` step.
- [ ] 4.6 Update [infrastructure/backend.template](../../../infrastructure/backend.template): pass `KmsKeyArn` directly to `DbStack` (`AuroraKmsKeyArn: !Ref KmsKeyArn`) instead of `!GetAtt SecurityStack.Outputs.AuroraKmsKeyArn`. Remove `DependsOn: SecurityStack` from `DbStack`. Remove `KmsKeyArn` pass-through to `SecurityStack` (security.template no longer takes it).
- [ ] 4.7 Update [infrastructure/master.template](../../../infrastructure/master.template): `KmsKeyArn` parameter is required (remove `Default: ""`); remove `HasKmsKeyArn` condition and `!If [HasKmsKeyArn, ..., !Ref "AWS::NoValue"]` pass-throughs.
- [ ] 4.8 Update [infrastructure/security.template](../../../infrastructure/security.template): remove `KmsKeyArn` parameter, `IsPrimaryKey`/`IsReplicaKey` conditions, and `AuroraKmsKeyArn` output's `Export`. **Leave the `AuroraKmsKey` / `AuroraKmsKeyReplica` resources in place for now** so existing stacks can be updated without dropping the key (Phase 5 task 7.2 removes them).
- [ ] 4.9 Update [infrastructure/application.template](../../../infrastructure/application.template) to accept `KmsKeyArn` parameter directly (from workflow `BOOTSTRAP_KMS_KEY_ARN`) instead of importing `AuroraKmsKeyArn-dev` or similar. Audit for any `Fn::ImportValue` referencing `AuroraKmsKeyArn-*` and remove.
- [ ] 4.10 Run `sam validate --template-file infrastructure/master.template`, `sam validate --template-file infrastructure/application.template`, `sam validate --template-file infrastructure/bootstrap.template`, `sam validate --template-file infrastructure/bootstrap-shared.template` locally.
- [ ] 4.11 Push to a throwaway feature branch; verify deploy succeeds using the new non-prod CI credentials; verify the resulting Aurora cluster's `KmsKeyId` equals the nonprod SSM ARN; verify the deployed app responds at its per-branch URL. (Expect a one-time full Docker rebuild because the ECR repo was recreated in 3.3/3.4.)

## 5. Phase 3 — Non-prod env migration

- [ ] 5.1 Delete the `dev` env stack: `aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`; wait for `DELETE_COMPLETE` (~15 min). No snapshot needed (resolved: drop `dev` data).
- [ ] 5.2 Trigger CI on `dev` branch (empty push or workflow re-run); verify the new cluster comes up healthy and its `KmsKeyId` matches the nonprod SSM parameter value via `aws rds describe-db-clusters --query 'DBClusters[?DBClusterIdentifier==`<id>`].KmsKeyId'`.
- [ ] 5.3 Snapshot `alpha` Aurora cluster (both regions): `aws rds create-db-cluster-snapshot --db-cluster-identifier <id> --db-cluster-snapshot-identifier pre-kms-alpha-<region>-<YYYY-MM-DD>` in `us-east-1` and `us-west-2`.
- [ ] 5.4 Delete `alpha` env stacks: `us-west-2` first (Aurora Global Cluster requires this order), then `us-east-1`. Wait for both `DELETE_COMPLETE`.
- [ ] 5.5 Trigger CI on `alpha`; new cluster comes up empty on the nonprod key. Manually restore from snapshot: `aws rds restore-db-cluster-from-snapshot --db-cluster-identifier <new-id> --snapshot-identifier <from-5.3> --engine aurora-mysql --kms-key-id <nonprod-arn>`. Then re-point the env stack at the restored cluster (document the adopt mechanism — likely a brief downtime to update the cluster reference, or restore-then-import).
- [ ] 5.6 Repeat 5.3–5.5 for `beta`.
- [ ] 5.7 Trigger redeploys on two representative feature branches; verify they pick up the nonprod key via the new application.template / workflow lookup.
- [ ] 5.8 Smoke-test each migrated env via its deployed URL (`https://dev.appcloud.systems`, `https://alpha.appcloud.systems`, `https://beta.appcloud.systems`).

## 6. Phase 4 — Prod migration (`app`)

- [ ] 6.1 Schedule a maintenance window with stakeholders; communicate the planned downtime envelope.
- [ ] 6.2 Snapshot `app` Aurora cluster in `us-east-1` and `us-west-2`: `pre-kms-app-<region>-<YYYY-MM-DD>`.
- [ ] 6.3 Restore the latest `us-east-1` snapshot into a parallel Aurora cluster named `taskmanager-app-newkey` encrypted with the `bootstrap-prod` key (out-of-band, not via CloudFormation initially). Keep this cluster running.
- [ ] 6.4 During the maintenance window: drain web traffic (scale ECS service to 0 or put up a maintenance page).
- [ ] 6.5 Final delta-sync writes from old cluster to new cluster (or stop writes and re-snapshot+restore).
- [ ] 6.6 Update `taskmanager/database/app/regional/us-east-1/password` Secrets Manager secret to point at the new cluster endpoint.
- [ ] 6.7 Roll the ECS service; verify the app comes up healthy reading/writing the new cluster.
- [ ] 6.8 Run an `app`-branch CloudFormation deploy that adopts the new cluster (likely via `--import-existing-resources` or a manual stack import). Document the exact adopt strategy used here.
- [ ] 6.9 Verify global cluster membership: the new `us-east-1` cluster is the primary; the existing `us-west-2` cluster is still the secondary on the OLD prod KMS key — that's tolerated for now (the sibling region change rebuilds it).
- [ ] 6.10 Decommission the old `us-east-1` cluster only after a soak period (≥24h).

## 7. Phase 5 — Cleanup

- [ ] 7.1 For each ARN in `openspec/changes/centralize-aurora-kms-keys/orphaned-keys.txt`, run `aws kms schedule-key-deletion --key-id <arn> --pending-window-in-days 7 --region <region>`. Skip any key still referenced by a live Aurora cluster (re-check via `aws rds describe-db-clusters`).
- [ ] 7.2 Final commit on [infrastructure/security.template](../../../infrastructure/security.template): remove `AuroraKmsKey`, `AuroraKmsKeyReplica`, and the `AuroraKmsKeyArn` output. Keep `SharedLambdaExecutionRole`, `BranchName` parameter, and `SharedLambdaRoleArn` output.
- [ ] 7.3 Re-deploy each env stack (`dev`, `alpha`, `beta`, `app`) so the now-empty KMS portion of security.template is reflected. Verify CloudFormation drift-detection shows no diffs against the new template.
- [ ] 7.4 Rotate both new CI access keys once (security hygiene; the secret values were visible in stack outputs during Phase 1b/3.5/3.7):
  - `aws iam create-access-key --user-name GitHubActionsUser` → update `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` → delete old key.
  - `aws iam create-access-key --user-name GitHubActionsUserProd` → update `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` → delete old key.
  - Trigger a deploy on a feature branch (verifies non-prod key) and on `app` (verifies prod key).

## 8. Validation

- [ ] 8.1 Run `openspec validate centralize-aurora-kms-keys --strict` and resolve any issues.
- [ ] 8.2 Manually verify each `## Requirement` scenario from `specs/aurora-kms-key-management/spec.md` against the deployed system:
  - Two new KMS keys exist per region; aliases match.
  - Per-branch env stacks own zero KMS resources.
  - `GitHubActionsUserProd` exists in `bootstrap-prod`; `GitHubActionsUser` exists in `bootstrap-nonprod`; legacy `bootstrap` stack no longer exists in either region.
  - `GitHubActionsUserProd` cannot `ScheduleKeyDeletion` on prod key; `GitHubActionsUser` cannot even `DescribeKey` against prod key.
  - SSM parameters resolve to current ARNs in both regions.
  - Workflow runs on `app` use `*_PROD` secrets; runs on other branches use the rotated non-prod secrets.
- [ ] 8.3 Run UI tests against `app` to confirm end-to-end health.
- [ ] 8.4 Archive this change per the experimental workflow (`/opsx:archive`).
