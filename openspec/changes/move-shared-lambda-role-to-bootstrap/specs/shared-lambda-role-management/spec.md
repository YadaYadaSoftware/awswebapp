## ADDED Requirements

### Requirement: Single bootstrap-owned `SharedLambdaExecutionRole` per account

The `bootstrap` stack SHALL create exactly one `AWS::IAM::Role` named `taskmanager-shared-lambda-execution-role` per AWS account, gated on the `IsPrimary` condition (the primary-region deploy creates it; IAM is global so the replica region does not). The role's `AssumeRolePolicyDocument` SHALL allow `lambda.amazonaws.com` and `ecs-tasks.amazonaws.com` to assume it (matching the legacy `security.template` policy). The role's inline policies SHALL be byte-identical to those in the legacy `security.template`'s `SharedLambdaExecutionRole`, with the same managed-policy attachments (`AWSLambdaVPCAccessExecutionRole`).

Per-env-stack `SharedLambdaExecutionRole` resources MUST NOT be created. The legacy `security.template` file is deleted as part of this change.

#### Scenario: Role exists with predictable name
- **WHEN** `bootstrap` is deployed in `us-east-1`
- **THEN** an IAM role named exactly `taskmanager-shared-lambda-execution-role` exists, with ARN `arn:aws:iam::${AWS::AccountId}:role/taskmanager-shared-lambda-execution-role`

#### Scenario: Role is not duplicated in replica region
- **WHEN** `bootstrap` is deployed in `us-west-2` (replica)
- **THEN** no new IAM role is created — IAM is global and the role already exists from the primary deploy

#### Scenario: Per-env duplication is gone
- **WHEN** any env stack (`dev`, `alpha`, `beta`, `app`) is deployed
- **THEN** the resulting stack contains zero `AWS::IAM::Role` resources with the `SharedLambdaExecutionRole` logical name

#### Scenario: security.template no longer exists
- **WHEN** the working tree is inspected for infrastructure templates
- **THEN** the file `infrastructure/security.template` does not exist; `backend.template` has no `SecurityStack` nested-stack resource

### Requirement: Role ARN is published via SSM Parameter Store

The `bootstrap` stack SHALL publish the role's ARN to AWS Systems Manager Parameter Store at the path `/taskmanager/iam/shared-lambda-role-arn` in **both** regions:
- Primary region: the SSM parameter value is `!GetAtt SharedLambdaExecutionRole.Arn`.
- Replica region: the SSM parameter value is `!Sub "arn:aws:iam::${AWS::AccountId}:role/taskmanager-shared-lambda-execution-role"` — the role's predictable ARN, since the role resource itself isn't in the replica stack.

The bootstrap stack SHALL also emit a `SharedLambdaRoleArn` output (no Export) for diagnostic visibility.

The legacy `SharedLambdaRoleArn-${BranchName}` CloudFormation Export SHALL no longer exist after the migration — `security.template` is deleted and no replacement Export is created.

#### Scenario: SSM parameter resolves in both regions
- **WHEN** the bootstrap stack is deployed in `us-east-1` and `us-west-2`
- **THEN** both `aws ssm get-parameter --name /taskmanager/iam/shared-lambda-role-arn --region us-east-1` and `--region us-west-2` return the same string, equal to the global IAM role's ARN

#### Scenario: Legacy Export is gone
- **WHEN** `aws cloudformation list-exports --region us-east-1` is run after the change has been applied to all env stacks
- **THEN** no Export named `SharedLambdaRoleArn-dev`, `SharedLambdaRoleArn-alpha`, `SharedLambdaRoleArn-beta`, or `SharedLambdaRoleArn-app` appears in the results

### Requirement: Deploy workflow looks up role ARN from SSM and passes it as a stack parameter

The deploy workflow ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)) SHALL include a "Lookup shared Lambda role ARN from SSM" step that runs after the existing KMS-key lookup step. It reads `/taskmanager/iam/shared-lambda-role-arn` from `${{ matrix.region }}` and writes the result to `$GITHUB_ENV` as `SHARED_LAMBDA_ROLE_ARN`. The workflow SHALL fail loudly if the SSM parameter is missing or empty.

The "Set parameter overrides" step SHALL append `SharedLambdaRoleArn=${SHARED_LAMBDA_ROLE_ARN}` to `BASE_PARAMS` for **both** branch paths — the master-template path (`dev`, `alpha`, `beta`, `app`) AND the application-template path (feature branches) — because both deploys' nested stacks (api.template and web.template) need the value.

#### Scenario: Workflow reads SSM and exports env var
- **WHEN** the deploy workflow runs for any branch
- **THEN** before "Set parameter overrides" runs, `$GITHUB_ENV` contains `SHARED_LAMBDA_ROLE_ARN=arn:aws:iam::ACCT:role/taskmanager-shared-lambda-execution-role`

#### Scenario: Master-template branches get the parameter
- **WHEN** the workflow deploys `dev`, `alpha`, `beta`, or `app`
- **THEN** the master-template deploy's `parameter-overrides` includes `SharedLambdaRoleArn=arn:aws:iam::ACCT:role/taskmanager-shared-lambda-execution-role`

#### Scenario: Feature-branch deploys get the parameter
- **WHEN** the workflow deploys a feature branch (application.template path)
- **THEN** the application-template deploy's `parameter-overrides` includes `SharedLambdaRoleArn=arn:aws:iam::ACCT:role/taskmanager-shared-lambda-execution-role`

#### Scenario: Missing SSM parameter fails the workflow
- **WHEN** the deploy workflow runs in a region where `/taskmanager/iam/shared-lambda-role-arn` is not set (bootstrap not yet deployed there)
- **THEN** the workflow fails before any CloudFormation deploy step, with a log message identifying the missing SSM parameter

### Requirement: Templates consume role ARN via `!Ref` parameter, not `Fn::ImportValue`

The `api.template` and `web.template` SHALL receive the role ARN as a `SharedLambdaRoleArn` parameter and reference it with `!Ref SharedLambdaRoleArn` wherever the legacy code used `Fn::ImportValue: !Sub "SharedLambdaRoleArn-${EnvironmentToImport}"`. No `Fn::ImportValue` lookups of any `SharedLambdaRoleArn-*` export SHALL remain in any template.

The `SharedLambdaRoleArn` parameter SHALL be threaded through the stack chain so api/web both receive it:
- `master.template` → `BackendStack` (`backend.template`) and `ApplicationStack` (`application.template`)
- `application.template` → `ApiStack` (`api.template`) and `WebStack` (`web.template`)

(`backend.template` accepts the parameter for diagnostic-output purposes — its nested stacks `network`/`db`/`infrastructure` don't currently consume it, but `backend.template` still has a `SharedLambdaRoleArn` output that downstream tooling may inspect.)

#### Scenario: api.template uses parameter, not import
- **WHEN** `api.template` is inspected after the change
- **THEN** the file contains `SharedLambdaRoleArn: !Ref SharedLambdaRoleArn` (or equivalent `!Ref`) and contains no `Fn::ImportValue` referencing `SharedLambdaRoleArn-*`

#### Scenario: web.template uses parameter, not import
- **WHEN** `web.template` is inspected after the change
- **THEN** both `TaskRoleArn` and `ExecutionRoleArn` (the two references identified in the legacy code) use `!Ref SharedLambdaRoleArn`, and no `Fn::ImportValue` referencing `SharedLambdaRoleArn-*` remains

#### Scenario: Feature branch's app deploy doesn't import from dev
- **WHEN** a feature branch's application.template deploys
- **THEN** the resulting `ApiStack` and `WebStack` resources receive their role ARN via the parameter chain (not via `Fn::ImportValue` from `dev`'s security stack export)
