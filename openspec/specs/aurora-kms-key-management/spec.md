# aurora-kms-key-management Specification

## Purpose
TBD - created by archiving change centralize-aurora-kms-keys. Update Purpose after archive.
## Requirements
### Requirement: Single consolidated bootstrap stack per region

The bootstrap template ([infrastructure/bootstrap.template](../../../../infrastructure/bootstrap.template)) SHALL be deployed as exactly ONE CloudFormation stack per region. The stack name SHALL equal the deployment domain with `.` replaced by `-` (e.g., `appcloud-systems` for `appcloud.systems`); it MUST NOT carry a `bootstrap-` prefix. That single stack SHALL own all of the following resources:

- Account-wide shared infrastructure: the `TemplatesBucket` (an `AWS::S3::Bucket` named `${AccountId}-${StackName}-${Region}`, e.g., `991795635857-appcloud-systems-us-east-1`) and its `TemplatesBucketPolicy`, the `WebECRRepository` for the web container, the `ApiGatewayCloudWatchLogsRole` + `ApiGatewayAccount`, and the Config rule + Lambda + SSM document for log-retention enforcement.
- Both Aurora multi-region KMS keys (`AuroraKmsKeyNonprod`, `AuroraKmsKeyProd` in the primary region; their `AWS::KMS::ReplicaKey` counterparts in the secondary region).
- Both KMS aliases — derived from `${AWS::StackName}` via `!Sub`, evaluating to `alias/${AWS::StackName}-aurora-nonprod` and `alias/${AWS::StackName}-aurora-prod` (for the `appcloud-systems` stack: `alias/appcloud-systems-aurora-nonprod` and `alias/appcloud-systems-aurora-prod`) — and both SSM parameters — derived as `/${AWS::StackName}/kms/nonprod/aurora-key-arn` and `/${AWS::StackName}/kms/prod/aurora-key-arn` — in every region.
- Both GitHub Actions IAM users (`${AWS::StackName}-GitHubActionsUser` and `${AWS::StackName}-GitHubActionsUserProd` — for the `appcloud-systems` stack: `appcloud-systems-GitHubActionsUser` and `appcloud-systems-GitHubActionsUserProd`) with their access keys and per-scope deployment policies — primary region only, since IAM is global.
- The `${AWS::StackName}-prod-kms-admin` IAM role (for the `appcloud-systems` stack: `appcloud-systems-prod-kms-admin`) — primary region only.

The template MUST NOT be deployed as separate `bootstrap-shared` / `bootstrap-prod` / `bootstrap-nonprod` instances. There is no `BootstrapScope` (or equivalent) parameter; both scopes coexist in the same stack and are differentiated by resource name. The only conditions on the template SHALL be `IsPrimary` / `IsReplica` (derived from whether `PrimaryNonprodKeyArn` is empty). There is no `HasKeyAdmin` condition or `KeyAdminPrincipalArn` parameter — the prod key uses a targeted Deny (not the rejected `NotPrincipal+Deny` pattern), so no key-admin exemption is needed; see "Production key uses restricted access policy" below.

The bootstrap template MUST NOT contain any case-sensitive occurrence of the literal `taskmanager` outside of comment lines. Every resource name that previously embedded `taskmanager` SHALL be expressed via `!Sub` referencing `${AWS::StackName}`.

#### Scenario: Primary region deploy
- **WHEN** `aws cloudformation deploy --stack-name appcloud-systems --template-file infrastructure/bootstrap.template --capabilities CAPABILITY_NAMED_IAM --region us-east-1` is run with `PrimaryNonprodKeyArn` and `PrimaryProdKeyArn` empty (default; no `TemplatesBucketName` parameter required)
- **THEN** the deploy succeeds and creates: the shared infra (S3 templates-bucket `991795635857-appcloud-systems-us-east-1` with TemplatesBucketPolicy, ECR repository named `appcloud-systems`, ApiGateway role+account, Config rule + Lambda + SSM doc), both multi-region KMS keys (nonprod with broad policy, prod with a targeted Deny on `appcloud-systems-GitHubActionsUser`), both aliases (`alias/appcloud-systems-aurora-nonprod` and `alias/appcloud-systems-aurora-prod`), both SSM parameters (`/appcloud-systems/kms/nonprod/aurora-key-arn` and `/appcloud-systems/kms/prod/aurora-key-arn`), both IAM users (`appcloud-systems-GitHubActionsUser`, `appcloud-systems-GitHubActionsUserProd`) with their access keys and policies, and the `appcloud-systems-prod-kms-admin` role

#### Scenario: Replica region deploy
- **WHEN** the same template is deployed to `us-west-2` with `--stack-name appcloud-systems` and `PrimaryNonprodKeyArn=<from us-east-1 output>` and `PrimaryProdKeyArn=<from us-east-1 output>`
- **THEN** the deploy creates: a per-region S3 templates-bucket `991795635857-appcloud-systems-us-west-2`, a per-region ECR repo, ApiGateway role+account, Config rule, both `AWS::KMS::ReplicaKey` resources (pointing at the us-east-1 primary keys), both aliases (`alias/appcloud-systems-aurora-nonprod`, `alias/appcloud-systems-aurora-prod`), both SSM parameters at the `/appcloud-systems/kms/*` paths. The deploy does NOT create any IAM users (IAM is global; the users already exist from the primary-region deploy)

#### Scenario: No separate scope stacks exist
- **WHEN** `aws cloudformation list-stacks --stack-status-filter CREATE_COMPLETE UPDATE_COMPLETE --region us-east-1` is run after the migration is complete
- **THEN** the result contains a stack named `appcloud-systems` but no stacks named `bootstrap-appcloud-systems`, `bootstrap-prod`, `bootstrap-nonprod`, `bootstrap-shared`, or any name with a `bootstrap-` prefix

### Requirement: KMS keys for Aurora encryption are owned by the bootstrap stack, not per-branch env stacks

The system SHALL provision exactly two Aurora encryption KMS keys per AWS account, both in the single regional bootstrap stack (named with the dashed-domain convention, e.g., `appcloud-systems`): one nonprod key (`AuroraKmsKeyNonprod`) and one prod key (`AuroraKmsKeyProd`). Each key SHALL be a multi-region key (`AWS::KMS::Key` with `MultiRegion: true`) created in the primary region (us-east-1), with an `AWS::KMS::ReplicaKey` created in the secondary region by the same bootstrap template deployed there.

Each key SHALL have a corresponding `AWS::KMS::Alias` whose `AliasName` is derived via `!Sub` from `${AWS::StackName}`, evaluating to `alias/${AWS::StackName}-aurora-nonprod` and `alias/${AWS::StackName}-aurora-prod`.

Per-branch environment stacks (`dev`, `alpha`, `beta`, `app`, and feature branches) MUST NOT create any `AWS::KMS::Key` or `AWS::KMS::ReplicaKey` resource of their own.

#### Scenario: Two keys per account in primary region
- **WHEN** the bootstrap stack (named `appcloud-systems` for our deployment) is deployed in `us-east-1`
- **THEN** exactly two new multi-region KMS keys exist in `us-east-1`, with aliases `alias/appcloud-systems-aurora-prod` and `alias/appcloud-systems-aurora-nonprod`

#### Scenario: Replicas in secondary region
- **WHEN** the bootstrap stack is deployed in the secondary region
- **THEN** the stack creates two `AWS::KMS::ReplicaKey` resources whose `PrimaryKeyArn` values point at the corresponding keys in `us-east-1`, each with an alias derived from `${AWS::StackName}`

#### Scenario: Env stack does not own a KMS key
- **WHEN** any env stack (master.template) is deployed for any branch
- **THEN** the resulting stack contains zero resources of type `AWS::KMS::Key` or `AWS::KMS::ReplicaKey`

### Requirement: Dedicated GitHub Actions IAM user per scope

The `bootstrap` stack (primary region) SHALL create two IAM users:

- `GitHubActionsUser` (logical ID; `UserName: !Sub "${AWS::StackName}-GitHubActionsUser"`, e.g. `appcloud-systems-GitHubActionsUser`) for non-prod CI, with an `AWS::IAM::AccessKey` and a `DeploymentPolicy` granting the broad set of AWS permissions needed for env-stack deploys (same shape as the legacy bootstrap stack's policy).
- `GitHubActionsUserProd` (logical ID; `UserName: !Sub "${AWS::StackName}-GitHubActionsUserProd"`, e.g. `appcloud-systems-GitHubActionsUserProd`) for prod CI, with an `AWS::IAM::AccessKey` and a `DeploymentPolicy-prod` granting the same broad set of permissions but with the KMS statement restricted to the prod key only (`Resource: !GetAtt AuroraKmsKeyProd.Arn`) and excluding destructive KMS actions (`kms:PutKeyPolicy`, `kms:ScheduleKeyDeletion`, `kms:ReplicateKey`, `kms:CreateKey`, `kms:CreateAlias`, `kms:DeleteAlias`, `kms:UpdateAlias`).

Both IAM users have explicit `UserName:` properties derived from `${AWS::StackName}`, so their ARNs are predictable (`arn:aws:iam::{account}:user/${AWS::StackName}-GitHubActionsUser` and `.../${AWS::StackName}-GitHubActionsUserProd`, e.g. `.../appcloud-systems-GitHubActionsUserProd`); the TemplatesBucketPolicy and the secondary-region KMS key policies reference them by `${AWS::StackName}`-derived ARN.

#### Scenario: Prod CI user exists
- **WHEN** the `bootstrap` stack is deployed in the primary region
- **THEN** an IAM user named `${AWS::StackName}-GitHubActionsUserProd` (e.g. `appcloud-systems-GitHubActionsUserProd`) exists with an access key, and the stack outputs `GitHubActionsUserProdAccessKeyId` and `GitHubActionsUserProdSecretAccessKey`

#### Scenario: Non-prod CI user exists in bootstrap
- **WHEN** the `bootstrap` stack is deployed in the primary region
- **THEN** an IAM user named `${AWS::StackName}-GitHubActionsUser` (e.g. `appcloud-systems-GitHubActionsUser`) exists with an access key, and the stack outputs `GitHubActionsUserAccessKeyId` and `GitHubActionsUserSecretAccessKey`

#### Scenario: Non-prod CI user cannot deploy app stack
- **WHEN** the `GitHubActionsUser` attempts `cloudformation:UpdateStack` against the `app-appcloud-systems` stack
- **THEN** the API call is denied (its `DeploymentPolicy` shape grants broad access but the `app` stack's deployments are gated by the prod KMS key policy denying it `kms:*` for any cluster operations)

### Requirement: Production key uses restricted access policy

The prod KMS key policy SHALL grant `GitHubActionsUserProd` only the operations needed to use the key for Aurora encryption (`kms:CreateGrant`, `kms:DescribeKey`, `kms:Decrypt`, `kms:Encrypt`, `kms:GenerateDataKey`, `kms:ReEncryptFrom`, `kms:ReEncryptTo`, `kms:RetireGrant`, `kms:ListGrants`, `kms:RevokeGrant`). The policy MUST NOT grant `GitHubActionsUserProd` any of: `kms:ScheduleKeyDeletion`, `kms:DisableKey`, `kms:PutKeyPolicy`, `kms:DeleteAlias`, `kms:UpdateAlias`, `kms:ReplicateKey`, `kms:CreateAlias`.

The prod key policy SHALL include an explicit `Effect: Deny` statement targeting the nonprod CI principal (`GitHubActionsUser`) — `Principal: { AWS: arn:aws:iam::ACCT:user/GitHubActionsUser }`, `Action: kms:*`, `Resource: "*"`. This denial blocks the IAM-delegation pathway that the standard `AllowAccountRoot kms:* Resource: *` statement would otherwise enable — without it, the nonprod CI user's broad `DeploymentPolicy` (`kms:*` on `Resource: "*"`) reaches the prod key.

An earlier draft used `NotPrincipal+Deny` (deny everyone except an exemption list) instead of a targeted Deny. That pattern was rejected because `NotPrincipal` with a `Service: rds.amazonaws.com` entry does **not** match the assumed-role session of RDS's service-linked role `AWSServiceRoleForRDS`, which is what actually performs encryption operations after the grant is created. The resulting Aurora cluster fails with `inaccessible-encryption-credentials`. A targeted Deny on the specific untrusted principal (`GitHubActionsUser`) achieves the security goal — nonprod CI cannot reach the prod key — without breaking any AWS-internal service-linked-role pathways.

The targeted-Deny shape also removes the need for a `KeyAdminPrincipalArn` parameter that the earlier draft required: KMS's lockout-safety check only fires when the proposed policy would block the calling principal from `kms:PutKeyPolicy`. A Deny on `GitHubActionsUser` doesn't affect the deployer (a different IAM user), so the check passes and no exemption parameter is needed.

Destructive operations on the production key SHALL be granted only to the AWS account root principal and to a dedicated IAM role named `${AWS::StackName}-prod-kms-admin` (e.g. `appcloud-systems-prod-kms-admin`) that is also created in the `bootstrap` stack (primary region).

#### Scenario: Prod CI cannot delete the prod key
- **WHEN** `GitHubActionsUserProd` calls `kms:ScheduleKeyDeletion` against the prod key
- **THEN** the API call is denied and returns `AccessDeniedException`

#### Scenario: Non-prod CI cannot reach the prod key at all
- **WHEN** `GitHubActionsUser` calls `kms:DescribeKey` against the prod key
- **THEN** the API call is denied by the prod key policy's targeted Deny on `GitHubActionsUser` and returns `AccessDeniedException` with the message "...with an explicit deny in a resource-based policy"

#### Scenario: RDS service-linked role can still encrypt for Aurora
- **WHEN** an `app`-branch deploy creates an `AWS::RDS::DBCluster` with `KmsKeyId` set to the prod key ARN, and RDS's `AWSServiceRoleForRDS` performs encryption operations on the cluster's data
- **THEN** the operations succeed — the targeted Deny names `GitHubActionsUser` only, not the SLR, so the standard grant-based encryption path works

#### Scenario: Prod CI can use the prod key for Aurora encryption
- **WHEN** an `app`-branch deploy creates an `AWS::RDS::DBCluster` with `KmsKeyId` set to the prod key ARN
- **THEN** RDS successfully calls `CreateGrant`/`GenerateDataKey` against the key under the `GitHubActionsUserProd` grant context and the cluster comes up healthy

#### Scenario: prod-kms-admin trust policy excludes CI
- **WHEN** any caller examines the `AssumeRolePolicyDocument` of `prod-kms-admin`
- **THEN** it allows the AWS account root principal but does not list any GitHub Actions IAM user as a principal

### Requirement: Non-production key keeps permissive policy

The nonprod KMS key policy SHALL grant `GitHubActionsUser` the broad set of operations the legacy `security.template` policy granted (including `kms:CreateAlias`, `kms:DeleteAlias`, `kms:UpdateAlias`, `kms:ListAliases`, `kms:CreateGrant`, `kms:Decrypt`, `kms:Encrypt`, `kms:GenerateDataKey`, `kms:ReEncryptFrom`, `kms:ReEncryptTo`, `kms:RetireGrant`, `kms:DescribeKey`, `kms:ListGrants`, `kms:RevokeGrant`). The nonprod key policy MUST NOT include a `NotPrincipal+Deny` clause — nonprod is intentionally accessible to the broad nonprod CI policy.

#### Scenario: Non-prod CI can manage nonprod aliases
- **WHEN** `GitHubActionsUser` calls `kms:CreateAlias` or `kms:DeleteAlias` against the nonprod key
- **THEN** the call succeeds

### Requirement: Branch-to-key mapping is explicit and enforced at deploy time

The deploy workflow SHALL select the Aurora KMS key as follows:

- For the `app` branch: the ARN published by the bootstrap stack at `/${dashed-domain}/kms/prod/aurora-key-arn` in the deploying region (for our deployment: `/appcloud-systems/kms/prod/aurora-key-arn`).
- For every other branch (`dev`, `alpha`, `beta`, and any feature branch): the ARN published by the bootstrap stack at `/${dashed-domain}/kms/nonprod/aurora-key-arn` in the deploying region (for our deployment: `/appcloud-systems/kms/nonprod/aurora-key-arn`).

The selection SHALL be made by reading from AWS SSM Parameter Store at paths constructed from the workflow-computed dashed-domain value:

```
/${dashed}/kms/prod/aurora-key-arn
/${dashed}/kms/nonprod/aurora-key-arn
```

where `${dashed}` is the value of `secrets.DOMAIN_NAME` with `.` replaced by `-` (computed in a single workflow step and reused in every subsequent reference). The workflow MUST NOT hardcode any specific dashed-domain value (such as `appcloud-systems`) — the SSM lookup path is always constructed by string interpolation.

#### Scenario: app branch picks prod key
- **WHEN** the deploy workflow runs on branch `app` for the `appcloud.systems` deployment
- **THEN** the `KmsKeyArn` parameter passed to master.template equals the value read from `/appcloud-systems/kms/prod/aurora-key-arn` in the matrix region

#### Scenario: dev branch picks nonprod key
- **WHEN** the deploy workflow runs on branch `dev` for the `appcloud.systems` deployment
- **THEN** the `KmsKeyArn` parameter passed to master.template equals the value read from `/appcloud-systems/kms/nonprod/aurora-key-arn` in the matrix region

#### Scenario: feature branch picks nonprod key
- **WHEN** the deploy workflow runs on a branch named `feature/login-banner` for the `appcloud.systems` deployment
- **THEN** the SSM lookup step reads `/appcloud-systems/kms/nonprod/aurora-key-arn` (the value is not consumed by application.template, but the lookup runs uniformly for all branches)

#### Scenario: Workflow contains no hardcoded dashed-domain value
- **WHEN** `Select-String -Path .github/workflows/zbuild.yml -CaseSensitive -Pattern 'appcloud-systems'` is run
- **THEN** zero matches are returned — every reference uses `${{ steps.<id>.outputs.dashed }}` constructed from `secrets.DOMAIN_NAME`

### Requirement: Branch-conditional AWS credentials in the deploy workflow

The deploy workflow SHALL select GitHub Actions AWS credentials as follows:

- For the `app` branch: `secrets.AWS_ACCESS_KEY_ID_PROD` and `secrets.AWS_SECRET_ACCESS_KEY_PROD`.
- For every other branch: the existing `secrets.AWS_ACCESS_KEY_ID` and `secrets.AWS_SECRET_ACCESS_KEY`.

The selection MUST happen before `aws-actions/configure-aws-credentials` runs, in the same deploy job. If `AWS_ACCESS_KEY_ID_PROD` or `AWS_SECRET_ACCESS_KEY_PROD` is empty on an `app` push, the workflow SHALL fail loudly (no silent fallback to non-prod credentials).

The non-prod `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub secrets in use after this change SHALL be sourced from the `GitHubActionsUser` access key in the `bootstrap` stack — not from the legacy `bootstrap` stack (which is retired).

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

The bootstrap stack SHALL publish each KMS key ARN as an `AWS::SSM::Parameter` of type `String` at a path derived from `${AWS::StackName}`:

- `!Sub "/${AWS::StackName}/kms/nonprod/aurora-key-arn"` — populated with the nonprod key ARN.
- `!Sub "/${AWS::StackName}/kms/prod/aurora-key-arn"` — populated with the prod key ARN.

For the `appcloud-systems` stack these evaluate to `/appcloud-systems/kms/nonprod/aurora-key-arn` and `/appcloud-systems/kms/prod/aurora-key-arn`.

The stack SHALL NOT create a CloudFormation Export for either key ARN, because Exports create cross-stack coupling that prevents bootstrap updates when consumers exist.

During setup and iteration of this change, the SSM parameters and the KMS key resources SHALL NOT carry `DeletionPolicy: Retain` — Retain plus the derived parameter names would cause every failed deploy to leave an orphaned parameter that blocks retry with "AlreadyExists". Adding `DeletionPolicy: Retain` back is a permitted follow-up change once databases are actually encrypting with the keys.

#### Scenario: Parameter populated on bootstrap deploy
- **WHEN** the bootstrap stack named `appcloud-systems` is deployed in `us-east-1`
- **THEN** the SSM parameters `/appcloud-systems/kms/nonprod/aurora-key-arn` and `/appcloud-systems/kms/prod/aurora-key-arn` exist in `us-east-1` and their values are the ARNs of the two multi-region keys just created

#### Scenario: No CloudFormation Export for the keys
- **WHEN** `aws cloudformation list-exports --region us-east-1` is run
- **THEN** no export named `AuroraKmsKey*Arn*` appears (other bootstrap exports such as `TemplatesBucketName` and `WebECRRepository` remain)

#### Scenario: No legacy `/taskmanager/` SSM parameters remain
- **WHEN** `aws ssm describe-parameters --parameter-filters Key=Name,Option=BeginsWith,Values=/taskmanager --region us-east-1` is run after Phase 5 cleanup
- **THEN** zero parameters are returned (the same check in `us-west-2` also returns zero)

### Requirement: KMS key lifecycle is decoupled from per-branch env stack lifecycle

Deleting any per-branch env stack (`dev`, `alpha`, `beta`, `app`, or any feature branch) SHALL leave the Aurora KMS keys untouched. The keys MUST persist as long as the `bootstrap` stack exists.

#### Scenario: Feature branch teardown leaves keys intact
- **WHEN** a feature branch's env stack is deleted (by the branch-delete cleanup workflow or by hand)
- **THEN** both KMS keys (nonprod and prod) remain in active (non-`PendingDeletion`) state, and the SSM parameters still resolve to the same ARNs

#### Scenario: dev rebuild does not create a new key
- **WHEN** the `dev` env stack is deleted and immediately redeployed
- **THEN** the redeploy reuses the existing nonprod key ARN (the SSM parameter value is unchanged) rather than creating a new KMS key

