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

## 1. Prereqs

- [ ] 1.1 Confirm the [`centralize-aurora-kms-keys`](../archive) change is fully landed: the consolidated `bootstrap` stack is deployed in both regions, env stacks (`dev`/`alpha`/`beta`/`app`) are using the new KMS key from the workflow's SSM lookup, and SSM parameters at `/{dashed-domain}/kms/{prod,nonprod}/aurora-key-arn` (e.g. `/appcloud-systems/kms/nonprod/aurora-key-arn`) resolve in both regions.
- [ ] 1.2 Identify the human admin IAM user / SSO role that will run the bootstrap update for the Lambda addition (same person who deployed the parent change is fine).

## 2. Lambda + bootstrap.template additions

- [x] 2.1 Add `AuroraClusterDeleteHandlerRole` to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — `AWS::IAM::Role` with `AssumeRolePolicyDocument` allowing `lambda.amazonaws.com`. Inline policy: `Describe*` on `*`; `Delete*`/`Modify*` on `arn:aws:rds:${AWS::Region}:${AWS::AccountId}:cluster:${AWS::StackName}-*` and `db:${AWS::StackName}-*`; `kms:DescribeKey` on `*`; CloudWatch Logs on `arn:aws:logs:${AWS::Region}:${AWS::AccountId}:*`. (Scoping uses `${AWS::StackName}` = dashed domain; see §0.)
- [x] 2.2 Add `AuroraClusterDeleteHandlerLogGroup` — `AWS::Logs::LogGroup`, `LogGroupName: !Sub "/aws/lambda/${AuroraClusterDeleteHandler}"`, `RetentionInDays: !Ref RetentionDays`, `DeletionPolicy: Delete`.
- [x] 2.3 Add `AuroraClusterDeleteHandler` — `AWS::Lambda::Function`, `Runtime: python3.12`, `Timeout: 900`, `Handler: index.lambda_handler`, `Role: !GetAtt AuroraClusterDeleteHandlerRole.Arn`. Inline `Code.ZipFile` per design.md D4, using the `cfnresponse` module. No `Condition` (always created in both regions). Wait budget is `13 min` (Lambda hard ceiling is the 900s timeout).
- [x] 2.4 Add `AuroraClusterDeleteHandlerArnParameter` — `AWS::SSM::Parameter`, `Type: String`, `Name: !Sub "/${AWS::StackName}/lambda/aurora-cluster-delete-handler-arn"`, `Value: !GetAtt AuroraClusterDeleteHandler.Arn`. No `DeletionPolicy: Retain`.
- [x] 2.5 Add `AuroraClusterDeleteHandlerArn` to the bootstrap stack's `Outputs` section: `Value: !GetAtt AuroraClusterDeleteHandler.Arn`, no Export.
- [x] 2.6 Run `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template --region us-east-1`. — **VALID** (44.7 KB, under the 51.2 KB inline limit).
- [x] 2.7 Manual deploy (operator from 1.2), **primary region**: `aws cloudformation deploy --stack-name appcloud-systems --template-file infrastructure/bootstrap.template --capabilities CAPABILITY_NAMED_IAM --region us-east-1`. Updates the existing bootstrap stack in place — adds the new Lambda + role + log group + SSM parameter. (No `TemplatesBucketName` override — bootstrap has no such parameter; the bucket name is derived from `${AWS::StackName}`. Primary region needs no parameter overrides.) — **DONE** (stack `UPDATE_COMPLETE`; all four Aurora resources `CREATE_COMPLETE`).
- [x] 2.8 Verify in us-east-1: `aws ssm get-parameter --name /appcloud-systems/lambda/aurora-cluster-delete-handler-arn --region us-east-1` returns a valid Lambda ARN. — **DONE**: resolves to `arn:aws:lambda:us-east-1:991795635857:function:appcloud-systems-AuroraClusterDeleteHandler-3hwjcZ5Os4DA`.
- [ ] 2.9 Repeat 2.7 for **us-east-2** (replica region), passing the replica-region overrides the parent change uses: `--parameter-overrides PrimaryNonprodKeyArn=<primary nonprod key arn> PrimaryProdKeyArn=<primary prod key arn>`. Then verify the SSM param resolves in us-east-2.

## 3. Template wiring (master / backend / db)

- [x] 3.1 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/master.template](../../../infrastructure/master.template) (`Type: String`, no default — required). Pass through to `BackendStack`.
- [x] 3.2 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/backend.template](../../../infrastructure/backend.template). Pass through to `DbStack`.
- [x] 3.3 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/db.template](../../../infrastructure/db.template).
- [x] 3.4 Add to [infrastructure/db.template](../../../infrastructure/db.template): (a) explicit `DBClusterIdentifier: ${DomainDashed}-${BranchName}` on `AuroraCluster` and `DBInstanceIdentifier: ${DomainDashed}-${BranchName}-instance` on `AuroraInstance` (needed so the Lambda's IAM policy can scope to `${AWS::StackName}-*`); (b) the `AuroraClusterDrainOnDelete` custom resource after the cluster/instance, with `DependsOn: AuroraCluster`, `ServiceToken: !Ref AuroraClusterDeleteHandlerArn`, `ClusterIdentifier: !Ref AuroraCluster`, and a comment explaining the reverse-order-deletion rationale.
- [x] 3.5 Run `aws cloudformation validate-template` on master / backend / db. — all **VALID**.

## 4. Workflow wiring (zbuild.yml)

- [x] 4.1 Add a "Lookup Aurora cluster delete handler ARN from SSM" step to [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s `deploy` job, immediately after "Lookup bootstrap KMS key from SSM". Reads `/${processed-domain}/lambda/aurora-cluster-delete-handler-arn` in `matrix.region`, exports `AURORA_CLUSTER_DELETE_HANDLER_ARN` via `$GITHUB_ENV`, fails loudly if missing.
- [x] 4.2 In the "Set parameter overrides" step's master-template branch (`if [[ " app beta alpha dev " == *" $BRANCH_NAME "* ]]`), append `BASE_PARAMS="$BASE_PARAMS,AuroraClusterDeleteHandlerArn=${AURORA_CLUSTER_DELETE_HANDLER_ARN}"`. Feature-branch (application.template) path unchanged.
- [x] 4.3 Confirm no other workflow files reference Aurora cluster lifecycle directly. — confirmed: only `zbuild.yml` touches RDS; `cleanup-on-branch-delete.yml` relies on this custom resource.

## 5. Roll out to env stacks

> **⚠ REPLACEMENT RISK (new — from the explicit-naming decision in §0/§3.4):**
> Adding `DBClusterIdentifier`/`DBInstanceIdentifier` to the **existing**
> `dev`/`alpha`/`beta`/`app` clusters is a CloudFormation **replacement** —
> CFN will create a new cluster and delete the old one, **destroying its data**.
> Before rolling out to any env that holds data you care about, choose a path:
> (a) snapshot the cluster, deploy (accept recreate), restore from snapshot; or
> (b) defer the rename and instead broaden the IAM scope to
> `arn:...:cluster:*` (revisit the §0 decision). `dev` is safe to recreate.
> Do **not** push this to `app` without a deliberate data-migration plan.

- [ ] 5.1 Push the template + workflow changes to a throwaway feature branch first. Verify the new SSM lookup step succeeds in CI logs. Feature branches deploy `application.template` (no Aurora) so the custom resource and cluster rename aren't exercised — this only confirms the workflow plumbing + the SSM lookup work.
- [ ] 5.2 Merge to `dev` and push. **This recreates dev's Aurora cluster** (explicit-name replacement) and adds the custom resource. Acceptable on dev (no production data). Watch the CI deploy.
- [ ] 5.3 Verify dev's stack has the new custom resource and the renamed cluster: `aws cloudformation list-stack-resources --stack-name dev-appcloud-systems --region us-east-1 --query "StackResourceSummaries[?LogicalResourceId=='AuroraClusterDrainOnDelete']"`.
- [ ] 5.4 Test the recovery path end-to-end on dev: `aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`. Watch delete events — the custom resource's Lambda runs, logs to its log group, and the stack reaches `DELETE_COMPLETE` cleanly without operator intervention. Re-trigger CI to recreate dev.
- [ ] 5.5 Roll out to alpha, beta, app **in that order**, executing the chosen §5 data-migration path for each (these hold data). Each is a master-template update that adds the custom resource AND renames the cluster (replacement).

## 6. Validation

- [x] 6.1 Run `openspec validate robust-aurora-cluster-teardown --strict` and resolve any issues. — passes ("Change 'robust-aurora-cluster-teardown' is valid").
- [ ] 6.2 Manually verify each `## Requirement` scenario from `specs/aurora-cluster-teardown/spec.md` against the deployed system:
  - Lambda exists in both regions and the SSM param resolves.
  - Lambda IAM policy denies a hypothetical non-`${dashed-domain}-*` cluster delete (use `aws iam simulate-principal-policy`).
  - dev/alpha/beta/app all have the `AuroraClusterDrainOnDelete` custom resource.
  - Recovery path: inject a stuck state on a non-production cluster (e.g. temporarily Deny on its KMS key via `aws kms put-key-policy`), trigger stack delete, confirm the Lambda log shows the cluster being force-drained. Restore the KMS policy after.
- [ ] 6.3 Archive this change per the experimental workflow (`/opsx:archive`).
