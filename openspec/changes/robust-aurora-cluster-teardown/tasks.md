## 1. Prereqs

- [ ] 1.1 Confirm the [`centralize-aurora-kms-keys`](../centralize-aurora-kms-keys/proposal.md) change is fully landed: the consolidated `bootstrap` stack is deployed in both regions, env stacks (`dev`/`alpha`/`beta`/`app`) are using the new KMS key from the workflow's SSM lookup, and SSM parameters at `/taskmanager/kms/{prod,nonprod}/aurora-key-arn` resolve in both regions.
- [ ] 1.2 Identify the human admin IAM user / SSO role that will run the bootstrap update for the Lambda addition (same person who deployed the parent change is fine).

## 2. Lambda + bootstrap.template additions

- [ ] 2.1 Add `AuroraClusterDeleteHandlerRole` to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — `AWS::IAM::Role` with `AssumeRolePolicyDocument` allowing `lambda.amazonaws.com`. Inline policies per design.md D5: `Describe*` on `*`, `Delete*`/`Modify*` on `arn:aws:rds:${Region}:${Account}:cluster:taskmanager-*` and `arn:aws:rds:${Region}:${Account}:db:taskmanager-*`, `kms:DescribeKey` on `*`, CloudWatch Logs on the Lambda's own log group ARN.
- [ ] 2.2 Add `AuroraClusterDeleteHandlerLogGroup` — `AWS::Logs::LogGroup`, `LogGroupName: !Sub "/aws/lambda/${AuroraClusterDeleteHandler}"`, `RetentionInDays: !Ref RetentionDays` (reuse the existing bootstrap parameter), `DeletionPolicy: Delete`.
- [ ] 2.3 Add `AuroraClusterDeleteHandler` — `AWS::Lambda::Function`, `Runtime: python3.12`, `Timeout: 900`, `Handler: index.lambda_handler`, `Role: !GetAtt AuroraClusterDeleteHandlerRole.Arn`. Inline `Code.ZipFile` containing the handler per design.md D4. Use the `cfnresponse` module pattern (standard for inline-ZipFile Lambdas). No `Condition` (always created in both primary and replica regions).
- [ ] 2.4 Add `AuroraClusterDeleteHandlerArnParameter` — `AWS::SSM::Parameter`, `Type: String`, `Name: "/taskmanager/lambda/aurora-cluster-delete-handler-arn"`, `Value: !GetAtt AuroraClusterDeleteHandler.Arn`. No `DeletionPolicy: Retain` (consistent with KMS-param iteration policy).
- [ ] 2.5 Add `AuroraClusterDeleteHandlerArn` to the bootstrap stack's `Outputs` section: `Value: !GetAtt AuroraClusterDeleteHandler.Arn`, no Export.
- [ ] 2.6 Run `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template --region us-east-1`.
- [ ] 2.7 Manual deploy: operator from 1.2 runs `aws cloudformation deploy --stack-name bootstrap-appcloud-systems --template-file infrastructure/bootstrap.template --parameter-overrides TemplatesBucketName=cf-templates-991795635857-us-east-1 --capabilities CAPABILITY_NAMED_IAM --region us-east-1`. Updates the existing bootstrap stack in place — adds the new Lambda + role + log group + SSM parameter.
- [ ] 2.8 Verify in us-east-1: `aws ssm get-parameter --name /taskmanager/lambda/aurora-cluster-delete-handler-arn --region us-east-1` returns a valid Lambda ARN.
- [ ] 2.9 Repeat 2.7 for us-west-2 (replica region) with the appropriate `PrimaryNonprodKeyArn` / `PrimaryProdKeyArn` / `TemplatesBucketName` overrides as per the parent change's tasks 3.5.

## 3. Template wiring (master / backend / db)

- [ ] 3.1 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/master.template](../../../infrastructure/master.template) (`Type: String`, no default — required). Pass through to `BackendStack` as a stack parameter.
- [ ] 3.2 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/backend.template](../../../infrastructure/backend.template). Pass through to `DbStack`.
- [ ] 3.3 Add `AuroraClusterDeleteHandlerArn` parameter to [infrastructure/db.template](../../../infrastructure/db.template).
- [ ] 3.4 Add the custom resource to [infrastructure/db.template](../../../infrastructure/db.template), positioned after the `AuroraCluster` and `AuroraInstance` resources:
  ```yaml
  AuroraClusterDrainOnDelete:
    Type: Custom::AuroraClusterDrainOnDelete
    DependsOn: AuroraCluster
    Properties:
      ServiceToken: !Ref AuroraClusterDeleteHandlerArn
      ClusterIdentifier: !Ref AuroraCluster
  ```
  Include a YAML comment explaining the `DependsOn` rationale (reverse-order deletion) so future editors don't accidentally remove it.
- [ ] 3.5 Run `aws cloudformation validate-template` on all three updated templates.

## 4. Workflow wiring (zbuild.yml)

- [ ] 4.1 Add a new "Lookup Aurora cluster delete handler ARN from SSM" step to [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s `deploy` job, immediately after the existing "Lookup bootstrap KMS key from SSM" step. The step calls `aws ssm get-parameter --name /taskmanager/lambda/aurora-cluster-delete-handler-arn --region ${{ matrix.region }} --query Parameter.Value --output text` and exports the value as `AURORA_CLUSTER_DELETE_HANDLER_ARN` via `$GITHUB_ENV`. Fail loudly if the SSM parameter is missing.
- [ ] 4.2 Update the "Set parameter overrides" step's master-template branch (the `if [[ " app beta alpha dev " == *" $BRANCH_NAME "* ]]` block) to append `BASE_PARAMS="$BASE_PARAMS,AuroraClusterDeleteHandlerArn=${AURORA_CLUSTER_DELETE_HANDLER_ARN}"`. Feature-branch path (application.template) is unchanged.
- [ ] 4.3 Verify no other workflow files (`.github/workflows/cleanup-on-branch-delete.yml`, etc.) reference Aurora cluster lifecycle directly — they don't today, but worth confirming so we don't miss a parallel update.

## 5. Roll out to env stacks

- [ ] 5.1 Push the template + workflow changes to a throwaway feature branch first. Verify the new SSM lookup step succeeds in CI logs. Feature branches deploy `application.template` (no Aurora) so the custom resource isn't actually created — this just confirms the workflow plumbing works.
- [ ] 5.2 Merge to `dev` and push. Watch the CI deploy: master.template updates, the custom resource gets created in dev's existing stack. Should complete in < 5 min (custom resource Create is a Lambda call that no-ops in ~2 seconds).
- [ ] 5.3 Verify dev's stack has the new custom resource: `aws cloudformation list-stack-resources --stack-name dev-appcloud-systems --region us-east-1 --query "StackResourceSummaries[?LogicalResourceId=='AuroraClusterDrainOnDelete']"`.
- [ ] 5.4 Test the recovery path end-to-end on dev: `aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`. Watch the delete events — the custom resource's Lambda should run, log to its log group, and the stack should reach `DELETE_COMPLETE` cleanly without operator intervention. Then re-trigger CI to recreate dev (or push an empty commit).
- [ ] 5.5 Repeat 5.2-5.3 for alpha, beta, app — in that order. Each is a master-template update that adds the custom resource; no Aurora behavior change at deploy time.

## 6. Validation

- [ ] 6.1 Run `openspec validate robust-aurora-cluster-teardown --strict` and resolve any issues.
- [ ] 6.2 Manually verify each `## Requirement` scenario from `specs/aurora-cluster-teardown/spec.md` against the deployed system:
  - Lambda exists in both regions and the SSM param resolves.
  - Lambda IAM policy denies hypothetical `taskmanager-*`-pattern-mismatching cluster deletes (use IAM Policy Simulator or `aws iam simulate-principal-policy`).
  - dev/alpha/beta/app all have the `AuroraClusterDrainOnDelete` custom resource.
  - Test the actual recovery path: inject a stuck state on a non-production cluster (e.g., temporarily put a Deny on its KMS key via direct `aws kms put-key-policy`), trigger stack delete, confirm the Lambda log shows the cluster being force-drained. Restore the original KMS policy after.
- [ ] 6.3 Archive this change per the experimental workflow (`/opsx:archive`).
