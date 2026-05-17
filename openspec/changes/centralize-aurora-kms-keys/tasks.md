## 1. Phase 0 — Prereqs

- [ ] 1.1 Identify the existing human admin IAM user (or SSO role) that will deploy `bootstrap-prod` for the first time. Confirm that user/role has AdministratorAccess or equivalent CloudFormation+IAM+KMS create permissions.
- [ ] 1.2 Inventory existing orphaned `AuroraKmsKey` resources in `us-east-1` and `us-west-2` (description matches `Multi-region KMS key for Aurora Global Database encryption - *`); save the list to `openspec/changes/centralize-aurora-kms-keys/orphaned-keys.txt` for the Phase 5 sweep.

## 2. Phase 1 — Bootstrap template changes

- [ ] 2.1 Add `BootstrapScope` parameter (`AllowedValues: ["", "prod", "nonprod"]`, `Default: ""`) to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template), plus `IsProdScope` / `IsNonprodScope` / `IsUnscoped` `Conditions`. Existing resources (the original `bootstrap` stack contents) are gated on `IsUnscoped` so they only materialize in the unscoped stack instance.
- [ ] 2.2 Add `IsPrimaryRegion` parameter and `IsPrimaryRegionCondition` to bootstrap.template (mirror existing primary/replica split in security.template).
- [ ] 2.3 Add `AuroraKmsKey` resource (multi-region, `DeletionPolicy: Retain`) gated on `!And [IsPrimaryRegion, !Not [IsUnscoped]]`.
- [ ] 2.4 Add `AuroraKmsKeyReplica` resource gated on `!And [!Not [IsPrimaryRegion], !Not [IsUnscoped]]`, with `PrimaryKeyArn` parameter (passed in by the workflow/operator for the secondary-region deploy).
- [ ] 2.5 Add scoped `KeyPolicy`: `IsProdScope` → restricted policy (no destructive ops for `GitHubActionsUserProd`; only `prod-kms-admin` role + account root get destructive ops; non-prod CI user not listed at all); `IsNonprodScope` → broad policy granted to existing non-prod CI user.
- [ ] 2.6 Add `prod-kms-admin` IAM role (conditional on `IsProdScope`) with `AssumeRolePolicyDocument` allowing only `AWS::AccountId:root`.
- [ ] 2.7 Add `GitHubActionsUserProd` IAM user + `AWS::IAM::AccessKey` + `DeploymentPolicy-prod` policy (conditional on `IsProdScope`). Scope `DeploymentPolicy-prod` to mirror the shape of existing `DeploymentPolicy` but limit IAM/KMS/Secrets statements to resources used by the `app` stack.
- [ ] 2.8 Add `AWS::KMS::Alias` resource — `alias/taskmanager-aurora-${BootstrapScope}` — pointing at the key or replica (conditional on scope).
- [ ] 2.9 Add `AWS::SSM::Parameter` resource at path `/taskmanager/kms/${BootstrapScope}/aurora-key-arn` with `DeletionPolicy: Retain`; value is the key ARN. Conditional on `!IsUnscoped`.
- [ ] 2.10 Add stack outputs: `GitHubActionsUserProdAccessKeyId`, `GitHubActionsUserProdSecretAccessKey` (with `NoEcho` documented via stack policy; the secret value will appear in stack outputs once for the operator to copy).
- [ ] 2.11 Run `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template`; resolve any issues.
- [ ] 2.12 Verify the unscoped `bootstrap` stack still works: do a no-op `aws cloudformation deploy --stack-name bootstrap ... --no-execute-changeset` in `us-east-1` and inspect the changeset for surprises.

## 3. Phase 1b — Initial bootstrap-prod / bootstrap-nonprod deploy (manual)

- [ ] 3.1 The human admin from 1.1 runs `aws cloudformation deploy --stack-name bootstrap-nonprod --template-file infrastructure/bootstrap.template --parameter-overrides BootstrapScope=nonprod IsPrimaryRegion=true TemplatesBucketName=cf-templates-<account>-us-east-1 --capabilities CAPABILITY_NAMED_IAM --region us-east-1`.
- [ ] 3.2 Same operator deploys `bootstrap-nonprod` to `us-west-2` (current secondary), passing `IsPrimaryRegion=false PrimaryKeyArn=<from 3.1 output>`.
- [ ] 3.3 Same operator deploys `bootstrap-prod` in `us-east-1` with `BootstrapScope=prod IsPrimaryRegion=true`. Copies `GitHubActionsUserProdAccessKeyId` and `GitHubActionsUserProdSecretAccessKey` from the stack outputs.
- [ ] 3.4 Same operator deploys `bootstrap-prod` in `us-west-2` with `IsPrimaryRegion=false PrimaryKeyArn=<from 3.3 output>`.
- [ ] 3.5 Add `AWS_ACCESS_KEY_ID_PROD` and `AWS_SECRET_ACCESS_KEY_PROD` to GitHub repository secrets, using the values from 3.3.
- [ ] 3.6 Verify SSM parameters in both regions: `aws ssm get-parameter --name /taskmanager/kms/nonprod/aurora-key-arn --region us-east-1` (and `-prod`, and `--region us-west-2`).
- [ ] 3.7 Verify key policies: try `aws kms schedule-key-deletion --key-id <prod-key-arn>` using the new `GitHubActionsUserProd` credentials — confirm `AccessDeniedException`. Try `aws kms describe-key --key-id <prod-key-arn>` using the existing non-prod CI credentials — confirm `AccessDeniedException`.

## 4. Phase 2 — Wire workflows and templates

- [ ] 4.1 Add "Select AWS credentials" step in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s `deploy` job before `Configure AWS credentials`: branch-conditional output of `access-key-id` / `secret-access-key` per D4 in design.md.
- [ ] 4.2 Add explicit guard: if branch is `app` and `secrets.AWS_ACCESS_KEY_ID_PROD == ''`, `exit 1` with a clear message.
- [ ] 4.3 Change `Configure AWS credentials` step to consume `steps.select-creds.outputs.*` instead of `secrets.AWS_*` directly.
- [ ] 4.4 Add "Lookup bootstrap KMS key from SSM" step that runs after `Get AWS Account ID` and before `Set parameter overrides`: `aws ssm get-parameter --name /taskmanager/kms/${SCOPE}/aurora-key-arn --region ${{ matrix.region }} --query Parameter.Value --output text` where `SCOPE=prod` iff `BRANCH_NAME == app` else `nonprod`. Export as `BOOTSTRAP_KMS_KEY_ARN` env var for downstream steps.
- [ ] 4.5 Update `Set parameter overrides` step: `KmsKeyArn` parameter now sources from `BOOTSTRAP_KMS_KEY_ARN`. Remove the `KmsKeyArn=${{ steps.get-primary-outputs.outputs.kms-key-arn }}` line. Remove the `kms-key-arn` line from `Get primary region outputs for secondary` step (it no longer needs to do that lookup).
- [ ] 4.6 Update [infrastructure/backend.template](../../../infrastructure/backend.template): pass `KmsKeyArn` directly to `DbStack` (`AuroraKmsKeyArn: !Ref KmsKeyArn`) instead of `!GetAtt SecurityStack.Outputs.AuroraKmsKeyArn`. Remove `DependsOn: SecurityStack` from `DbStack`. Remove `KmsKeyArn` pass-through to `SecurityStack` (security.template no longer takes it).
- [ ] 4.7 Update [infrastructure/master.template](../../../infrastructure/master.template): `KmsKeyArn` parameter is required (remove `Default: ""`); remove `HasKmsKeyArn` condition and `!If [HasKmsKeyArn, ..., !Ref "AWS::NoValue"]` pass-throughs.
- [ ] 4.8 Update [infrastructure/security.template](../../../infrastructure/security.template): remove `KmsKeyArn` parameter, `IsPrimaryKey`/`IsReplicaKey` conditions, and `AuroraKmsKeyArn` output's `Export`. **Leave the `AuroraKmsKey` / `AuroraKmsKeyReplica` resources in place for now** so existing stacks can be updated without dropping the key (Phase 5 task 7.2 removes them).
- [ ] 4.9 Update [infrastructure/application.template](../../../infrastructure/application.template) to accept `KmsKeyArn` parameter directly (from workflow `BOOTSTRAP_KMS_KEY_ARN`) instead of importing `AuroraKmsKeyArn-dev` or similar. Audit for any `Fn::ImportValue` referencing `AuroraKmsKeyArn-*` and remove.
- [ ] 4.10 Run `sam validate --template-file infrastructure/master.template`, `sam validate --template-file infrastructure/application.template`, `sam validate --template-file infrastructure/bootstrap.template` locally.
- [ ] 4.11 Push to a throwaway feature branch; verify deploy succeeds using the non-prod CI user; verify the resulting Aurora cluster's `KmsKeyId` equals the nonprod SSM ARN; verify the deployed app responds at its per-branch URL.

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
- [ ] 7.4 Rotate the `GitHubActionsUserProd` access key once (the secret value was visible in the stack output during Phase 1b/3.3): create a new access key via `aws iam create-access-key --user-name GitHubActionsUserProd`, update `AWS_*_PROD` GitHub secrets, then `aws iam delete-access-key` for the old one. Trigger an `app` deploy to verify the new key works.

## 8. Validation

- [ ] 8.1 Run `openspec validate centralize-aurora-kms-keys --strict` and resolve any issues.
- [ ] 8.2 Manually verify each `## Requirement` scenario from `specs/aurora-kms-key-management/spec.md` against the deployed system:
  - Two new KMS keys exist per region; aliases match.
  - Per-branch env stacks own zero KMS resources.
  - `GitHubActionsUserProd` exists; non-prod CI cannot deploy `app` stack.
  - `GitHubActionsUserProd` cannot `ScheduleKeyDeletion` on prod key; non-prod CI cannot even `DescribeKey` against prod key.
  - SSM parameters resolve to current ARNs in both regions.
  - Workflow runs on `app` use `*_PROD` secrets; runs on other branches use the existing secrets.
- [ ] 8.3 Run UI tests against `app` to confirm end-to-end health.
- [ ] 8.4 Archive this change per the experimental workflow (`/opsx:archive`).
