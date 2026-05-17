## 1. Phase 0 — Prereqs

- [ ] 1.1 Decide and capture answers to design's Open Questions (separate prod deployer credentials? dev data loss? one change or two? clean up orphans? bootstrap-prod chicken-and-egg).
- [ ] 1.2 Inventory existing orphaned `AuroraKmsKey` resources in `us-east-1` and `us-west-2` (description matches `Multi-region KMS key for Aurora Global Database encryption - *`); save the list for Phase 5 cleanup.
- [ ] 1.3 Run `aws sesv2 create-email-identity --email-identity appcloud.systems --region us-east-2`; add the three returned DKIM CNAMEs to Route 53 hosted zone `Z06422172SASV44F5Y8VA`.
- [ ] 1.4 Verify `aws sesv2 get-email-identity --email-identity appcloud.systems --region us-east-2` shows `DkimAttributes.Status: SUCCESS`.
- [ ] 1.5 Submit SES production-access request for `us-east-2` (`aws sesv2 put-account-details ... --region us-east-2`). Track the AWS turnaround (~24h).

## 2. Phase 1 — Bootstrap template changes

- [ ] 2.1 Add `BootstrapScope` parameter (`AllowedValues: [prod, nonprod]`) to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) and a `Conditions.IsProdScope` block.
- [ ] 2.2 Add `AuroraKmsKey` resource (multi-region, `DeletionPolicy: Retain`) to bootstrap.template, gated on `IsPrimaryRegion` parameter (mirror existing primary/replica split in security.template).
- [ ] 2.3 Add `AuroraKmsKeyReplica` resource gated on the non-primary region path, with `PrimaryKeyArn` parameter.
- [ ] 2.4 Add scoped `KeyPolicy`: `IsProdScope` → restricted policy (no destructive ops for CI user; `prod-kms-admin` role for destructive ops); else broad policy.
- [ ] 2.5 Add `prod-kms-admin` IAM role (conditional on `IsProdScope`) with `AssumeRolePolicyDocument` allowing only `AWS::AccountId:root`.
- [ ] 2.6 Add `AWS::KMS::Alias` resource — `alias/taskmanager-aurora-${BootstrapScope}` — pointing at the key/replica.
- [ ] 2.7 Add `AWS::SSM::Parameter` resource at path `/taskmanager/kms/${BootstrapScope}/aurora-key-arn` with `DeletionPolicy: Retain`; value is the key ARN.
- [ ] 2.8 Suffix existing bootstrap stack instance: rename the original `bootstrap` stack output names where they would collide (none expected — existing exports like `GitHubActionsUserArn` keep their names because they live in the unscoped `bootstrap` stack only).
- [ ] 2.9 Confirm the unscoped `bootstrap` stack still works after the template changes by running `aws cloudformation validate-template` and inspecting that the `BootstrapScope` parameter defaults sensibly (or is required only for the scoped instances).
- [ ] 2.10 Manually deploy in `us-east-1`: `aws cloudformation deploy --stack-name bootstrap-nonprod --template-file infrastructure/bootstrap.template --parameter-overrides BootstrapScope=nonprod IsPrimaryRegion=true ... --capabilities CAPABILITY_NAMED_IAM --region us-east-1`.
- [ ] 2.11 Manually deploy in `us-east-1`: `bootstrap-prod` with `BootstrapScope=prod IsPrimaryRegion=true`. Use credentials of an operator that satisfies the chicken-and-egg from Open Question #5.
- [ ] 2.12 Verify SSM parameters: `aws ssm get-parameter --name /taskmanager/kms/nonprod/aurora-key-arn --region us-east-1` and `/taskmanager/kms/prod/aurora-key-arn` both return valid ARNs.
- [ ] 2.13 Verify key policies are correct: try `aws kms schedule-key-deletion` against the prod key using GitHub Actions CI credentials in a test workflow — confirm it fails with `AccessDeniedException`.

## 3. Phase 1b — Wire env stacks to the new keys (still us-west-2 secondary)

- [ ] 3.1 Add a workflow step in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) before "Set parameter overrides" that reads `/taskmanager/kms/${SCOPE}/aurora-key-arn` from SSM in `matrix.region`, where `SCOPE=prod` iff `BRANCH_NAME == app` else `nonprod`. Export as `BOOTSTRAP_KMS_KEY_ARN`.
- [ ] 3.2 Change "Set parameter overrides" so master.template's `KmsKeyArn` parameter sources from `BOOTSTRAP_KMS_KEY_ARN` instead of `steps.get-primary-outputs.outputs.kms-key-arn`.
- [ ] 3.3 Update [infrastructure/backend.template](../../../infrastructure/backend.template) to pass `KmsKeyArn` directly through to `DbStack` (`AuroraKmsKeyArn: !Ref KmsKeyArn`) instead of via `!GetAtt SecurityStack.Outputs.AuroraKmsKeyArn`. Remove the dependency between `DbStack` and `SecurityStack`.
- [ ] 3.4 Update [infrastructure/master.template](../../../infrastructure/master.template) so `KmsKeyArn` is required (no longer defaults to `""`); remove `HasKmsKeyArn` condition and the conditional `!Ref "AWS::NoValue"` pass-through.
- [ ] 3.5 Update [infrastructure/security.template](../../../infrastructure/security.template) to remove KMS-related parameters (`KmsKeyArn`) and conditions (`IsPrimaryKey`/`IsReplicaKey`) — but **leave the `AuroraKmsKey` / `AuroraKmsKeyReplica` resources in place for now** so existing stacks can be updated without dropping the key. (They will be removed in Phase 5.)
- [ ] 3.6 Update [infrastructure/application.template](../../../infrastructure/application.template) (feature-branch path) to also accept `KmsKeyArn` from the workflow rather than importing the dev env's KMS export. Verify imports of `AuroraKmsKeyArn-dev` are removed.
- [ ] 3.7 `sam validate` the templates locally.
- [ ] 3.8 Push to a throwaway feature branch and verify the deploy succeeds against the nonprod key, that the Aurora cluster comes up with `KmsKeyId` set to the SSM-resolved ARN, and that the test app responds at the per-branch URL.

## 4. Phase 2 — Non-prod env migration

- [ ] 4.1 Snapshot existing `alpha` and `beta` Aurora clusters in both `us-east-1` and `us-west-2`: `aws rds create-db-cluster-snapshot --db-cluster-identifier <id> --db-cluster-snapshot-identifier pre-kms-migration-<env>-<region>-<date>`.
- [ ] 4.2 Confirm with user that `dev` database content can be wiped (Open Question #2). If yes, proceed; if no, snapshot `dev` too.
- [ ] 4.3 Delete the `dev` env stack: `aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`; wait for `DELETE_COMPLETE`.
- [ ] 4.4 Trigger CI on `dev` branch (empty push or workflow re-run); verify new cluster comes up healthy and its `KmsKeyId` matches the nonprod SSM parameter value.
- [ ] 4.5 Run `aws rds describe-db-clusters --query 'DBClusters[?DBClusterIdentifier==`taskmanager-...`].KmsKeyId'` to confirm.
- [ ] 4.6 Delete `alpha` env stacks (both regions in order: `us-west-2` first, then `us-east-1` — global cluster requires this).
- [ ] 4.7 Trigger CI on `alpha`; restore snapshot into the new cluster via `aws rds restore-db-cluster-from-snapshot` (this may require manual orchestration outside CI — document the steps).
- [ ] 4.8 Repeat 4.6–4.7 for `beta`.
- [ ] 4.9 Trigger a redeploy on two or three representative feature branches; verify they pick up the nonprod key via the new application.template / workflow lookup.
- [ ] 4.10 Smoke-test each migrated env via the deployed URL (`https://dev.appcloud.systems`, `https://alpha.appcloud.systems`, `https://beta.appcloud.systems`).

## 5. Phase 3 — Prod migration (`app`)

- [ ] 5.1 Schedule a maintenance window with stakeholders; communicate the planned downtime envelope.
- [ ] 5.2 Snapshot `app` Aurora cluster in `us-east-1` and `us-west-2`: `pre-kms-migration-app-<region>-<date>`.
- [ ] 5.3 Restore the latest `us-east-1` snapshot into a parallel Aurora cluster named `taskmanager-app-newkey` encrypted with the `bootstrap-prod` key (out-of-band, not via CloudFormation initially). Keep this cluster running and catching up.
- [ ] 5.4 During the maintenance window: drain web traffic (scale ECS service to 0 or put up a maintenance page).
- [ ] 5.5 Final delta-sync writes from old cluster to new cluster (or stop writes and re-snapshot+restore).
- [ ] 5.6 Update `taskmanager/database/app/regional/us-east-1/password` (and reader endpoint reference) to point at the new cluster.
- [ ] 5.7 Roll the ECS service; verify the app comes up healthy reading/writing the new cluster.
- [ ] 5.8 Run an `app`-branch CloudFormation deploy that adopts the new cluster (either by importing it into the stack or by recreating the cluster resource pointing at the same snapshot). Document the exact adopt strategy chosen here.
- [ ] 5.9 Verify global cluster membership: the new `us-east-1` cluster is the primary; the existing `us-west-2` cluster is still the secondary (it's still on the OLD prod KMS key — that's fine for now; Phase 4 will rebuild it).
- [ ] 5.10 Decommission the old `us-east-1` cluster only after a soak period (≥24h).

## 6. Phase 4 — Region shift (us-west-2 → us-east-2)

- [ ] 6.1 Wait at least 1 week after Phase 3 completes; verify no incidents tied to the KMS migration.
- [ ] 6.2 For each of `alpha`, `beta`, `app`: snapshot the `us-west-2` regional cluster (`pre-region-shift-<env>-uswest2-<date>`).
- [ ] 6.3 Remove `us-west-2` regional clusters from each global cluster: `aws rds remove-from-global-cluster --global-cluster-identifier taskmanager-<env>-global-cluster --db-cluster-identifier <usw2-cluster-arn>`.
- [ ] 6.4 Delete `us-west-2` env stacks: `aws cloudformation delete-stack --stack-name <env>-appcloud-systems --region us-west-2` for `alpha`, `beta`, `app`.
- [ ] 6.5 Deploy `bootstrap`, `bootstrap-nonprod`, `bootstrap-prod` to `us-east-2` (mirror the manual deploys from Phase 1, just with `--region us-east-2`).
- [ ] 6.6 Verify SSM parameters in `us-east-2` and that the prod KMS replica's policy is correct.
- [ ] 6.7 Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml): change `AWS_REGION_SECONDARY: us-west-2` → `us-east-2`. Update the matrix block `region: us-west-2` → `us-east-2`. Search the workflow for any other `us-west-2` literal and replace.
- [ ] 6.8 Commit the workflow change; PR; merge to `app` so the change reaches the default branch and any default-branch-only event handlers will see it.
- [ ] 6.9 Trigger redeploys in order `alpha` → `beta` → `app` (lowest-criticality first). Each will create its `us-east-2` regional cluster from scratch and add it to the global cluster.
- [ ] 6.10 Verify `aws rds describe-global-clusters --global-cluster-identifier taskmanager-app-global-cluster` shows two members: `us-east-1` (writer) and `us-east-2` (reader).
- [ ] 6.11 Update Route 53 DNS failover health checks to point at the new `us-east-2` ALB.
- [ ] 6.12 Smoke-test failover: scale down primary briefly (in a maintenance window only) and verify traffic moves to `us-east-2`.

## 7. Phase 5 — Cleanup

- [ ] 7.1 Delete the orphaned `AuroraKmsKey` resources inventoried in 1.2: for each, `aws kms schedule-key-deletion --key-id <arn> --pending-window-in-days 7 --region <region>`.
- [ ] 7.2 Final commit on [infrastructure/security.template](../../../infrastructure/security.template): remove `AuroraKmsKey`, `AuroraKmsKeyReplica`, `KmsKeyArn` parameter, `IsPrimaryKey`/`IsReplicaKey` conditions, `AuroraKmsKeyArn` output. Keep `SharedLambdaExecutionRole` and its output. `BranchName` parameter stays since other resources still use it.
- [ ] 7.3 Re-deploy each env stack (`dev`, `alpha`, `beta`, `app`) so the now-empty KMS portion of security.template is reflected. Verify CloudFormation drift-detection shows no diffs against the new template.
- [ ] 7.4 Delete the old `bootstrap` stack in `us-west-2`: `aws cloudformation delete-stack --stack-name bootstrap --region us-west-2`. Verify the deletion is clean.
- [ ] 7.5 Update [CLAUDE.md](../../../CLAUDE.md) to reflect: `us-east-2` as secondary region; new bootstrap stack topology (`bootstrap` / `bootstrap-prod` / `bootstrap-nonprod`); KMS key discovery via SSM; SES verification list updated to include `us-east-2`.
- [ ] 7.6 Update [BRANCH_MANAGEMENT_README.md](../../../BRANCH_MANAGEMENT_README.md) — region references and any KMS notes.
- [ ] 7.7 Add a one-line note to README clarifying the bootstrap stack split.

## 8. Validation

- [ ] 8.1 Run `openspec validate bootstrap-kms-and-region-shift --strict` and resolve any issues.
- [ ] 8.2 Manually verify each `## Requirement` scenario from the two new specs against the deployed system:
  - Two new KMS keys exist per region; aliases match.
  - Per-branch env stacks own zero KMS resources.
  - CI cannot `ScheduleKeyDeletion` on the prod key (test in a throwaway feature branch workflow).
  - SSM parameters resolve to current ARNs in both regions.
  - Aurora Global Cluster spans `us-east-1` + `us-east-2`, never `us-west-2`.
  - Workflow file contains `AWS_REGION_SECONDARY: us-east-2` and no `us-west-2` literal.
- [ ] 8.3 Run UI tests against `app` to confirm end-to-end health.
- [ ] 8.4 Archive this change per the experimental workflow (`/opsx:archive`).
