## MODIFIED Requirements

### Requirement: Single consolidated bootstrap stack per region

The bootstrap template ([infrastructure/bootstrap.template](../../../../infrastructure/bootstrap.template)) SHALL be deployed as exactly ONE CloudFormation stack per region. The stack name SHALL equal the deployment domain with `.` replaced by `-` (e.g., `appcloud-systems` for `appcloud.systems`); it MUST NOT carry a `bootstrap-` prefix. That single stack SHALL own all of the following resources:

- Account-wide shared infrastructure: the `WebECRRepository` for the web container, the `ApiGatewayCloudWatchLogsRole` + `ApiGatewayAccount`, the Config rule + Lambda + SSM document for log-retention enforcement, and the `TemplatesBucketPolicy` on `cf-templates-{account}-{region}`.
- Both Aurora multi-region KMS keys (`AuroraKmsKeyNonprod`, `AuroraKmsKeyProd` in the primary region; their `AWS::KMS::ReplicaKey` counterparts in the secondary region).
- Both KMS aliases — derived from `${AWS::StackName}` via `!Sub`, evaluating to `alias/${AWS::StackName}-aurora-nonprod` and `alias/${AWS::StackName}-aurora-prod` (for the `appcloud-systems` stack: `alias/appcloud-systems-aurora-nonprod` and `alias/appcloud-systems-aurora-prod`) — and both SSM parameters — derived as `/${AWS::StackName}/kms/nonprod/aurora-key-arn` and `/${AWS::StackName}/kms/prod/aurora-key-arn` — in every region.
- Both GitHub Actions IAM users (`${AWS::StackName}-GitHubActionsUser` and `${AWS::StackName}-GitHubActionsUserProd` — for the `appcloud-systems` stack: `appcloud-systems-GitHubActionsUser` and `appcloud-systems-GitHubActionsUserProd`) with their access keys and per-scope deployment policies — primary region only, since IAM is global.
- The `${AWS::StackName}-prod-kms-admin` IAM role (for the `appcloud-systems` stack: `appcloud-systems-prod-kms-admin`) — primary region only.

The template MUST NOT be deployed as separate `bootstrap-shared` / `bootstrap-prod` / `bootstrap-nonprod` instances. There is no `BootstrapScope` (or equivalent) parameter; both scopes coexist in the same stack and are differentiated by resource name. The only conditions on the template SHALL be `IsPrimary` / `IsReplica` (derived from whether `PrimaryNonprodKeyArn` is empty) and `HasKeyAdmin` (driving the optional `KeyAdminPrincipalArn` exemption on the prod key's NotPrincipal Deny).

The bootstrap template MUST NOT contain any case-sensitive occurrence of the literal `taskmanager` outside of comment lines. Every resource name that previously embedded `taskmanager` SHALL be expressed via `!Sub` referencing `${AWS::StackName}`.

#### Scenario: Primary region deploy
- **WHEN** `aws cloudformation deploy --stack-name appcloud-systems --template-file infrastructure/bootstrap.template --parameter-overrides TemplatesBucketName=<bucket> --capabilities CAPABILITY_NAMED_IAM --region us-east-1` is run with `PrimaryNonprodKeyArn` and `PrimaryProdKeyArn` empty (default)
- **THEN** the deploy succeeds and creates: the shared infra (ECR repository named `appcloud-systems`, ApiGateway role+account, Config rule + Lambda + SSM doc, TemplatesBucketPolicy), both multi-region KMS keys (nonprod with broad policy, prod with a targeted Deny on `appcloud-systems-GitHubActionsUser`), both aliases (`alias/appcloud-systems-aurora-nonprod` and `alias/appcloud-systems-aurora-prod`), both SSM parameters (`/appcloud-systems/kms/nonprod/aurora-key-arn` and `/appcloud-systems/kms/prod/aurora-key-arn`), both IAM users (`appcloud-systems-GitHubActionsUser`, `appcloud-systems-GitHubActionsUserProd`) with their access keys and policies, and the `appcloud-systems-prod-kms-admin` role

#### Scenario: Replica region deploy
- **WHEN** the same template is deployed to `us-west-2` with `--stack-name appcloud-systems` and `PrimaryNonprodKeyArn=<from us-east-1 output>` and `PrimaryProdKeyArn=<from us-east-1 output>`
- **THEN** the deploy creates: the shared infra (a per-region copy), both `AWS::KMS::ReplicaKey` resources (pointing at the us-east-1 primary keys), both aliases (`alias/appcloud-systems-aurora-nonprod`, `alias/appcloud-systems-aurora-prod`), both SSM parameters at the `/appcloud-systems/kms/*` paths. The deploy does NOT create any IAM users (IAM is global; the users already exist from the primary-region deploy)

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
