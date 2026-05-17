## ADDED Requirements

### Requirement: KMS keys for Aurora encryption are owned by bootstrap stacks, not per-branch env stacks

The system SHALL provision exactly two Aurora encryption KMS keys per AWS account: one in the `bootstrap-prod` stack and one in the `bootstrap-nonprod` stack. Each key SHALL be a multi-region key (`AWS::KMS::Key` with `MultiRegion: true`) created in the primary region, with an `AWS::KMS::ReplicaKey` created in the current secondary region by the corresponding `bootstrap-*` stack deployed there.

Per-branch environment stacks (`dev`, `alpha`, `beta`, `app`, and feature branches) MUST NOT create any `AWS::KMS::Key` or `AWS::KMS::ReplicaKey` resource of their own.

#### Scenario: Two keys per account in primary region
- **WHEN** `bootstrap-prod` and `bootstrap-nonprod` are deployed in `us-east-1`
- **THEN** exactly two new multi-region KMS keys exist in `us-east-1`, one with the alias `alias/taskmanager-aurora-prod` and one with `alias/taskmanager-aurora-nonprod`

#### Scenario: Replicas in secondary region
- **WHEN** `bootstrap-prod` and `bootstrap-nonprod` are deployed in the current secondary region
- **THEN** each creates an `AWS::KMS::ReplicaKey` whose `PrimaryKeyArn` points at the corresponding key in `us-east-1`

#### Scenario: Env stack does not own a KMS key
- **WHEN** any env stack (master.template) is deployed for any branch
- **THEN** the resulting stack contains zero resources of type `AWS::KMS::Key` or `AWS::KMS::ReplicaKey`

### Requirement: Dedicated GitHub Actions IAM user per scope

The `bootstrap-prod` stack SHALL create an IAM user named `GitHubActionsUserProd` with its own `AWS::IAM::AccessKey`. This user SHALL have a `DeploymentPolicy-prod` IAM policy attached that grants the minimum AWS API permissions needed to deploy the `app` env stack (CloudFormation, ECS, ECR, Aurora/RDS, IAM read/write for stack-managed roles, Secrets Manager, ELBv2, ACM, Route 53, CloudWatch Logs, SSM read).

The `bootstrap-nonprod` stack SHALL create an IAM user named `GitHubActionsUser` with its own `AWS::IAM::AccessKey` and a `DeploymentPolicy` IAM policy that grants the AWS API permissions needed to deploy non-`app` env stacks (the same shape as the legacy bootstrap stack's policy). This user SHALL be used for every non-`app` branch and SHALL NOT be granted any permission on the `bootstrap-prod` KMS key.

Neither IAM user appears in the legacy `bootstrap` stack — that stack is fully retired by this change.

#### Scenario: Prod CI user exists
- **WHEN** `bootstrap-prod` is deployed
- **THEN** an IAM user named `GitHubActionsUserProd` exists with an access key, and the stack outputs `GitHubActionsUserProdAccessKeyId` and `GitHubActionsUserProdSecretAccessKey`

#### Scenario: Non-prod CI user exists in bootstrap-nonprod
- **WHEN** `bootstrap-nonprod` is deployed
- **THEN** an IAM user named `GitHubActionsUser` exists with an access key, and the stack outputs `GitHubActionsUserAccessKeyId` and `GitHubActionsUserSecretAccessKey`

#### Scenario: Non-prod CI user cannot deploy app stack
- **WHEN** the `GitHubActionsUser` (from `bootstrap-nonprod`) attempts `cloudformation:UpdateStack` against the `app-appcloud-systems` stack
- **THEN** the API call is denied

### Requirement: Production key uses restricted access policy

The `bootstrap-prod` KMS key policy SHALL grant `GitHubActionsUserProd` only the operations needed to use the key for Aurora encryption (`kms:CreateGrant`, `kms:DescribeKey`, `kms:Decrypt`, `kms:Encrypt`, `kms:GenerateDataKey`, `kms:ReEncryptFrom`, `kms:ReEncryptTo`, `kms:RetireGrant`, `kms:ListGrants`, `kms:RevokeGrant`). The policy MUST NOT grant `GitHubActionsUserProd` any of: `kms:ScheduleKeyDeletion`, `kms:DisableKey`, `kms:PutKeyPolicy`, `kms:DeleteAlias`, `kms:UpdateAlias`, `kms:ReplicateKey`, `kms:CreateAlias`.

The `GitHubActionsUser` from `bootstrap-nonprod` MUST NOT appear in the `bootstrap-prod` key policy at all.

Destructive operations on the production key SHALL be granted only to the AWS account root principal and to a dedicated IAM role named `prod-kms-admin` that is also created in the `bootstrap-prod` stack.

#### Scenario: Prod CI cannot delete the prod key
- **WHEN** `GitHubActionsUserProd` calls `kms:ScheduleKeyDeletion` against the `bootstrap-prod` key
- **THEN** the API call is denied by the key policy and returns `AccessDeniedException`

#### Scenario: Non-prod CI cannot reach the prod key at all
- **WHEN** the `GitHubActionsUser` from `bootstrap-nonprod` calls `kms:DescribeKey` against the `bootstrap-prod` key
- **THEN** the API call is denied by the key policy

#### Scenario: Prod CI can use the prod key for Aurora encryption
- **WHEN** an `app`-branch deploy creates an `AWS::RDS::DBCluster` with `KmsKeyId` set to the `bootstrap-prod` key ARN
- **THEN** RDS successfully calls `CreateGrant`/`GenerateDataKey` against the key under the `GitHubActionsUserProd` grant context and the cluster comes up healthy

#### Scenario: prod-kms-admin trust policy excludes CI
- **WHEN** any caller examines the `AssumeRolePolicyDocument` of `prod-kms-admin`
- **THEN** it allows the AWS account root principal but does not list any GitHub Actions IAM user (`GitHubActionsUser` or `GitHubActionsUserProd`) as a principal

### Requirement: Non-production key keeps permissive policy

The `bootstrap-nonprod` KMS key policy SHALL grant the `GitHubActionsUser` (created in the same `bootstrap-nonprod` stack) the same broad set of operations the current `security.template` policy grants (including `kms:CreateAlias`, `kms:DeleteAlias`, `kms:UpdateAlias`, `kms:ListAliases`, `kms:CreateGrant`, `kms:Decrypt`, `kms:Encrypt`, `kms:GenerateDataKey`, `kms:ReEncryptFrom`, `kms:ReEncryptTo`, `kms:RetireGrant`, `kms:DescribeKey`, `kms:ListGrants`, `kms:RevokeGrant`).

#### Scenario: Non-prod CI can manage nonprod aliases
- **WHEN** the `GitHubActionsUser` (from `bootstrap-nonprod`) calls `kms:CreateAlias` or `kms:DeleteAlias` against the `bootstrap-nonprod` key
- **THEN** the call succeeds

### Requirement: Branch-to-key mapping is explicit and enforced at deploy time

The deploy workflow SHALL select the Aurora KMS key as follows:

- For the `app` branch: the ARN published by `bootstrap-prod` in the deploying region.
- For every other branch (`dev`, `alpha`, `beta`, and any feature branch): the ARN published by `bootstrap-nonprod` in the deploying region.

The selection SHALL be made by reading from AWS SSM Parameter Store at well-known paths:
```
/taskmanager/kms/prod/aurora-key-arn
/taskmanager/kms/nonprod/aurora-key-arn
```
populated by the `bootstrap-prod` and `bootstrap-nonprod` stacks respectively in each region.

#### Scenario: app branch picks prod key
- **WHEN** the deploy workflow runs on branch `app`
- **THEN** the `KmsKeyArn` parameter passed to master.template equals the value read from `/taskmanager/kms/prod/aurora-key-arn` in the matrix region

#### Scenario: dev branch picks nonprod key
- **WHEN** the deploy workflow runs on branch `dev`
- **THEN** the `KmsKeyArn` parameter passed to master.template equals the value read from `/taskmanager/kms/nonprod/aurora-key-arn` in the matrix region

#### Scenario: feature branch picks nonprod key
- **WHEN** the deploy workflow runs on a branch named `feature/login-banner`
- **THEN** the `KmsKeyArn` parameter passed to application.template equals the value read from `/taskmanager/kms/nonprod/aurora-key-arn`

### Requirement: Branch-conditional AWS credentials in the deploy workflow

The deploy workflow SHALL select GitHub Actions AWS credentials as follows:

- For the `app` branch: `secrets.AWS_ACCESS_KEY_ID_PROD` and `secrets.AWS_SECRET_ACCESS_KEY_PROD`.
- For every other branch: the existing `secrets.AWS_ACCESS_KEY_ID` and `secrets.AWS_SECRET_ACCESS_KEY`.

The selection MUST happen before `aws-actions/configure-aws-credentials` runs, in the same deploy job. If `AWS_ACCESS_KEY_ID_PROD` or `AWS_SECRET_ACCESS_KEY_PROD` is empty on an `app` push, the workflow SHALL fail loudly (no silent fallback to non-prod credentials).

The non-prod `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub secrets in use after this change SHALL be sourced from the `GitHubActionsUser` access key in the `bootstrap-nonprod` stack — not from the legacy `bootstrap` stack (which is retired).

#### Scenario: app deploy uses prod credentials
- **WHEN** the deploy workflow runs on branch `app`
- **THEN** `aws-actions/configure-aws-credentials` is invoked with `aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID_PROD }}`

#### Scenario: dev deploy uses non-prod credentials
- **WHEN** the deploy workflow runs on branch `dev`
- **THEN** `aws-actions/configure-aws-credentials` is invoked with `aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}`

#### Scenario: Missing prod secret fails the build
- **WHEN** the deploy workflow runs on `app` and `secrets.AWS_ACCESS_KEY_ID_PROD` is empty
- **THEN** the workflow fails before any AWS API call is made, with a log message identifying the missing secret

#### Scenario: Cleanup workflow uses non-prod credentials
- **WHEN** the cleanup-on-branch-delete workflow runs for a feature-branch deletion
- **THEN** it uses the existing non-prod `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` (the workflow never runs for `app`/`beta`/`alpha`/`dev`, so no prod-credential path is needed)

### Requirement: KMS key ARN is exposed via SSM Parameter Store, not CloudFormation Exports

Each `bootstrap-prod` / `bootstrap-nonprod` stack SHALL publish its KMS key ARN as an `AWS::SSM::Parameter` of type `String` at the path specified above, in the region of the stack. The SSM parameter SHALL have `DeletionPolicy: Retain` so accidental stack deletion does not orphan downstream consumers.

The stacks SHALL NOT create a CloudFormation Export for the key ARN, because Exports create cross-stack coupling that prevents bootstrap updates when consumers exist.

#### Scenario: Parameter populated on bootstrap deploy
- **WHEN** `bootstrap-nonprod` is deployed in `us-east-1`
- **THEN** an SSM parameter `/taskmanager/kms/nonprod/aurora-key-arn` exists in `us-east-1` and its value is the ARN of the multi-region key just created

#### Scenario: No CloudFormation Export for the key
- **WHEN** `aws cloudformation list-exports --region us-east-1` is run
- **THEN** no export named `AuroraKmsKeyArn-*` appears (other bootstrap exports such as `GitHubActionsUserArn` remain)

### Requirement: KMS key lifecycle is decoupled from per-branch env stack lifecycle

Deleting any per-branch env stack (`dev`, `alpha`, `beta`, `app`, or any feature branch) SHALL leave the Aurora KMS keys untouched. The keys MUST persist as long as the corresponding `bootstrap-prod` or `bootstrap-nonprod` stack exists.

#### Scenario: Feature branch teardown leaves keys intact
- **WHEN** a feature branch's env stack is deleted (by the branch-delete cleanup workflow or by hand)
- **THEN** both `bootstrap-prod` and `bootstrap-nonprod` KMS keys remain in active (non-`PendingDeletion`) state, and the SSM parameters still resolve to the same ARNs

#### Scenario: dev rebuild does not create a new key
- **WHEN** the `dev` env stack is deleted and immediately redeployed
- **THEN** the redeploy reuses the existing `bootstrap-nonprod` key ARN (the SSM parameter value is unchanged) rather than creating a new KMS key
