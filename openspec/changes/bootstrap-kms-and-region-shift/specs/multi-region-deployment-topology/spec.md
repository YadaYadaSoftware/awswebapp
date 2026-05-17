## ADDED Requirements

### Requirement: Primary region is us-east-1

The system SHALL use `us-east-1` as the primary AWS region for every deployment, regardless of branch. The deploy matrix in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) SHALL set `is_primary: "true"` for `us-east-1` only.

#### Scenario: Primary region for any branch
- **WHEN** the deploy workflow runs for any branch (shared-infrastructure or feature)
- **THEN** the matrix entry with `is_primary: "true"` targets `us-east-1`

### Requirement: Secondary region is us-east-2

The system SHALL use `us-east-2` as the secondary AWS region. The environment variable `AWS_REGION_SECONDARY` in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) SHALL equal `us-east-2`. The system MUST NOT deploy any new stacks to `us-west-2`.

#### Scenario: Workflow env declares us-east-2
- **WHEN** the workflow file is parsed
- **THEN** the env block contains `AWS_REGION_SECONDARY: us-east-2` and does not contain the string `us-west-2`

#### Scenario: No us-west-2 deployment
- **WHEN** the deploy matrix is expanded for any branch
- **THEN** no matrix entry has `region: us-west-2`

### Requirement: Only app, beta, alpha branches deploy to the secondary region

The system SHALL deploy to `us-east-2` only when the branch leaf name is one of `app`, `beta`, or `alpha`. All other branches (`dev` and feature branches) SHALL deploy only to `us-east-1`. This preserves the existing single-region/multi-region distinction documented in [CLAUDE.md](../../../CLAUDE.md) and [BRANCH_MANAGEMENT_README.md](../../../BRANCH_MANAGEMENT_README.md).

#### Scenario: Multi-region branch
- **WHEN** the deploy workflow runs on branch `beta`
- **THEN** the matrix expands to two deploy jobs (`us-east-1` and `us-east-2`) and both run

#### Scenario: Single-region shared branch
- **WHEN** the deploy workflow runs on branch `dev`
- **THEN** only the `us-east-1` matrix entry's `deploy` field evaluates to `true`; the `us-east-2` job is skipped

#### Scenario: Feature branch
- **WHEN** the deploy workflow runs on a branch named `feature/login-banner`
- **THEN** only the `us-east-1` deploy runs; the `us-east-2` job is skipped

### Requirement: Aurora Global Cluster spans us-east-1 and us-east-2 for multi-region branches

For each multi-region branch (`app`, `beta`, `alpha`), the Aurora Global Cluster `taskmanager-${BranchName}-global-cluster` SHALL have exactly two regional clusters: the primary in `us-east-1` and the secondary in `us-east-2`. The system MUST NOT leave any `us-west-2` regional cluster attached to the global cluster.

#### Scenario: Global cluster membership for app
- **WHEN** `aws rds describe-global-clusters --global-cluster-identifier taskmanager-app-global-cluster` is executed after deployment completes
- **THEN** the returned `GlobalClusterMembers` list contains exactly two entries, with `DBClusterArn` values in `us-east-1` and `us-east-2`

### Requirement: SES domain identity verified in both regions

Before any deploy to `us-east-2` runs, the system SHALL have the `appcloud.systems` domain identity verified with DKIM in both `us-east-1` and `us-east-2`, mirroring the existing per-region SES setup documented in [CLAUDE.md](../../../CLAUDE.md).

#### Scenario: SES identity present
- **WHEN** `aws sesv2 get-email-identity --email-identity appcloud.systems --region us-east-2` is called
- **THEN** the response shows `DkimAttributes.Status: SUCCESS` and `VerifiedForSendingStatus: true`

### Requirement: Branch-delete cleanup region matches deploy region

The branch-deletion cleanup workflow ([.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml)) SHALL continue to operate only against `us-east-1`, because feature branches deploy only there. No `us-east-2` cleanup is performed for feature branches.

#### Scenario: Feature branch deletion stays in us-east-1
- **WHEN** the cleanup workflow fires for a deleted feature branch
- **THEN** every AWS API call it issues targets `us-east-1` and none target `us-east-2`
