## 1. Prereqs

- [ ] 1.1 Confirm the [`centralize-aurora-kms-keys`](../centralize-aurora-kms-keys/proposal.md) change has been deployed to all four env stacks. SSM parameters `/${AWS::StackName}/kms/{prod,nonprod}/aurora-key-arn` (e.g. `/appcloud-systems/kms/...`) resolve in both regions; env stacks are encrypted with the bootstrap-owned keys.
- [ ] 1.2 Verify no pre-existing IAM role would collide with the new name: `aws iam get-role --role-name ${AWS::StackName}-shared-lambda-execution-role` should return `NoSuchEntity`. If a stale role exists from prior experimentation, schedule its deletion before Phase 1.
- [ ] 1.3 Verify no out-of-tree consumer of the legacy export: `aws cloudformation list-exports --region us-east-1 --query "Exports[?starts_with(Name, 'SharedLambdaRoleArn-')]" --output table`. The result should contain only `dev`/`alpha`/`beta`/`app` from this repo (and only those that are currently deployed). Document any unexpected entries before proceeding.
- [ ] 1.4 Verify no AWS resource outside our templates has a resource policy referencing the existing per-env role ARNs: `aws iam list-roles --query "Roles[?contains(RoleName, 'SharedLambdaExecutionRole')]"` to list them, then audit Secrets Manager / S3 / KMS-key-outside-our-control policies for those ARNs. We expect zero hits.

## 2. Bootstrap update (Phase 1)

- [ ] 2.1 Add `SharedLambdaExecutionRole` resource to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — `Condition: IsPrimary`, `RoleName: ${AWS::StackName}-shared-lambda-execution-role`, `AssumeRolePolicyDocument` and inline policies copied verbatim from [security.template](../../../infrastructure/security.template) (so behavior is byte-identical).
- [ ] 2.2 Add `SharedLambdaRoleArnParameter` resource — `AWS::SSM::Parameter`, `Name: /${AWS::StackName}/iam/shared-lambda-role-arn`. Primary region: `Value: !GetAtt SharedLambdaExecutionRole.Arn`. Replica region: `Value: !Sub "arn:aws:iam::${AWS::AccountId}:role/${AWS::StackName}-shared-lambda-execution-role"`. Use `!If [IsPrimary, ...]` to select between the two value forms (the parameter resource itself is always created — both regions need their own SSM parameter for the workflow's regional lookup).
- [ ] 2.3 Add `SharedLambdaRoleArn` to bootstrap outputs: `Value: !If [IsPrimary, !GetAtt SharedLambdaExecutionRole.Arn, !Sub "arn:aws:iam::${AWS::AccountId}:role/${AWS::StackName}-shared-lambda-execution-role"]`. No Export.
- [ ] 2.4 `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template --region us-east-1`.
- [ ] 2.5 Operator deploys bootstrap update to us-east-1: `aws cloudformation deploy --stack-name bootstrap-appcloud-systems --template-file infrastructure/bootstrap.template --parameter-overrides TemplatesBucketName=cf-templates-991795635857-us-east-1 --capabilities CAPABILITY_NAMED_IAM --region us-east-1`.
- [ ] 2.6 Verify in us-east-1: `aws ssm get-parameter --name /${AWS::StackName}/iam/shared-lambda-role-arn --region us-east-1` returns `arn:aws:iam::991795635857:role/${AWS::StackName}-shared-lambda-execution-role`. `aws iam get-role --role-name ${AWS::StackName}-shared-lambda-execution-role` returns the role with the expected policies.
- [ ] 2.7 Deploy to us-west-2 (replica) with the appropriate `PrimaryNonprodKeyArn`/`PrimaryProdKeyArn`/`TemplatesBucketName` overrides. Verify `aws ssm get-parameter --name /${AWS::StackName}/iam/shared-lambda-role-arn --region us-west-2` returns the same ARN as us-east-1 (since IAM is global).

## 3. Template + workflow changes (Phase 2)

- [ ] 3.1 Add `SharedLambdaRoleArn` parameter to [master.template](../../../infrastructure/master.template) (`Type: String`, required). Pass through to `BackendStack` AND `ApplicationStack`.
- [ ] 3.2 Add `SharedLambdaRoleArn` parameter to [backend.template](../../../infrastructure/backend.template). Update the `SharedLambdaRoleArn` output to use `!Ref SharedLambdaRoleArn` (since `SecurityStack.Outputs.SharedLambdaRoleArn` is going away).
- [ ] 3.3 Remove the `SecurityStack` nested-stack resource from [backend.template](../../../infrastructure/backend.template) entirely. Remove `DependsOn: SecurityStack` from any sibling resource that still has it (audit first).
- [ ] 3.4 Add `SharedLambdaRoleArn` parameter to [application.template](../../../infrastructure/application.template). Pass through to `ApiStack` and `WebStack`.
- [ ] 3.5 [api.template](../../../infrastructure/api.template): add `SharedLambdaRoleArn` parameter; replace `Fn::ImportValue: !Sub "SharedLambdaRoleArn-${EnvironmentToImport}"` with `!Ref SharedLambdaRoleArn` (one occurrence, line ~47 per the parent change's grep).
- [ ] 3.6 [web.template](../../../infrastructure/web.template): add `SharedLambdaRoleArn` parameter; replace `Fn::ImportValue: !Sub "SharedLambdaRoleArn-${EnvironmentToImport}"` with `!Ref SharedLambdaRoleArn` at lines ~154 and ~156 (both `TaskRoleArn` and `ExecutionRoleArn`).
- [ ] 3.7 Delete [infrastructure/security.template](../../../infrastructure/security.template) (the file is now empty of useful content; its only resource is in bootstrap.template).
- [ ] 3.8 Edit [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml): add a "Lookup shared Lambda role ARN from SSM" step immediately after the existing "Lookup bootstrap KMS key from SSM" step. The step reads `/${AWS::StackName}/iam/shared-lambda-role-arn` in `${{ matrix.region }}` and exports `SHARED_LAMBDA_ROLE_ARN` via `$GITHUB_ENV`. Fail loudly if the parameter is missing or empty.
- [ ] 3.9 Edit "Set parameter overrides" step to append `BASE_PARAMS="$BASE_PARAMS,SharedLambdaRoleArn=${SHARED_LAMBDA_ROLE_ARN}"` **outside** the master-only `if [[ " app beta alpha dev " == ... ]]` block — so both master-template and application-template branches get the parameter.
- [ ] 3.10 `aws cloudformation validate-template` on all four updated templates (master, backend, application, api, web).
- [ ] 3.11 grep audit: confirm no remaining `Fn::ImportValue.*SharedLambdaRoleArn` anywhere in `infrastructure/` or `.github/`.
- [ ] 3.12 Commit and open PR. Review notes should call out: (a) this deletes security.template, (b) the parameter is threaded through 5 files, (c) the workflow append happens in BOTH branch paths.

## 4. Roll out via deploys (Phase 3)

- [ ] 4.1 Push to dev. Watch the CI deploy:
  - `SecurityStack` deletion in event log
  - `ApiStack` and `WebStack` updates (parameter change → resource updates)
  - ECS task definitions re-rendered with the new `TaskRoleArn`
  - Fargate rolling deployment on the web service
  - UI tests should still pass (the new role has byte-identical policies)
- [ ] 4.2 After dev is green, verify: `aws cloudformation list-exports --region us-east-1 --query "Exports[?Name=='SharedLambdaRoleArn-dev']"` returns empty (the export is gone).
- [ ] 4.3 Push to alpha → watch deploy → verify export gone.
- [ ] 4.4 Push to beta → same.
- [ ] 4.5 Push to app → same. Coordinate with stakeholders given the ECS rolling deploy is on prod web service.
- [ ] 4.6 Push a feature branch (any branch other than `dev`/`alpha`/`beta`/`app`) to verify the application-template path picks up the new role ARN via the workflow's BASE_PARAMS append. Confirm the feature branch's `ApiStack`/`WebStack` resources receive `SharedLambdaRoleArn=arn:aws:iam::...` and deploy successfully.

## 5. Validation + cleanup

- [ ] 5.1 `openspec validate move-shared-lambda-role-to-bootstrap --strict` and resolve any issues.
- [ ] 5.2 Verify each `## Requirement` scenario from `specs/shared-lambda-role-management/spec.md` against the deployed system:
  - Role `${AWS::StackName}-shared-lambda-execution-role` exists exactly once in IAM.
  - SSM parameter resolves in both regions.
  - All four legacy exports (`SharedLambdaRoleArn-dev/alpha/beta/app`) are gone.
  - `infrastructure/security.template` does not exist; `backend.template` does not contain `SecurityStack`.
  - Random per-env IAM roles (`bootstrap-appcloud-system-SharedLambdaExecutionRole-*`) are gone from IAM.
  - `grep -r "Fn::ImportValue.*SharedLambdaRoleArn" infrastructure/` returns empty.
- [ ] 5.3 Drop the §9.1 follow-up bullet from [centralize-aurora-kms-keys/tasks.md](../centralize-aurora-kms-keys/tasks.md) (this change is the implementation). The §9.2 (Retain) and §9.3 (obsolete) bullets stay.
- [ ] 5.4 Archive this change per the experimental workflow (`/opsx:archive`).
