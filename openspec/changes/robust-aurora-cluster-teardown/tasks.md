## 0. Naming-convention reconciliation (discovered during implementation)

> **Context:** this change was drafted against the legacy hardcoded-`taskmanager`
> convention. The `domain-derived-resource-naming` refactor has since landed, so
> all resource names derive from the deployment domain. The tasks below were
> updated to the current convention:
> - SSM paths are `/${AWS::StackName}/...` (bootstrap, where `${AWS::StackName}`
>   = dashed domain, e.g. `appcloud-systems`) and `/{processed-domain}/...` in the
>   workflow — **not** `/taskmanager/...`.
> - Aurora clusters are auto-named by CFN today; to scope the Lambda IAM policy
>   we now give them an **explicit** `DBClusterIdentifier`
>   (`${DomainDashed}-${BranchName}`) and instance id
>   (`${DomainDashed}-${BranchName}-instance`), and scope the policy to
>   `arn:...:cluster:${AWS::StackName}-*` / `db:${AWS::StackName}-*`.
> - The bootstrap stack is named `appcloud-systems` (no `bootstrap-` prefix); the
>   secondary region is `us-east-2` (per `AWS_REGION_SECONDARY`), not `us-west-2`.

## 0.1 Explicit-naming reversal (discovered on the first dev deploy, 2026-06-05)

> **What happened:** merging this change to `dev` and deploying tried to apply the
> explicit `DBClusterIdentifier` from §0/§3.4. That forced a cluster **replacement**,
> whose new endpoint changed the `DatabaseHost-dev` CloudFormation **export** — and
> CFN refused: *"Cannot update export DatabaseHost-dev as it is in use by …WebStack…,
> …ApiStack…, and robust-aurora-cluster-teardown-appcloud-systems-ApiStack (and 3
> more)."* dev's backend exports are imported by its own Web/Api stacks **and every
> feature branch's app stack** (`application.template` imports backend exports from
> `dev`). You cannot change an export's value while any stack imports it, so the
> DbStack update **rolled back**. (Silver lining: the rollback deleted the orphaned
> new cluster via the `AuroraClusterDrainOnDelete` Lambda, giving the teardown path
> a real, successful live test.)
>
> **Decision — reverse §0's explicit-naming choice:**
> - **Drop** `DBClusterIdentifier`/`DBInstanceIdentifier` from `db.template`. CFN
>   auto-names the cluster/instance again (which is also what dev reverted to, so the
>   template now matches the deployed resource — no replacement on next deploy).
> - **Broaden** the Lambda IAM policy in `bootstrap.template` from
>   `cluster:${AWS::StackName}-*` / `db:${AWS::StackName}-*` to `cluster:*` / `db:*`.
>   Auto-named clusters (e.g. `dev-appcloud-systems-backendstack-7d-auroracluster-…`)
>   can't be reliably prefix-matched from the region-wide bootstrap role, and a
>   safety-net teardown that silently lacks delete permission is worse than a broad
>   grant. The Lambda only ever deletes the one cluster the custom resource names.
> - **Future hardening:** tag env clusters with the domain and scope the policy via
>   `aws:ResourceTag` instead of `*`. Tracked, not this change.
>
> This makes the change deployable on all shared envs (no replacement → no export
> churn → no rollback) with **no data-loss risk**.

## 1. Prereqs

- [ ] 1.1 Confirm the [`centralize-aurora-kms-keys`](../archive) change is fully landed: the consolidated `bootstrap` stack is deployed in both regions, env stacks (`dev`/`alpha`/`beta`/`app`) are using the new KMS key from the workflow's SSM lookup, and SSM parameters at `/{dashed-domain}/kms/{prod,nonprod}/aurora-key-arn` (e.g. `/appcloud-systems/kms/nonprod/aurora-key-arn`) resolve in both regions.
- [ ] 1.2 Identify the human admin IAM user / SSO role that will run the bootstrap update for the Lambda addition (same person who deployed the parent change is fine).

## 2. Lambda + bootstrap.template additions

- [x] 2.1 Add `AuroraClusterDeleteHandlerRole` to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — `AWS::IAM::Role` with `AssumeRolePolicyDocument` allowing `lambda.amazonaws.com`. Inline policy: `Describe*` on `*`; `Delete*`/`Modify*` on `arn:aws:rds:${AWS::Region}:${AWS::AccountId}:cluster:*` and `db:*`; `kms:DescribeKey` on `*`; CloudWatch Logs on `arn:aws:logs:${AWS::Region}:${AWS::AccountId}:*`. (Scope is `cluster:*`/`db:*` because env clusters are CFN auto-named and can't be prefix-matched — see §0.1.)
- [x] 2.2 Add `AuroraClusterDeleteHandlerLogGroup` — `AWS::Logs::LogGroup`, `LogGroupName: !Sub "/aws/lambda/${AuroraClusterDeleteHandler}"`, `RetentionInDays: !Ref RetentionDays`, `DeletionPolicy: Delete`.
- [x] 2.3 Add `AuroraClusterDeleteHandler` — `AWS::Lambda::Function`, `Runtime: python3.12`, `Timeout: 900`, `Handler: index.lambda_handler`, `Role: !GetAtt AuroraClusterDeleteHandlerRole.Arn`. Inline `Code.ZipFile` per design.md D4, using the `cfnresponse` module. No `Condition` (always created in both regions). Wait budget is `13 min` (Lambda hard ceiling is the 900s timeout).
- [x] 2.4 Add `AuroraClusterDeleteHandlerArnParameter` — `AWS::SSM::Parameter`, `Type: String`, `Name: !Sub "/${AWS::StackName}/lambda/aurora-cluster-delete-handler-arn"`, `Value: !GetAtt AuroraClusterDeleteHandler.Arn`. No `DeletionPolicy: Retain`.
- [x] 2.5 Add `AuroraClusterDeleteHandlerArn` to the bootstrap stack's `Outputs` section: `Value: !GetAtt AuroraClusterDeleteHandler.Arn`, no Export.
- [x] 2.6 Run `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template --region us-east-1`. — **VALID** (44.7 KB, under the 51.2 KB inline limit).
- [x] 2.7 Manual deploy (operator from 1.2), **primary region**: `aws cloudformation deploy --stack-name appcloud-systems --template-file infrastructure/bootstrap.template --capabilities CAPABILITY_NAMED_IAM --region us-east-1`. Updates the existing bootstrap stack in place — adds the new Lambda + role + log group + SSM parameter. (No `TemplatesBucketName` override — bootstrap has no such parameter; the bucket name is derived from `${AWS::StackName}`. Primary region needs no parameter overrides.) — **DONE** (stack `UPDATE_COMPLETE`; all four Aurora resources `CREATE_COMPLETE`).
- [x] 2.8 Verify in us-east-1: `aws ssm get-parameter --name /appcloud-systems/lambda/aurora-cluster-delete-handler-arn --region us-east-1` returns a valid Lambda ARN. — **DONE**: resolves to `arn:aws:lambda:us-east-1:991795635857:function:appcloud-systems-AuroraClusterDeleteHandler-3hwjcZ5Os4DA`.
- [x] 2.9 Repeat 2.7 for **us-east-2** (replica region), passing the replica-region overrides the parent change uses: `--parameter-overrides PrimaryNonprodKeyArn=<primary nonprod key arn> PrimaryProdKeyArn=<primary prod key arn>`. Then verify the SSM param resolves in us-east-2. — **DONE 2026-06-05**: in-place update of the existing us-east-2 `appcloud-systems` stack (primary keys `mrk-c423…`/`mrk-239a…`); SSM `/appcloud-systems/lambda/aurora-cluster-delete-handler-arn` resolves to `…:us-east-2:…:function:appcloud-systems-AuroraClusterDeleteHandler-PSEaz9n43e4k`; role policy scoped to `arn:aws:rds:us-east-2:…:cluster:*` (region-pinned, broadened per §0.1).
- [x] 2.10 **Re-deploy bootstrap in us-east-1** to pick up the §0.1 IAM broadening (`cluster:*`/`db:*`). 2.7 already deployed the Lambda, but with the **old** narrow `${AWS::StackName}-*` scope, which cannot delete auto-named clusters — so the in-place update is required before the env rollout (§5) can rely on the teardown. Re-run the 2.7 `aws cloudformation deploy …` command. (us-east-2 in 2.9 is not yet deployed, so it picks up the broadened policy on first deploy — no separate re-deploy needed there.) — **DONE 2026-06-05**: stack `appcloud-systems` updated; `get-role-policy` confirms `rds:DeleteDBCluster` on `…:cluster:*`.

## 3. Template wiring (master / backend / db)

- [x] 3.1 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/master.template](../../../infrastructure/master.template) (`Type: String`, no default — required). Pass through to `BackendStack`.
- [x] 3.2 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/backend.template](../../../infrastructure/backend.template). Pass through to `DbStack`.
- [x] 3.3 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/db.template](../../../infrastructure/db.template).
- [x] 3.4 Add to [infrastructure/db.template](../../../infrastructure/db.template) the `AuroraClusterDrainOnDelete` custom resource after the cluster/instance, with `DependsOn: AuroraCluster`, `ServiceToken: !Ref AuroraClusterDeleteHandlerArn`, `ClusterIdentifier: !Ref AuroraCluster`, and a comment explaining the reverse-order-deletion rationale. **Reversed per §0.1:** do **not** add explicit `DBClusterIdentifier`/`DBInstanceIdentifier` — they force a replacement that breaks the `DatabaseHost` export. The custom resource's `ClusterIdentifier: !Ref AuroraCluster` resolves to whatever auto-name CFN assigned, so the teardown works without a predictable name.
- [x] 3.5 Run `aws cloudformation validate-template` on master / backend / db. — all **VALID**.

## 4. Workflow wiring (zbuild.yml)

- [x] 4.1 Add a "Lookup Aurora cluster delete handler ARN from SSM" step to [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s `deploy` job, immediately after "Lookup bootstrap KMS key from SSM". Reads `/${processed-domain}/lambda/aurora-cluster-delete-handler-arn` in `matrix.region`, exports `AURORA_CLUSTER_DELETE_HANDLER_ARN` via `$GITHUB_ENV`, fails loudly if missing.
- [x] 4.2 In the "Set parameter overrides" step's master-template branch (`if [[ " app beta alpha dev " == *" $BRANCH_NAME "* ]]`), append `BASE_PARAMS="$BASE_PARAMS,AuroraClusterDeleteHandlerArn=${AURORA_CLUSTER_DELETE_HANDLER_ARN}"`. Feature-branch (application.template) path unchanged.
- [x] 4.3 Confirm no other workflow files reference Aurora cluster lifecycle directly. — confirmed: only `zbuild.yml` touches RDS; `cleanup-on-branch-delete.yml` relies on this custom resource.

## 5. Roll out to env stacks

> **✅ NO REPLACEMENT (per §0.1 reversal):** dropping the explicit
> `DBClusterIdentifier`/`DBInstanceIdentifier` means the rollout adds **only** the
> `AuroraClusterDrainOnDelete` custom resource — a pure addition, no cluster
> replacement, **no data-loss risk**, no export churn. The earlier replacement
> hazard is retired. **Prerequisite:** the broadened-IAM bootstrap re-deploy (§2.10
> for us-east-1, §2.9 for us-east-2) must land first, or the teardown Lambda won't
> have permission to delete the auto-named clusters.

- [ ] 5.1 Push the template + workflow changes to a throwaway feature branch first. Verify the new SSM lookup step succeeds in CI logs. Feature branches deploy `application.template` (no Aurora) so the custom resource isn't exercised — this only confirms the workflow plumbing + the SSM lookup work. (Already partly covered: `testbranch` deployed a green app stack on 2026-06-05.)
- [x] 5.2 Merge to `dev` and push, adding the custom resource. ~~This recreates dev's Aurora cluster~~ — **done 2026-06-05**; the first attempt (with explicit naming) rolled back on the `DatabaseHost-dev` export-in-use error, which prompted the §0.1 reversal. Re-deploy after the §2.10 bootstrap update + this template fix is a clean no-op add (cluster already auto-named).
- [x] 5.3 Verify dev's stack has the new custom resource: `aws cloudformation list-stack-resources … --query "…AuroraClusterDrainOnDelete…"`. — **DONE 2026-06-05**: re-deploy (commit `d4377d7`) reached `UPDATE_COMPLETE` with **no replacement** — `AuroraClusterDrainOnDelete` = `CREATE_COMPLETE`, `AuroraCluster` unchanged (still `dev-appcloud-systems-backendstack-7d-auroracluster-wylri7idvrqk`), dev app serving 200.
- [x] 5.4 Test the recovery path end-to-end on dev: `aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`. Watch delete events — the custom resource's Lambda runs, logs to its log group, and the stack reaches `DELETE_COMPLETE` cleanly without operator intervention. Re-trigger CI to recreate dev. — **PASSED 2026-06-06**: clean intentional delete reached `DELETE_COMPLETE`; drain Lambda fired `RequestType=Delete` on `dev-appcloud-systems-backendstack-11-auroracluster-7hsybpkf73o2` (`status=available` → `Deleting cluster…`), cluster confirmed gone (`DBClusterNotFoundFault`). **Operational caveat:** deleting shared dev required first clearing EVERY feature-branch app stack importing dev's exports (whack-a-mole — they redeploy on any push); needed a CI/push freeze to hold the window. Recommend the §6.2 isolated-cluster approach for routine validation. ⏭ dev still needs CI recreate.
- [ ] 5.5 Roll out to alpha, beta, app **in that order**. Each is a master-template update that adds **only** the custom resource (no replacement, no data migration needed).

## 6. Validation

- [x] 6.1 Run `openspec validate robust-aurora-cluster-teardown --strict` and resolve any issues. — passes ("Change 'robust-aurora-cluster-teardown' is valid").
- [ ] 6.2 Manually verify each `## Requirement` scenario from `specs/aurora-cluster-teardown/spec.md` against the deployed system:
  - Lambda exists in both regions and the SSM param resolves.
  - Lambda IAM policy allows `rds:DeleteDBCluster` on the env's auto-named cluster (scope is `cluster:*`/`db:*` per §0.1; the earlier prefix-deny check no longer applies).
  - dev/alpha/beta/app all have the `AuroraClusterDrainOnDelete` custom resource.
  - Recovery path: inject a stuck state on a non-production cluster (e.g. temporarily Deny on its KMS key via `aws kms put-key-policy`), trigger stack delete, confirm the Lambda log shows the cluster being force-drained. Restore the KMS policy after.
- [ ] 6.3 Archive this change per the experimental workflow (`/opsx:archive`).
