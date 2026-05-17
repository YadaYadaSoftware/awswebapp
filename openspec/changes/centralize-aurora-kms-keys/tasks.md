## 1. Phase 0 — Prereqs

- [ ] 1.1 Identify the existing human admin IAM user (or SSO role) that will deploy the new consolidated `bootstrap` stack. Confirm that user/role has AdministratorAccess or equivalent CloudFormation+IAM+KMS create permissions. Capture its ARN — it will be passed as `KeyAdminPrincipalArn` during Phase 1 deploys.
- [ ] 1.2 Inventory existing orphaned `AuroraKmsKey` resources in `us-east-1` and `us-west-2` (description matches `Multi-region KMS key for Aurora Global Database encryption - *`); save the list to `openspec/changes/centralize-aurora-kms-keys/orphaned-keys.txt` for the Phase 5 sweep.
- [ ] 1.3 Snapshot the legacy `bootstrap` stack's current state for rollback reference: `aws cloudformation get-template --stack-name bootstrap --region us-east-1 > openspec/changes/centralize-aurora-kms-keys/legacy-bootstrap-template.yaml` (and same for `us-west-2`). Note the existing values of `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub secrets — they will be invalidated by this change.
- [ ] 1.4 Inventory any leftover `bootstrap-prod` / `bootstrap-nonprod` stacks from earlier iteration drafts (these were used in an intermediate three-stack-per-region design that was later collapsed to one stack). They'll be deleted in Phase 1.

## 2. Phase 1 — Template work (consolidated rewrite)

- [x] 2.1 Rewrite [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) as a single consolidated template owning everything (shared infra + both KMS keys + both IAM users + prod-kms-admin role). The legacy `bootstrap.template` content, the prior `bootstrap-shared.template`, and the prior per-scope `bootstrap.template` are all collapsed into this one file.
- [x] 2.2 Add parameters: `TemplatesBucketName` (required); `RetentionDays` (Default: 7); `PrimaryNonprodKeyArn` (Default: ""); `PrimaryProdKeyArn` (Default: ""); `KeyAdminPrincipalArn` (Default: ""). No `BootstrapScope`, no per-scope conditions.
- [x] 2.3 Add conditions: `IsPrimary` (`!Equals [!Ref PrimaryNonprodKeyArn, ""]`); `IsReplica` (opposite); `HasKeyAdmin`. The IsProd/IsNonprod scope conditions from the earlier draft are deliberately gone — both scopes' resources coexist unconditionally (subject only to IsPrimary for IAM resources, since IAM is global and IAM users would conflict if created in both regions).
- [x] 2.4 Add the shared infrastructure resources (always created): `WebECRRepository` (with explicit `RepositoryName: !Sub "ecr-${AWS::AccountId}-${AWS::Region}"`), `TemplatesBucketPolicy` (with hardcoded ARNs for both IAM users), `ApiGatewayCloudWatchLogsRole`, `ApiGatewayAccount`, `LogRetentionConfigFunction` (python3.12), `ConfigRuleRole`, `LogRetentionConfigRule`, `LogRetentionRemediationDocument`, `SSMAutomationRole`, `LogRetentionRemediation`, `ConfigInvokeLambdaPermission`.
- [x] 2.5 Add the nonprod KMS resources: `AuroraKmsKeyNonprod` (Condition: IsPrimary, `MultiRegion: true`) and `AuroraKmsKeyNonprodReplica` (Condition: IsReplica, `PrimaryKeyArn: !Ref PrimaryNonprodKeyArn`). Both with the nonprod KeyPolicy (broad — root + RDS service + `GitHubActionsUser`). Primary uses `!GetAtt GitHubActionsUser.Arn` for proper CFN dependency ordering; replica uses `!Sub` (user doesn't exist in this stack).
- [x] 2.6 Add the prod KMS resources: `AuroraKmsKeyProd` (Condition: IsPrimary) and `AuroraKmsKeyProdReplica` (Condition: IsReplica). Both with the prod KeyPolicy: root + RDS service + `GitHubActionsUserProd` + `ProdKmsAdminRole` Allow statements, **plus** a final `Effect: Deny` with `NotPrincipal` listing those four (with optional `KeyAdminPrincipalArn` exemption via `!If [HasKeyAdmin, ...]`).
- [x] 2.7 Add aliases (always created): `AuroraKmsKeyNonprodAlias` (`alias/taskmanager-aurora-nonprod`) and `AuroraKmsKeyProdAlias` (`alias/taskmanager-aurora-prod`). Each uses `!If [IsPrimary, !Ref AuroraKmsKey..., !Ref AuroraKmsKey...Replica]` for the target.
- [x] 2.8 Add SSM parameters (always created): `AuroraKmsKeyNonprodArnParameter` at `/taskmanager/kms/nonprod/aurora-key-arn` and `AuroraKmsKeyProdArnParameter` at `/taskmanager/kms/prod/aurora-key-arn`. No `DeletionPolicy: Retain` (failed deploys would orphan and block retries).
- [x] 2.9 Add IAM resources (Condition: IsPrimary, primary region only since IAM is global): `GitHubActionsUser` (UserName: `GitHubActionsUser`) + `GitHubActionsAccessKey` + `DeploymentPolicy` (broad); `GitHubActionsUserProd` (UserName: `GitHubActionsUserProd`) + `GitHubActionsAccessKeyProd` + `DeploymentPolicyProd` (broad but KMS-restricted to prod key only, excluding destructive ops); `ProdKmsAdminRole` (RoleName: `prod-kms-admin`, AssumeRolePolicyDocument allows only AWS::AccountId:root, inline policy grants `kms:*` on `Resource: "*"`).
- [x] 2.10 Add outputs: `AuroraKmsKeyNonprodArn`, `AuroraKmsKeyProdArn`, `AuroraKmsKeyNonprodAliasName`, `AuroraKmsKeyProdAliasName`, `TemplatesBucketName`, `WebECRRepositoryName`, `WebECRRepositoryUri`, `ApiGatewayCloudWatchLogsRoleArn` (shared infra outputs use the same `Export.Name` values as the legacy `bootstrap` stack so any downstream `Fn::ImportValue` keeps resolving); and (IsPrimary only) `GitHubActionsUserAccessKeyId`, `GitHubActionsUserSecretAccessKey`, `GitHubActionsUserArn`, `GitHubActionsUserProdAccessKeyId`, `GitHubActionsUserProdSecretAccessKey`, `GitHubActionsUserProdArn`, `ProdKmsAdminRoleArn`. CFN outputs do not support `NoEcho`; operator copies secrets once into GitHub secrets, then rotates (Phase 5 task) to invalidate the visible value.
- [x] 2.11 Run `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template --region us-east-1`. Succeeds.

## 3. Phase 1b — Teardown + initial deploy (manual, human admin, deploy quiet window)

This phase tears down the legacy `bootstrap` stack (and any leftover `bootstrap-prod`/`bootstrap-nonprod` from earlier iteration) and stands up the new consolidated `bootstrap` stack in each region. Schedule it for a quiet window: no in-flight PRs, no pending merges. While the old bootstrap is gone and the new one is not yet up, the `TemplatesBucketPolicy` and `ApiGatewayAccount` are absent and any concurrent CI deploy will fail.

- [ ] 3.1 Confirm no active CI runs and no merges queued. Pause any auto-merge bots.
- [ ] 3.2 Delete any leftover scoped bootstraps from earlier iteration drafts (per Phase 0 task 1.4): `aws cloudformation delete-stack --stack-name bootstrap-prod --region us-east-1`; same for `bootstrap-nonprod` and for `--region us-west-2`. Wait for all `DELETE_COMPLETE`. (Skip this if those stacks were never deployed.)
- [ ] 3.3 Delete the legacy `bootstrap` stack: `aws cloudformation delete-stack --stack-name bootstrap --region us-east-1`; wait for `DELETE_COMPLETE`. Same for `--region us-west-2`.
- [ ] 3.4 Human admin from 1.1 deploys the consolidated `bootstrap` to `us-east-1` (primary):
   ```
   aws cloudformation deploy --stack-name bootstrap \
     --template-file infrastructure/bootstrap.template \
     --parameter-overrides \
       TemplatesBucketName=cf-templates-<account>-us-east-1 \
       KeyAdminPrincipalArn=<deployer-arn-from-1.1> \
     --capabilities CAPABILITY_NAMED_IAM \
     --region us-east-1
   ```
   `PrimaryNonprodKeyArn` and `PrimaryProdKeyArn` are omitted (default empty), which triggers `IsPrimary=true`. Captures `GitHubActionsUserAccessKeyId`, `GitHubActionsUserSecretAccessKey`, `GitHubActionsUserProdAccessKeyId`, `GitHubActionsUserProdSecretAccessKey`, `AuroraKmsKeyNonprodArn`, `AuroraKmsKeyProdArn` from stack outputs.
- [ ] 3.5 Same operator deploys the consolidated `bootstrap` to `us-west-2` (replica):
   ```
   aws cloudformation deploy --stack-name bootstrap \
     --template-file infrastructure/bootstrap.template \
     --parameter-overrides \
       TemplatesBucketName=cf-templates-<account>-us-west-2 \
       PrimaryNonprodKeyArn=<AuroraKmsKeyNonprodArn from 3.4> \
       PrimaryProdKeyArn=<AuroraKmsKeyProdArn from 3.4> \
       KeyAdminPrincipalArn=<deployer-arn-from-1.1> \
     --capabilities CAPABILITY_NAMED_IAM \
     --region us-west-2
   ```
   Replica deploy creates KMS replicas + aliases + SSM parameters + the per-region shared infra, but no IAM resources (gated on IsPrimary).
- [ ] 3.6 Same operator deploys `bootstrap-nonprod` to `us-west-2`: `--parameter-overrides PrimaryKeyArn=<AuroraKmsKeyArn from 3.5 output>`.
- [ ] 3.7 Same operator deploys `bootstrap-prod` to `us-east-1`: `aws cloudformation deploy --stack-name bootstrap-prod --template-file infrastructure/bootstrap.template --parameter-overrides KeyAdminPrincipalArn=<arn-of-the-deploying-IAM-user> --capabilities CAPABILITY_NAMED_IAM --region us-east-1`. The `KeyAdminPrincipalArn` is required during initial setup because the prod key policy's NotPrincipal+Deny would otherwise lock the deployer out of `kms:PutKeyPolicy` on the next update (KMS rejects policy updates that would lock out the calling principal). Once `prod-kms-admin` can be assumed by the deployer, omit this parameter and run deploys from the assumed role instead. Captures `GitHubActionsUserProdAccessKeyId` + `GitHubActionsUserProdSecretAccessKey` from stack outputs.
- [ ] 3.8 Same operator deploys `bootstrap-prod` to `us-west-2`: `--parameter-overrides PrimaryKeyArn=<AuroraKmsKeyArn from 3.7 output>`.
- [ ] 3.9 **Rotate `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub repository secrets** using the values from 3.5. The legacy values are now invalid.
- [ ] 3.10 **Add `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` GitHub repository secrets** using the values from 3.7.
- [ ] 3.11 Verify SSM parameters in both regions: `aws ssm get-parameter --name /taskmanager/kms/nonprod/aurora-key-arn --region us-east-1` and `--region us-west-2`; same for `/taskmanager/kms/prod/aurora-key-arn`. All four must return valid ARNs.
- [ ] 3.12 Verify key policies: try `aws kms schedule-key-deletion --key-id <prod-key-arn>` using the new `GitHubActionsUserProd` credentials — confirm `AccessDeniedException`. Try `aws kms describe-key --key-id <prod-key-arn>` using the new non-prod `GitHubActionsUser` credentials — confirm `AccessDeniedException`.
- [ ] 3.13 Quiet window closes. Resume any paused auto-merge bots.

## 4. Phase 2 — Wire workflows and templates

- [x] 4.1 Add "Select AWS credentials by branch" step in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s `deploy` job before `Configure AWS credentials`: branch-conditional output of `access-key-id` / `secret-access-key` per D4 in design.md.
- [x] 4.2 Built-in guard: if branch is `app` and `AWS_ACCESS_KEY_ID_PROD` is empty, the select-creds step's bash exits non-zero with a clear error message before any AWS call is made.
- [x] 4.3 Changed `Configure AWS credentials` step to consume `steps.select-creds.outputs.*` instead of `secrets.AWS_*` directly. Also removed the redundant `env: AWS_ACCESS_KEY_ID/_SECRET_ACCESS_KEY` blocks on the `Check if Web Container Image Exists` and `Build and Push Web Container Image` steps — `configure-aws-credentials` already exports those as job-wide env, the explicit overrides were duplicating and would have used the wrong (nonprod) creds for `app` deploys.
- [x] 4.4 Added "Lookup bootstrap KMS key from SSM" step. Runs for all branches (not just master-template) since the workflow needs to handle both paths uniformly; the value is just unused for feature branches. Exports as `BOOTSTRAP_KMS_KEY_ARN` env var via `$GITHUB_ENV`. Fails loudly if the SSM parameter is empty or missing (so bootstrap-{prod,nonprod} not being deployed surfaces clearly).
- [x] 4.5 Updated `Set parameter overrides`: `KmsKeyArn=${BOOTSTRAP_KMS_KEY_ARN}` (sources from env var, not from `get-primary-outputs`). Removed the `kms-key-arn` line from `Get primary region outputs for secondary` step (no longer needed).
- [x] 4.6 [infrastructure/backend.template](../../../infrastructure/backend.template): `DbStack` now uses `AuroraKmsKeyArn: !Ref KmsKeyArn`. Removed `SecurityStack` from `DbStack.DependsOn`. Removed `KmsKeyArn` pass-through to `SecurityStack`. Removed `HasKmsKeyArn` condition. `KmsKeyArn` parameter description updated and `Default: ""` removed. The `SecurityStack` resource and its KMS-related resources stay in place (Phase 5 removes them).
- [x] 4.7 [infrastructure/master.template](../../../infrastructure/master.template): `KmsKeyArn` parameter is now required (no `Default: ""`); `HasKmsKeyArn` condition removed. The pass-through to `BackendStack` (`KmsKeyArn: !Ref KmsKeyArn`) is unconditional.
- [x] 4.8 [infrastructure/security.template](../../../infrastructure/security.template): minimal change — removed only the `Export.Name` on the `AuroraKmsKeyArn` output, breaking any downstream `Fn::ImportValue`. Kept `KmsKeyArn` parameter and `IsPrimaryKey`/`IsReplicaKey` conditions because the existing `AuroraKmsKey`/`AuroraKmsKeyReplica` resources still reference them. Phase 5 task 7.2 will remove the resources and the supporting parameter/conditions together. (The earlier task plan said to remove the parameter and conditions during Section 4, but that would break the template because the still-present Replica resource depends on them; deferred to Phase 5.)
- [x] 4.9 [infrastructure/application.template](../../../infrastructure/application.template) — **no work needed.** A grep audit found zero KMS references in this template (no `KmsKeyArn` parameter, no `Fn::ImportValue` for `AuroraKmsKeyArn-*`). Feature branches deploy application.template and consume `dev`'s backend via `EnvironmentToImport`; they never directly own or reference the KMS key. The Section 4 wiring affects only the master-template deploy path.
- [x] 4.10 Run `aws cloudformation validate-template` on `master.template`, `application.template`, `bootstrap.template`, `backend.template`, `security.template` — all pass.
- [ ] 4.11 Push to a throwaway feature branch; verify the deploy succeeds. Feature branches use application.template (no KMS path), so this test validates the workflow plumbing (select-creds, SSM lookup, etc.) but doesn't actually exercise KMS — that gets exercised in Phase 3 when `dev` redeploys via master.template. (Expect a one-time full Docker rebuild because the ECR repo was recreated by the consolidated bootstrap in 3.4/3.5.)

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
- [ ] 6.3 Restore the latest `us-east-1` snapshot into a parallel Aurora cluster named `taskmanager-app-newkey` encrypted with the prod key from `bootstrap` (out-of-band, not via CloudFormation initially). Keep this cluster running.
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
- [ ] 7.4 Rotate both new CI access keys once (security hygiene; the secret values were visible in the consolidated bootstrap stack outputs during Phase 1b/3.4):
  - `aws iam create-access-key --user-name GitHubActionsUser` → update `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` → delete old key.
  - `aws iam create-access-key --user-name GitHubActionsUserProd` → update `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` → delete old key.
  - Trigger a deploy on a feature branch (verifies non-prod key) and on `app` (verifies prod key).
- [ ] 7.5 Once `prod-kms-admin` becomes the standard admin path (e.g., its trust policy is expanded to allow the deployer to assume it via SSO), re-deploy `bootstrap` in both regions with `KeyAdminPrincipalArn=""` to drop the deployer's exemption from the prod key's NotPrincipal Deny. Verify the resulting `NotPrincipal.AWS` list contains exactly three entries (root, `GitHubActionsUserProd`, `prod-kms-admin`).

## 8. Validation

- [ ] 8.1 Run `openspec validate centralize-aurora-kms-keys --strict` and resolve any issues.
- [ ] 8.2 Manually verify each `## Requirement` scenario from `specs/aurora-kms-key-management/spec.md` against the deployed system:
  - Exactly one `bootstrap` stack per region; no `bootstrap-prod`/`bootstrap-nonprod`/`bootstrap-shared` stacks remain.
  - Two new KMS keys per region; both aliases (`alias/taskmanager-aurora-prod` and `alias/taskmanager-aurora-nonprod`) point at them.
  - Per-branch env stacks own zero KMS resources.
  - `GitHubActionsUser` and `GitHubActionsUserProd` both exist in the consolidated `bootstrap` stack (us-east-1 only — IAM is global).
  - `GitHubActionsUserProd` cannot `ScheduleKeyDeletion` on the prod key (`AccessDeniedException`, identity-policy-based).
  - `GitHubActionsUser` cannot even `DescribeKey` on the prod key (`AccessDeniedException` with "explicit deny in a resource-based policy" from the NotPrincipal Deny).
  - SSM parameters resolve to current ARNs in both regions.
  - Workflow runs on `app` use `*_PROD` secrets; runs on other branches use the rotated non-prod secrets.
- [ ] 8.3 Run UI tests against `app` to confirm end-to-end health.
- [ ] 8.4 Archive this change per the experimental workflow (`/opsx:archive`).

## 9. Follow-ups (after this change archives)

These were considered during this change and deliberately deferred to keep the scope contained. They should each be tracked as their own OpenSpec change when picked up.

- [ ] 9.1 **Move `SharedLambdaExecutionRole` to bootstrap, delete `security.template` entirely.** Currently `security.template` exists solely to create one IAM role per env stack, with identical policies across envs — clear duplication with no functional benefit. Moving it to bootstrap requires: (a) add the role to `bootstrap.template` with an explicit `RoleName` (IAM is global, so gate on `IsPrimary`); (b) publish ARN via a new SSM parameter `/taskmanager/iam/shared-lambda-role-arn` in each region (replica region uses a `!Sub`'d predictable ARN); (c) new "Lookup shared Lambda role ARN from SSM" step in `zbuild.yml` (parallel to the KMS lookup); (d) new `SharedLambdaRoleArn` parameter threaded through master.template → backend.template and master.template → application.template → api.template / web.template; (e) replace `Fn::ImportValue: !Sub "SharedLambdaRoleArn-${EnvironmentToImport}"` in api.template (line ~47) and web.template (~lines 154 + 156) with `!Ref SharedLambdaRoleArn`; (f) delete `security.template` and its `SecurityStack` nested-stack resource in backend.template. 7 files touched + workflow + OpenSpec spec/proposal/tasks. Deferred because it adds in-flight refactor risk during the KMS-change push and conflates failure causes if dev breaks.
- [ ] 9.2 **Add `DeletionPolicy: Retain` back to bootstrap's KMS keys and SSM parameters** once Aurora clusters are actually encrypting production data. During iteration we left Retain off so failed deploys self-clean; with prod data in flight the data-loss protection of Retain outweighs the iteration friction.
- [ ] 9.3 **Set up a "prod deployer" path that doesn't require `KeyAdminPrincipalArn` on every prod bootstrap deploy.** Expand `prod-kms-admin`'s `AssumeRolePolicyDocument` so the deployer (typically a human admin via SSO) can assume it; ongoing bootstrap-prod policy updates then deploy under that role, and `KeyAdminPrincipalArn` can be set to empty on future deploys (per Resolved Decisions Q5).
